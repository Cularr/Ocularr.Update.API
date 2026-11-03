using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Ocularr.Update.API.OAuth;

namespace Ocularr.Update.API.Test
{
    [TestFixture]
    public class OAuthSignInFixture
    {
        private FakeTokenEndpoint _tokens;

        [SetUp]
        public void SetUp() => _tokens = new FakeTokenEndpoint();

        [Test]
        public async Task authorize_sends_the_user_to_the_provider_with_pkce()
        {
            using var factory = OAuthService.Create(_tokens);

            var response = await factory.Client().GetAsync($"/v1/oauth/mangabaka/authorize?state={Uri.EscapeDataString(OAuthService.Callback)}&redirect_uri=https%3A%2F%2Fevil.test%2F");
            var location = response.Headers.Location.ToString();
            var query = OAuthService.Query(location);
            var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Ocularr.Update.API.OAuth.State").ToTimeLimitedDataProtector();
            var saved = JsonSerializer.Deserialize<OAuthState>(protector.Unprotect(query["state"], out var expiration));

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                Assert.That(location, Does.StartWith("https://auth.test/authorize?"));
                Assert.That(query["client_id"], Is.EqualTo("client"));
                Assert.That(query["response_type"], Is.EqualTo("code"));
                Assert.That(query["scope"], Is.EqualTo("library.read offline_access"));
                Assert.That(query["redirect_uri"], Is.EqualTo(OAuthService.RedirectUrl));
                Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
                Assert.That(query["code_challenge"], Has.Length.EqualTo(43));
                Assert.That(query["state"], Does.Not.Contain("ocularr.home.test"));
                Assert.That(saved.Callback, Is.EqualTo(OAuthService.Callback));
                Assert.That(expiration, Is.EqualTo(DateTimeOffset.UtcNow.AddMinutes(10)).Within(TimeSpan.FromMinutes(1)));
                Assert.That(response.Headers.CacheControl.NoStore, Is.True);
                Assert.That(response.Headers.GetValues("Set-Cookie").Single(), Does.StartWith($"{OAuthController.NonceCookie}={saved.Nonce};").And.Contains("path=/v1/oauth").And.Contains("samesite=lax").And.Contains("httponly"));
            });
        }

        [Test]
        public async Task plain_pkce_sends_the_verifier_as_the_challenge()
        {
            using var factory = OAuthService.Create(_tokens, new Dictionary<string, string> { ["OAuth:Providers:mangabaka:Pkce"] = "Plain" });
            var client = factory.Client();
            var query = await Authorize(client, "mangabaka");

            await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(query["state"])}");

            Assert.Multiple(() =>
            {
                Assert.That(query["code_challenge_method"], Is.EqualTo("plain"));
                Assert.That(_tokens.Requests.Single().Form["code_verifier"], Is.EqualTo(query["code_challenge"]));
            });
        }

        [TestCase("another browser")]
        [TestCase("a later sign-in")]
        [TestCase("a replay")]
        public async Task the_sign_in_must_finish_in_the_browser_that_started_it(string kind)
        {
            using var factory = OAuthService.Create(_tokens);
            var client = factory.Client();
            var state = (await Authorize(client, "mangabaka"))["state"];

            if (kind == "a later sign-in")
            {
                await Authorize(client, "mangabaka");
            }

            if (kind == "a replay")
            {
                await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(state)}");
                _tokens.Requests.Clear();
            }

            var response = await (kind == "another browser" ? factory.Client() : client).GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(state)}");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(_tokens.Requests, Is.Empty);
            });
        }

        [TestCase("")]
        [TestCase("oauth.html")]
        [TestCase("/oauth.html")]
        [TestCase("javascript:alert(1)//oauth.html")]
        [TestCase("ftp://ocularr.test/oauth.html")]
        [TestCase("https://user:pass@ocularr.test/oauth.html")]
        [TestCase("https://ocularr.test/oauth.html?x=1")]
        [TestCase("https://ocularr.test/oauth.html#x")]
        [TestCase("https://ocularr.test/oauth.html.evil.test/")]
        [TestCase("https://ocularr.test/login")]
        public async Task authorize_refuses_a_callback_that_is_not_an_oauth_page(string callback)
        {
            using var factory = OAuthService.Create(_tokens);

            var response = await factory.Client().GetAsync($"/v1/oauth/mangabaka/authorize?state={Uri.EscapeDataString(callback)}");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(response.Headers.Location, Is.Null);
            });
        }

        [TestCase("authorize?state=https%3A%2F%2Focularr.test%2Foauth.html")]
        [TestCase("auth?code=abc&state=x")]
        [TestCase("renew?refresh_token=x")]
        public async Task an_unknown_provider_is_not_found(string path)
        {
            using var factory = OAuthService.Create(_tokens);

            var response = await factory.Client().GetAsync($"/v1/oauth/anilist/{path}");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task the_code_is_exchanged_and_the_tokens_are_handed_to_the_callback()
        {
            using var factory = OAuthService.Create(_tokens);
            var client = factory.Client();
            var query = await Authorize(client, "mangabaka");

            var response = await client.GetAsync($"/v1/oauth/MangaBaka/auth?code=abc&state={Uri.EscapeDataString(query["state"])}");
            var page = await response.Content.ReadAsStringAsync();
            var request = _tokens.Requests.Single();
            var link = WebUtility.HtmlDecode(page.Split("id=\"continue\" class=\"button\" href=\"")[1].Split('"')[0]);

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(request.Url, Is.EqualTo("https://auth.test/token"));
                Assert.That(request.Authorization, Is.EqualTo("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("client:secret"))));
                Assert.That(request.Form["grant_type"], Is.EqualTo("authorization_code"));
                Assert.That(request.Form["code"], Is.EqualTo("abc"));
                Assert.That(request.Form["redirect_uri"], Is.EqualTo(OAuthService.RedirectUrl));
                Assert.That(Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(request.Form["code_verifier"]))), Is.EqualTo(query["code_challenge"]));
                Assert.That(request.Form.ContainsKey("client_secret"), Is.False);
                Assert.That(link, Does.StartWith(OAuthService.Callback + "?"));
                Assert.That(OAuthService.Query(link), Is.EquivalentTo(new Dictionary<string, string> { ["access_token"] = "access&1", ["expires_in"] = "3600", ["refresh_token"] = "refresh 1" }));
                Assert.That(page, Does.Contain("<code class=\"address\">https://ocularr.home.test:8787/oauth.html</code>"));
                Assert.That(page, Does.Contain("id=\"close\""));
                Assert.That(page, Does.Not.Contain("access&1"));
                Assert.That(page, Does.Contain("&amp;expires_in=3600&amp;"));
                Assert.That(response.Headers.CacheControl.NoStore, Is.True);
                Assert.That(response.Headers.GetValues("Content-Security-Policy").Single(), Does.StartWith("default-src 'none'"));
                Assert.That(response.Headers.GetValues("Referrer-Policy").Single(), Is.EqualTo("no-referrer"));
                Assert.That(response.Headers.GetValues("X-Frame-Options").Single(), Is.EqualTo("DENY"));
                Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
            });
        }

        [Test]
        public async Task the_page_shows_the_provider_and_allows_only_its_own_assets_and_icon_host()
        {
            using var factory = OAuthService.Create(_tokens, new Dictionary<string, string> { ["OAuth:Providers:mangabaka:Name"] = "MangaBaka", ["OAuth:Providers:mangabaka:IconUrl"] = "https://icons.test/baka.svg" });
            var client = factory.Client();
            var query = await Authorize(client, "mangabaka");

            var response = await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(query["state"])}");
            var page = await response.Content.ReadAsStringAsync();
            var csp = response.Headers.GetValues("Content-Security-Policy").Single();

            Assert.Multiple(() =>
            {
                Assert.That(page, Does.Contain("MangaBaka &rarr; Ocularr"));
                Assert.That(page, Does.Contain("<img src=\"https://icons.test/baka.svg\""));
                Assert.That(page, Does.Contain("<link rel=\"icon\" type=\"image/svg+xml\" href=\"/v1/oauth/assets/gutter.svg\">"));
                Assert.That(page, Does.Not.Contain("<style"));
                Assert.That(page, Does.Not.Contain("{{"));
                Assert.That(csp, Does.Contain("script-src 'self'; style-src 'self'; img-src 'self' https://icons.test;"));
                Assert.That(csp, Does.Not.Contain("unsafe"));
            });
        }

        [Test]
        public async Task a_placeholder_in_the_callback_address_is_not_filled_in()
        {
            const string callback = "https://ocularr.home.test/{{link}}/oauth.html";
            using var factory = OAuthService.Create(_tokens);
            var client = factory.Client();
            var authorize = await client.GetAsync($"/v1/oauth/mangabaka/authorize?state={Uri.EscapeDataString(callback)}");
            var state = OAuthService.Query(authorize.Headers.Location.ToString())["state"];

            var page = await (await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(state)}")).Content.ReadAsStringAsync();

            Assert.That(page, Does.Contain($"<code class=\"address\">{callback}</code>"));
        }

        [Test]
        public async Task a_failed_sign_in_page_has_no_continue_link()
        {
            using var factory = OAuthService.Create(_tokens);
            var client = factory.Client();
            var state = (await Authorize(client, "mangabaka"))["state"];

            var response = await client.GetAsync($"/v1/oauth/mangabaka/auth?error=access_denied&state={Uri.EscapeDataString(state)}");
            var page = await response.Content.ReadAsStringAsync();

            Assert.Multiple(() =>
            {
                Assert.That(page, Does.Contain("Sign-in cancelled"));
                Assert.That(page, Does.Contain("mangabaka &rarr; Ocularr"));
                Assert.That(page, Does.Not.Contain("id=\"continue\""));
                Assert.That(response.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("img-src 'self';"));
            });
        }

        [TestCase("oauth.css", "text/css")]
        [TestCase("oauth.js", "text/javascript")]
        [TestCase("gutter.svg", "image/svg+xml")]
        [TestCase("ocularr.svg", "image/svg+xml")]
        public async Task the_page_assets_are_served(string file, string type)
        {
            using var factory = OAuthService.Create(_tokens);

            var response = await factory.Client().GetAsync($"/v1/oauth/assets/{file}");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType.MediaType, Is.EqualTo(type));
                Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
            });
        }

        [Test]
        public async Task the_page_template_is_not_served()
        {
            using var factory = OAuthService.Create(_tokens);

            var response = await factory.Client().GetAsync("/v1/oauth/assets/oauth.html");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [TestCase("https://api.test/v1", "https://api.test/v1")]
        [TestCase(null, null)]
        public async Task the_resource_is_sent_when_signing_in(string resource, string expected)
        {
            using var factory = OAuthService.Create(_tokens, new Dictionary<string, string> { ["OAuth:Providers:mangabaka:Resource"] = resource });
            var client = factory.Client();
            var query = await Authorize(client, "mangabaka");

            await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(query["state"])}");

            Assert.Multiple(() =>
            {
                Assert.That(query.GetValueOrDefault("resource"), Is.EqualTo(expected));
                Assert.That(_tokens.Requests.Single().Form.GetValueOrDefault("resource"), Is.EqualTo(expected));
            });
        }

        [Test]
        public async Task post_client_authentication_sends_the_secret_in_the_body()
        {
            using var factory = OAuthService.Create(_tokens, new Dictionary<string, string> { ["OAuth:Providers:mangabaka:ClientAuthentication"] = "Post", ["OAuth:Providers:mangabaka:Pkce"] = "None" });
            var client = factory.Client();
            var query = await Authorize(client, "mangabaka");

            await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(query["state"])}");
            var request = _tokens.Requests.Single();

            Assert.Multiple(() =>
            {
                Assert.That(request.Authorization, Is.Null);
                Assert.That(request.Form["client_id"], Is.EqualTo("client"));
                Assert.That(request.Form["client_secret"], Is.EqualTo("secret"));
                Assert.That(request.Form.ContainsKey("code_verifier"), Is.False);
                Assert.That(query.ContainsKey("code_challenge"), Is.False);
            });
        }

        [TestCase("tampered")]
        [TestCase("other")]
        [TestCase("expired")]
        [TestCase("missing")]
        public async Task an_invalid_state_is_refused_without_a_token_request(string kind)
        {
            using var factory = OAuthService.Create(_tokens);
            var client = factory.Client();
            var state = (await Authorize(client, kind == "other" ? "other" : "mangabaka"))["state"];
            var protector = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Ocularr.Update.API.OAuth.State").ToTimeLimitedDataProtector();

            state = kind switch
            {
                "tampered" => state[..^4] + (state.EndsWith("AAAA") ? "BBBB" : "AAAA"),
                "expired" => protector.Protect(protector.Unprotect(state), DateTimeOffset.UtcNow.AddSeconds(-1)),
                "missing" => string.Empty,
                _ => state
            };

            var response = await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(state)}");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(_tokens.Requests, Is.Empty);
            });
        }

        [TestCase("error=access_denied&code=abc")]
        [TestCase("")]
        public async Task a_refused_sign_in_makes_no_token_request(string parameters)
        {
            using var factory = OAuthService.Create(_tokens);
            var client = factory.Client();
            var state = (await Authorize(client, "mangabaka"))["state"];

            var response = await client.GetAsync($"/v1/oauth/mangabaka/auth?{parameters}&state={Uri.EscapeDataString(state)}");

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(_tokens.Requests, Is.Empty);
            });
        }

        [TestCase(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}")]
        [TestCase(HttpStatusCode.OK, "{\"token_type\":\"Bearer\"}")]
        [TestCase(HttpStatusCode.InternalServerError, "oops")]
        public async Task a_failed_exchange_shows_the_failure_page_with_a_status_proxies_pass_through(HttpStatusCode status, string body)
        {
            _tokens.StatusCode = status;
            _tokens.Body = body;
            using var factory = OAuthService.Create(_tokens);
            var client = factory.Client();
            var state = (await Authorize(client, "mangabaka"))["state"];

            var response = await client.GetAsync($"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(state)}");

            Assert.Multiple(async () =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Sign-in failed"));
            });
        }

        [Test]
        public async Task state_survives_a_restart_with_the_same_data_directory()
        {
            using var first = OAuthService.Create(_tokens);
            var authorize = await first.Client().GetAsync($"/v1/oauth/mangabaka/authorize?state={Uri.EscapeDataString(OAuthService.Callback)}");
            var state = OAuthService.Query(authorize.Headers.Location.ToString())["state"];
            using var second = OAuthService.Create(_tokens, new Dictionary<string, string> { ["Update:DataDirectory"] = first.DataDirectory });
            var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/oauth/mangabaka/auth?code=abc&state={Uri.EscapeDataString(state)}");
            request.Headers.Add("Cookie", authorize.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);

            var response = await second.Client().SendAsync(request);

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(Directory.GetFiles(Path.Combine(first.DataDirectory, "keys"), "key-*.xml"), Is.Not.Empty);
            });
        }

        [TestCase("OAuth:Providers:mangabaka:TokenUrl", "")]
        [TestCase("OAuth:Providers:mangabaka:ClientId", "")]
        [TestCase("OAuth:Providers:mangabaka:RedirectUrl", "not a url")]
        [TestCase("OAuth:Providers:mangabaka:IconUrl", "http://icons.test/baka.svg")]
        [TestCase("OAuth:Providers:mangabaka:IconUrl", "not a url")]
        public void an_incomplete_provider_stops_the_service_from_starting(string key, string value)
        {
            using var factory = OAuthService.Create(_tokens, new Dictionary<string, string> { [key] = value });

            var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
            Assert.That(exception.Message, Does.Contain("OAuth:Providers:mangabaka"));
        }

        private static async Task<Dictionary<string, string>> Authorize(HttpClient client, string provider)
        {
            var response = await client.GetAsync($"/v1/oauth/{provider}/authorize?state={Uri.EscapeDataString(OAuthService.Callback)}");
            return OAuthService.Query(response.Headers.Location.ToString());
        }
    }
}
