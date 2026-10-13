using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ServarrAPI.Test
{
    public sealed class FakeGithub : HttpMessageHandler
    {
        public const string ApiUrl = "https://github.test/";

        public List<object> Releases { get; } = new ();

        public List<string> Downloads { get; } = new ();

        public static string AssetUrl(string name) => $"https://downloads.test/{name}";

        public static byte[] AssetBytes(string name) => Encoding.UTF8.GetBytes(name);

        public void AddRelease(string version, bool prerelease, params string[] assets) =>
            Releases.Insert(0, new
            {
                tag_name = $"v{version}",
                prerelease,
                published_at = DateTimeOffset.UtcNow.AddMinutes(Releases.Count),
                body = "* New: Something by @someone in https://github.com/Cularr/Ocularr/pull/1",
                assets = assets.Select(x => new { name = x, browser_download_url = AssetUrl(x) }).ToArray()
            });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;

            if (uri.Host == "github.test" && uri.AbsolutePath == "/repos/Cularr/Ocularr/releases")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(Releases), Encoding.UTF8, "application/json")
                });
            }

            if (uri.Host == "downloads.test")
            {
                lock (Downloads)
                {
                    Downloads.Add(uri.AbsolutePath.TrimStart('/'));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(AssetBytes(uri.AbsolutePath.TrimStart('/')))
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
        }
    }
}
