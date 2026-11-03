using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Ocularr.Update.API.OAuth;

namespace Ocularr.Update.API.Test
{
    [TestFixture]
    public class OAuthFailureFixture
    {
        private FakeTokenEndpoint _tokens;
        private RecordingLogger<OAuthController> _logger;

        [SetUp]
        public void SetUp()
        {
            _tokens = new FakeTokenEndpoint();
            _logger = new RecordingLogger<OAuthController>();
        }

        [TestCase("{\"error\":\"invalid_request\",\"error_description\":\"The request is malformed.\",\"hint\":\"Check the `client_id` parameter\"}", "invalid_request: Check the `client_id` parameter")]
        [TestCase("{\"error\":\"invalid_grant\",\"error_description\":\"The code expired.\"}", "invalid_grant: The code expired.")]
        [TestCase("{\"error\":\"invalid_client\"}", "invalid_client")]
        [TestCase("<html>", "HTTP 400")]
        public async Task a_failed_exchange_shows_and_logs_the_provider_error_with_a_reference(string body, string detail)
        {
            _tokens.StatusCode = HttpStatusCode.BadRequest;
            _tokens.Body = body;
            using var factory = Create();
            var client = factory.Client();

            var response = await SignIn(client);
            var page = await response.Content.ReadAsStringAsync();
            var reference = Regex.Match(page, "Reference ([^<]+)<").Groups[1].Value;
            var warning = _logger.Entries.Single(x => x.Level == LogLevel.Warning).Message;

            Assert.Multiple(() =>
            {
                Assert.That(page, Does.Contain($"<code class=\"detail\">{WebUtility.HtmlEncode(detail)}</code>"));
                Assert.That(reference, Is.Not.Empty);
                Assert.That(response.Headers.GetValues("X-Request-Id").Single(), Is.EqualTo(reference));
                Assert.That(warning, Does.Contain("mangabaka"));
                Assert.That(warning, Does.Contain("400"));
                Assert.That(warning, Does.Contain(detail));
                Assert.That(warning, Does.Contain(reference));
            });
        }

        [Test]
        public async Task the_provider_error_is_html_encoded_and_shortened()
        {
            _tokens.StatusCode = HttpStatusCode.BadRequest;
            _tokens.Body = $"{{\"error\":\"invalid_request\",\"error_description\":\"<script>alert(1)</script>{new string('x', 500)}\"}}";
            using var factory = Create();

            var page = await (await SignIn(factory.Client())).Content.ReadAsStringAsync();
            var detail = Regex.Match(page, "<code class=\"detail\">([^<]*)</code>").Groups[1].Value;

            Assert.Multiple(() =>
            {
                Assert.That(page, Does.Not.Contain("<script>alert"));
                Assert.That(WebUtility.HtmlDecode(detail), Has.Length.EqualTo(300));
            });
        }

        [TestCase(HttpStatusCode.InternalServerError, "[]", "HTTP 500")]
        [TestCase(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"hint\":\"expired\\r\\n[12:00:00 INF] forged\"}", "invalid_grant: expired  [12:00:00 INF] forged")]
        [TestCase(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\",\"error_description\":\"got secret and abc\"}", "invalid_client: got *** and ***")]
        public async Task the_provider_error_is_made_safe_for_the_page_and_the_log(HttpStatusCode status, string body, string detail)
        {
            _tokens.StatusCode = status;
            _tokens.Body = body;
            using var factory = Create();

            var page = await (await SignIn(factory.Client())).Content.ReadAsStringAsync();
            var warning = _logger.Entries.Single(x => x.Level == LogLevel.Warning).Message;

            Assert.Multiple(() =>
            {
                Assert.That(page, Does.Contain($"<code class=\"detail\">{WebUtility.HtmlEncode(detail)}</code>"));
                Assert.That(warning, Does.Contain(detail));
                Assert.That(warning, Does.Not.Contain("\n").And.Not.Contain("\r"));
            });
        }

        [Test]
        public async Task overlapping_secrets_are_fully_redacted()
        {
            _tokens.StatusCode = HttpStatusCode.BadRequest;
            _tokens.Body = "{\"error\":\"invalid_grant\",\"error_description\":\"abc\"}";
            using var factory = OAuthService.Create(_tokens, new Dictionary<string, string> { ["OAuth:Providers:mangabaka:ClientSecret"] = "ab" }, x => x.AddSingleton<ILogger<OAuthController>>(_logger));

            var page = await (await SignIn(factory.Client())).Content.ReadAsStringAsync();

            Assert.That(page, Does.Contain("<code class=\"detail\">invalid_grant: ***</code>"));
        }

        [TestCase("")]
        [TestCase("Connection refused")]
        public async Task an_unreachable_provider_shows_a_detail_and_reference(string message)
        {
            _tokens.Failure = new HttpRequestException(message);
            using var factory = Create();

            var response = await SignIn(factory.Client());
            var page = await response.Content.ReadAsStringAsync();

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(page, Does.Match("<code class=\"detail\">[^<]+</code>"));
                Assert.That(page, Does.Contain($"Reference {response.Headers.GetValues("X-Request-Id").Single()}<"));
            });
        }

        [Test]
        public async Task a_failed_renewal_is_logged_with_the_provider_error_and_reference()
        {
            _tokens.StatusCode = HttpStatusCode.InternalServerError;
            _tokens.Body = "{\"error\":\"server_error\",\"error_description\":\"Database down\"}";
            using var factory = Create();

            var response = await factory.Client().GetAsync("/v1/oauth/mangabaka/renew?refresh_token=old");
            var warning = _logger.Entries.Single(x => x.Level == LogLevel.Warning).Message;

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(warning, Does.Contain("mangabaka"));
                Assert.That(warning, Does.Contain("server_error: Database down"));
                Assert.That(warning, Does.Contain(response.Headers.GetValues("X-Request-Id").Single()));
            });
        }

        private static async Task<HttpResponseMessage> SignIn(HttpClient client)
        {
            var authorize = await client.GetAsync($"/v1/oauth/mangabaka/authorize?state={Uri.EscapeDataString(OAuthService.Callback)}");
            var state = OAuthService.Query(authorize.Headers.Location.ToString())["state"];
            return await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(state)}");
        }

        private ServiceFactory Create() =>
            OAuthService.Create(_tokens, services: x => x.AddSingleton<ILogger<OAuthController>>(_logger));
    }
}
