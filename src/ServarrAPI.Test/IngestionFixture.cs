using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Octokit;
using Octokit.Internal;
using ServarrAPI.Release;
using ServarrAPI.Release.Github;
using ServarrAPI.TaskQueue;

namespace ServarrAPI.Test
{
    [TestFixture]
    [NonParallelizable]
    public class IngestionFixture
    {
        private const string Linux = "Ocularr.main.{0}.linux-x64.tar.gz";
        private const string Windows = "Ocularr.main.{0}.win-x64.zip";

        private TestDatabase _database;
        private FakeGithub _github;
        private ServiceFactory _factory;

        [SetUp]
        public void SetUp()
        {
            _database = new TestDatabase();
            _github = new FakeGithub();
            _factory = new ServiceFactory(
                new Dictionary<string, string>
                {
                    ["ConnectionStrings:Database"] = _database.ConnectionString,
                    ["Update:GithubOrg"] = "Cularr",
                    ["Update:BranchRedirects:main"] = "master"
                },
                x =>
                {
                    x.AddSingleton(new GitHubClient(new Connection(new ProductHeaderValue("test"), new Uri(FakeGithub.ApiUrl), new InMemoryCredentialStore(Credentials.Anonymous), new HttpClientAdapter(() => _github), new SimpleJsonSerializer())));
                    x.AddHttpClient(nameof(GithubReleaseSource)).ConfigurePrimaryHttpMessageHandler(() => _github);
                });
        }

        [TearDown]
        public void TearDown()
        {
            _factory?.Dispose();
            _database?.Dispose();
        }

        [Test]
        public async Task a_stable_release_is_offered_on_master_and_develop()
        {
            _github.AddRelease("5.0.0.2", false, string.Format(Linux, "5.0.0.2"));

            await Refresh();

            Assert.Multiple(async () =>
            {
                Assert.That(await Latest("master"), Is.EqualTo("5.0.0.2"));
                Assert.That(await Latest("develop"), Is.EqualTo("5.0.0.2"));
            });
        }

        [Test]
        public async Task a_prerelease_is_offered_on_develop_only()
        {
            _github.AddRelease("5.0.0.2", false, string.Format(Linux, "5.0.0.2"));
            _github.AddRelease("5.0.0.3", true, string.Format(Linux, "5.0.0.3"));

            await Refresh();

            Assert.Multiple(async () =>
            {
                Assert.That(await Latest("master"), Is.EqualTo("5.0.0.2"));
                Assert.That(await Latest("develop"), Is.EqualTo("5.0.0.3"));
            });
        }

        [Test]
        public async Task the_main_branch_is_served_from_master()
        {
            _github.AddRelease("5.0.0.2", false, string.Format(Linux, "5.0.0.2"));

            await Refresh();

            Assert.That(await Latest("main"), Is.EqualTo("5.0.0.2"));
        }

        [TestCase("osx", "Arm64", "Ocularr.main.5.0.0.2.osx-arm64.tar.gz")]
        [TestCase("osx", "X64", "Ocularr.main.5.0.0.2.osx-x64.tar.gz")]
        [TestCase("bsd", "X64", "Ocularr.main.5.0.0.2.freebsd-x64.tar.gz")]
        [TestCase("bsd", "Arm64", null)]
        [TestCase("windows", "X64", "Ocularr.main.5.0.0.2.win-x64.zip")]
        public async Task every_platform_gets_the_file_for_its_architecture(string os, string arch, string expected)
        {
            _github.AddRelease("5.0.0.2", false, "Ocularr.main.5.0.0.2.osx-x64.tar.gz", "Ocularr.main.5.0.0.2.osx-arm64.tar.gz", "Ocularr.main.5.0.0.2.freebsd-x64.tar.gz", "Ocularr.main.5.0.0.2.win-x64.zip", "Ocularr.main.5.0.0.2.win-x64-installer.exe");

            await Refresh();
            var package = await Package("master", os, arch: arch);

            Assert.That(package.ValueKind == JsonValueKind.Undefined ? null : package.GetProperty("filename").GetString(), Is.EqualTo(expected));
        }

