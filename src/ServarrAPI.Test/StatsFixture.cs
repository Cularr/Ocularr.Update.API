using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using ServarrAPI.Model;
using ServarrAPI.Stats;
using Architecture = System.Runtime.InteropServices.Architecture;
using OperatingSystem = ServarrAPI.Model.OperatingSystem;

namespace ServarrAPI.Test
{
    [TestFixture]
    public class StatsFixture
    {
        private const string UserAgent = "Ocularr/5.0.0.100 (ubuntu 24.04)";

        [TestCase("&active=true", "True")]
        [TestCase("&active=false", "False")]
        [TestCase("", null)]
        public async Task usage_is_recorded_only_when_the_client_sends_active(string active, string expected)
        {
            var stats = new RecordingStats();
            using var factory = new ServiceFactory(services: x => x
                .AddSingleton<IUsageStats>(stats)
                .AddSingleton<IUpdateFileService, EmptyUpdateFiles>());
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            var response = await client.GetAsync($"/v1/update/master?version=5.0.0.100&os=linux&runtime=netcore&runtimeVer=10.0.0&arch=X64&dbType=PostgreSQL{active}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(stats.Tags.Select(x => x["activeinstall"]), Is.EqualTo(expected == null ? Array.Empty<string>() : new[] { expected }));
        }

        [Test]
        public async Task usage_tags_name_the_ocularr_install()
        {
            var stats = new RecordingStats();
            using var factory = new ServiceFactory(services: x => x
                .AddSingleton<IUsageStats>(stats)
                .AddSingleton<IUpdateFileService, EmptyUpdateFiles>());
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            await client.GetAsync("/v1/update/master?version=5.0.0.100&os=linux&runtime=netcore&runtimeVer=10.0.0&arch=Arm64&dbType=PostgreSQL&active=true");

            Assert.That(stats.Tags.Single(), Is.SupersetOf(new Dictionary<string, string>
            {
                ["program"] = "Ocularr",
                ["requestApp"] = "ocularr",
                ["branch"] = "master",
                ["version"] = "5.0.0.100",
                ["os"] = "Linux",
                ["osName"] = "ubuntu",
                ["osVersion"] = "24.04",
                ["runtime"] = "NetCore",
                ["runtimeVersion"] = "10.0.0",
                ["arch"] = "Arm64",
                ["dbType"] = "PostgreSQL"
            }));
        }

        [Test]
        public async Task development_builds_are_not_recorded()
        {
            var stats = new RecordingStats();
            using var factory = new ServiceFactory(services: x => x
                .AddSingleton<IUsageStats>(stats)
                .AddSingleton<IUpdateFileService, EmptyUpdateFiles>());
            using var client = factory.CreateClient();

            await client.GetAsync("/v1/update/master?version=10.0.0.1&os=linux&runtime=netcore&arch=X64&active=true");

            Assert.That(stats.Tags, Is.Empty);
        }

        [Test]
        public async Task points_are_written_as_line_protocol()
        {
            var port = FreePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var received = ReceiveAsync(listener);

            using (var stats = new UsageStats(Options.Create(new StatsOptions { Url = $"http://127.0.0.1:{port}", Token = "user:pass", Org = "-", Bucket = "telegraf" })))
            {
                stats.Write("192.0.2.1", new Dictionary<string, string> { ["program"] = "Ocularr", ["runtimeVersion"] = null, ["osVersion"] = string.Empty });
            }

            var (query, authorization, body) = await received.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(query, Does.Contain("bucket=telegraf"));
                Assert.That(authorization, Is.EqualTo("Token user:pass"));
                Assert.That(body, Does.StartWith("userstats,program=Ocularr source=\"192.0.2.1\" "));
            });
        }

        [Test]
        public void an_empty_url_disables_stats()
        {
            using var stats = new UsageStats(Options.Create(new StatsOptions()));

            Assert.DoesNotThrow(() => stats.Write("192.0.2.1", new Dictionary<string, string>()));
        }

        private static async Task<(string Query, string Authorization, string Body)> ReceiveAsync(HttpListener listener)
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            context.Response.StatusCode = 204;
            context.Response.Close();
            return (context.Request.Url.Query, context.Request.Headers["Authorization"], body);
        }

        private static int FreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }

        private sealed class RecordingStats : IUsageStats
        {
            public List<IReadOnlyDictionary<string, string>> Tags { get; } = new ();

            public void Write(string source, IReadOnlyDictionary<string, string> tags) => Tags.Add(tags);
        }

        private sealed class EmptyUpdateFiles : IUpdateFileService
        {
            public Task<UpdateFileEntity> Insert(UpdateFileEntity model) => throw new NotSupportedException();

            public Task<List<string>> FileNames(int updateId) => throw new NotSupportedException();

            public Task<UpdateFileEntity> Find(string version, string branch, OperatingSystem os, Runtime runtime, Architecture arch, bool installer) => Task.FromResult<UpdateFileEntity>(null);

            public Task<List<UpdateFileEntity>> Find(string branch, OperatingSystem os, Runtime runtime, Architecture arch, bool installer, int count, string installedVersion = null, string runtimeVersion = null, bool excludeMajorVersions = false) => Task.FromResult(new List<UpdateFileEntity>());
        }
    }
}
