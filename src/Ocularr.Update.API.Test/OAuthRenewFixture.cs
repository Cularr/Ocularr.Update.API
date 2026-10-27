using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Ocularr.Update.API.Test
{
    [TestFixture]
    public class OAuthRenewFixture
    {
        [Test]
        public async Task renew_returns_the_new_tokens()
        {
            var tokens = new FakeTokenEndpoint();
            using var factory = OAuthService.Create(tokens);

            var response = await factory.Client().GetAsync("/v1/oauth/mangabaka/renew?refresh_token=old%20token");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var request = tokens.Requests.Single();

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Headers.CacheControl.NoStore, Is.True);
                Assert.That(request.Form["grant_type"], Is.EqualTo("refresh_token"));
                Assert.That(request.Form["refresh_token"], Is.EqualTo("old token"));
                Assert.That(request.Authorization, Does.StartWith("Basic "));
                Assert.That(body.GetProperty("access_token").GetString(), Is.EqualTo("access&1"));
                Assert.That(body.GetProperty("token_type").GetString(), Is.EqualTo("Bearer"));
                Assert.That(body.GetProperty("expires_in").GetInt32(), Is.EqualTo(3600));
                Assert.That(body.GetProperty("refresh_token").GetString(), Is.EqualTo("refresh 1"));
            });
        }

        [TestCase("https://api.test/v1", "https://api.test/v1")]
        [TestCase(null, null)]
        public async Task renew_sends_the_resource(string resource, string expected)
        {
            var tokens = new FakeTokenEndpoint();
            using var factory = OAuthService.Create(tokens, new Dictionary<string, string> { ["OAuth:Providers:mangabaka:Resource"] = resource });

            await factory.Client().GetAsync("/v1/oauth/mangabaka/renew?refresh_token=old");

            Assert.That(tokens.Requests.Single().Form.GetValueOrDefault("resource"), Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("?refresh_token=")]
        public async Task renew_requires_a_refresh_token(string query)
        {
            var tokens = new FakeTokenEndpoint();
            using var factory = OAuthService.Create(tokens);

            var response = await factory.Client().GetAsync($"/v1/oauth/mangabaka/renew{query}");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(tokens.Requests, Is.Empty);
            });
        }

        [TestCase(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest, "invalid_grant")]
        [TestCase(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest, "invalid_grant")]
        [TestCase(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_request\",\"message\":\"The refresh token is invalid.\"}", HttpStatusCode.BadRequest, "invalid_request")]
        [TestCase(HttpStatusCode.Unauthorized, "{\"message\":\"Invalid refresh token\",\"error\":\"invalid_token\",\"error_description\":\"Invalid refresh token\"}", HttpStatusCode.BadRequest, "invalid_token")]
        [TestCase(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}", HttpStatusCode.BadGateway, null)]
        [TestCase(HttpStatusCode.Unauthorized, "<html>", HttpStatusCode.BadGateway, null)]
        [TestCase(HttpStatusCode.Forbidden, "{\"error\":\"invalid_grant\"}", HttpStatusCode.BadGateway, null)]
        [TestCase(HttpStatusCode.InternalServerError, "{\"error\":\"server_error\"}", HttpStatusCode.BadGateway, null)]
        [TestCase(HttpStatusCode.BadGateway, "<html>", HttpStatusCode.BadGateway, null)]
        public async Task upstream_failures_are_reported(HttpStatusCode upstream, string body, HttpStatusCode expected, string expectedError)
        {
            var tokens = new FakeTokenEndpoint { StatusCode = upstream, Body = body };
            using var factory = OAuthService.Create(tokens);

            var response = await factory.Client().GetAsync("/v1/oauth/mangabaka/renew?refresh_token=old");
            var content = await response.Content.ReadAsStringAsync();

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(expected));

                if (expectedError == null)
                {
                    Assert.That(content, Does.Not.Contain("\"error\""));
                }
                else
                {
                    Assert.That(JsonDocument.Parse(content).RootElement.GetProperty("error").GetString(), Is.EqualTo(expectedError));
                }
            });
        }

        [Test]
        public async Task oauth_requests_are_rate_limited()
        {
            using var factory = OAuthService.Create(new FakeTokenEndpoint());
            var client = factory.Client();

            var statuses = new HttpStatusCode[11];
            for (var i = 0; i < statuses.Length; i++)
            {
                statuses[i] = (await client.GetAsync("/v1/oauth/mangabaka/renew?refresh_token=old")).StatusCode;
            }

            Assert.Multiple(async () =>
            {
                Assert.That(statuses.Take(10), Is.All.EqualTo(HttpStatusCode.OK));
                Assert.That(statuses[10], Is.EqualTo(HttpStatusCode.TooManyRequests));
                Assert.That((await client.GetAsync("/v1/ping")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }
    }
}
