using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Octokit;
using ServarrAPI.Extensions;
using ServarrAPI.Model;
using ServarrAPI.Util;

namespace ServarrAPI.Release.Github
{
    public class GithubReleaseSource : ReleaseSourceBase
    {
        private static readonly Regex ReleaseFeaturesGroup = new(@"^\*\s+(?:[0-9a-f]{40}\s+)?(?:New:|\(?feat(?:\([^)]*\))?\)?!?:)\s*(?<text>.*?)(?:\s+by\s+@\S+\s+in\s+https://\S+)?\r*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex ReleaseFixesGroup = new(@"^\*\s+(?:[0-9a-f]{40}\s+)?(?:Fix(?:ed)?:|\(?fix(?:\([^)]*\))?\)?!?:)\s*(?<text>.*?)(?:\s+by\s+@\S+\s+in\s+https://\S+)?\r*$", RegexOptions.Compiled | RegexOptions.Multiline);

        private readonly Config _config;
        private readonly IUpdateService _updateService;
        private readonly IUpdateFileService _updateFileService;
        private readonly GitHubClient _gitHubClient;
        private readonly HttpClient _httpClient;

        public GithubReleaseSource(IUpdateService updateService,
                                   IUpdateFileService updateFileService,
                                   GitHubClient gitHubClient,
                                   IHttpClientFactory httpClientFactory,
                                   IOptions<Config> config)
        {
            _updateService = updateService;
            _updateFileService = updateFileService;
            _config = config.Value;
            _gitHubClient = gitHubClient;
            _httpClient = httpClientFactory.CreateClient(nameof(GithubReleaseSource));
        }

        public static GitHubClient CreateClient(Config config)
        {
            var productHeader = new ProductHeaderValue("ServarrAPI");
            var client = string.IsNullOrWhiteSpace(config.GithubUrl)
                ? new GitHubClient(productHeader)
                : new GitHubClient(productHeader, new Uri(config.GithubUrl));

            if (!string.IsNullOrWhiteSpace(config.GithubToken))
            {
                client.Credentials = new Credentials(config.GithubToken);
            }

            return client;
        }

        public static void ConfigureDownloads(HttpClient client, Config config)
        {
            client.DefaultRequestHeaders.Add("User-Agent", "ServarrAPI");

            if (!string.IsNullOrWhiteSpace(config.GithubToken))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"token {config.GithubToken}");
            }
        }

        protected override async Task<List<string>> DoFetchReleasesAsync()
        {
            var updated = new HashSet<string>();

            var githubOrg = _config.GithubOrg ?? _config.Project;
            var releases = await _gitHubClient.Repository.Release.GetAll(
                githubOrg,
                _config.Project,
                new ApiOptions
                {
                    PageSize = 100,
                    PageCount = 1
                })
                .ConfigureAwait(false);

            var validReleases = releases.ToArray()
                .Where(r => r.PublishedAt.HasValue && r.TagName.StartsWith("v") && VersionUtil.IsValid(r.TagName.Substring(1)))
                .OrderByDescending(r => r.PublishedAt)
                .Take(3)
                .Reverse()
                .ToArray();

            foreach (var release in validReleases)
            {
                var version = release.TagName.Substring(1);

                // determine the branch
                var branch = release.Prerelease ? "develop" : "master";

                if (await ProcessRelease(release, branch, version))
                {
                    updated.Add(branch);
                }

                // releases on master should also appear on develop
                if (branch == "master" && await ProcessRelease(release, "develop", version))
                {
                    updated.Add("develop");
                }
            }

            return updated.ToList();
        }

        private async Task<bool> ProcessRelease(Octokit.Release release, string branch, string version)
        {
            var isNewRelease = false;

            // Get an updateEntity
            var updateEntity = await _updateService.Find(version, branch).ConfigureAwait(false);

            if (updateEntity != null)
            {
                var stored = await _updateFileService.FileNames(updateEntity.Id).ConfigureAwait(false);
                var missing = release.Assets.Where(x => !stored.Contains(x.Name) && Parser.ParseOS(x.Name).HasValue).ToList();

                await Task.WhenAll(missing.Select(x => ProcessAsset(x, branch, updateEntity.Id)));

                return missing.Count > 0;
            }

            var parsedVersion = Version.Parse(version);

            // Create update object
            updateEntity = new UpdateEntity
            {
                Version = version,
                IntVersion = parsedVersion.ToIntVersion(),
                ReleaseDate = release.PublishedAt.Value.UtcDateTime,
                Branch = branch
            };

            // Set new release to true.
            isNewRelease = true;

            // Parse changes
            updateEntity.New = Features(release.Body);
            updateEntity.Fixed = Fixes(release.Body);

            await _updateService.Insert(updateEntity).ConfigureAwait(false);

            // Process release files.
            await Task.WhenAll(release.Assets.Select(x => ProcessAsset(x, branch, updateEntity.Id)));

            return isNewRelease;
        }

        public static List<string> Features(string releaseBody) => Notes(ReleaseFeaturesGroup, releaseBody);

        public static List<string> Fixes(string releaseBody) => Notes(ReleaseFixesGroup, releaseBody);

        private static List<string> Notes(Regex regex, string releaseBody) =>
            regex.Matches(releaseBody ?? string.Empty).Select(x => x.Groups["text"].Value).ToList();

        private async Task ProcessAsset(ReleaseAsset releaseAsset, string branch, int updateId)
        {
            var operatingSystem = Parser.ParseOS(releaseAsset.Name);
            if (!operatingSystem.HasValue)
            {
                return;
            }

            var runtime = Parser.ParseRuntime(releaseAsset.Name);
            var arch = Parser.ParseArchitecture(releaseAsset.Name);
            var installer = Parser.ParseInstaller(releaseAsset.Name);

            // Calculate the hash of the zip file.
            var releaseZip = Path.Combine(_config.DataDirectory, branch.ToLowerInvariant(), releaseAsset.Name);

            try
            {
                if (!File.Exists(releaseZip))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(releaseZip));

                    await using var fileStream = File.OpenWrite(releaseZip);
                    await using var artifactStream = await _httpClient.GetStreamAsync(releaseAsset.BrowserDownloadUrl);
                    await artifactStream.CopyToAsync(fileStream);
                }

                string releaseHash;
                await using (var stream = File.OpenRead(releaseZip))
                using (var sha = SHA256.Create())
                {
                    releaseHash = BitConverter.ToString(await sha.ComputeHashAsync(stream)).Replace("-", "").ToLowerInvariant();
                }

                // Add to database.
                var updateFile = new UpdateFileEntity
                {
                    UpdateId = updateId,
                    OperatingSystem = operatingSystem.Value,
                    Architecture = arch,
                    Runtime = runtime,
                    Filename = releaseAsset.Name,
                    Url = releaseAsset.BrowserDownloadUrl,
                    Hash = releaseHash,
                    Installer = installer
                };

                await _updateFileService.Insert(updateFile).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(releaseZip))
                {
                    File.Delete(releaseZip);
                }
            }
        }
    }
}