        [Test]
        public async Task a_partial_import_is_completed_on_the_next_refresh()
        {
            _github.AddRelease("5.0.0.2", false, string.Format(Linux, "5.0.0.2"));
            await Refresh();
            _github.Releases.Clear();
            _github.AddRelease("5.0.0.2", false, string.Format(Linux, "5.0.0.2"), string.Format(Windows, "5.0.0.2"), "Ocularr.main.5.0.0.2.unknown.txt");

            var repaired = await Fetch();
            var again = await Fetch();

            var update = await Package("master", "windows");
            Assert.Multiple(() =>
            {
                Assert.That(repaired, Is.EquivalentTo(new[] { "master", "develop" }));
                Assert.That(again, Is.Empty);
                Assert.That(update.GetProperty("version").GetString(), Is.EqualTo("5.0.0.2"));
                Assert.That(update.GetProperty("hash").GetString(), Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(FakeGithub.AssetBytes(string.Format(Windows, "5.0.0.2"))))));
                Assert.That(_github.Downloads.Count(x => x == string.Format(Linux, "5.0.0.2")), Is.EqualTo(2));
                Assert.That(_github.Downloads.Count(x => x == string.Format(Windows, "5.0.0.2")), Is.EqualTo(2));
            });
        }

        [Test]
        public async Task the_refresh_service_queues_a_github_refresh_at_start()
        {
            var queue = new RecordingQueue();
            using var factory = _factory.WithWebHostBuilder(x => x
                .ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string> { ["Update:RefreshInterval"] = "03:00:00" }))
                .ConfigureServices(s => s.AddSingleton<IBackgroundTaskQueue>(queue)));
            _github.AddRelease("5.0.0.2", false, string.Format(Linux, "5.0.0.2"));

            factory.CreateClient();
            await (await queue.First)(default);

            Assert.That(await Latest("master", factory.CreateClient()), Is.EqualTo("5.0.0.2"));
        }

        [Test]
        public async Task the_refresh_service_can_be_turned_off()
        {
            var queue = new RecordingQueue();
            using var factory = _factory.WithWebHostBuilder(x => x.ConfigureServices(s => s.AddSingleton<IBackgroundTaskQueue>(queue)));

            factory.CreateClient();
            await Task.Delay(200);

            Assert.Multiple(() =>
            {
                Assert.That(queue.Count, Is.Zero);
                Assert.That(factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested, Is.False);
            });
        }

        [TestCase(null, "https://api.github.com/")]
        [TestCase("https://github.test/", "https://github.test/api/v3/")]
        public void the_github_url_is_configurable(string githubUrl, string expected)
        {
            var client = GithubReleaseSource.CreateClient(new Config { GithubUrl = githubUrl, GithubToken = "token" });

            Assert.Multiple(() =>
            {
                Assert.That(client.Connection.BaseAddress, Is.EqualTo(new Uri(expected)));
                Assert.That(client.Credentials.Password, Is.EqualTo("token"));
            });
        }

        private async Task<List<string>> Fetch()
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<GithubReleaseSource>().FetchReleasesAsync();
        }

        private async Task Refresh()
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<GithubReleaseSource>().FetchReleasesAsync();
        }

        private async Task<string> Latest(string branch, HttpClient client = null)
        {
            var package = await Package(branch, "linux", client);
            return package.ValueKind == JsonValueKind.Undefined ? null : package.GetProperty("version").GetString();
        }

        private async Task<JsonElement> Package(string branch, string os, HttpClient client = null, string arch = "X64")
        {
            client ??= _factory.CreateClient();
            var response = await client.GetFromJsonAsync<JsonElement>($"/v1/update/{branch}?version=5.0.0.1&os={os}&runtime=netcore&arch={arch}");
            return response.TryGetProperty("updatePackage", out var package) ? package : default;
        }
    }
}
