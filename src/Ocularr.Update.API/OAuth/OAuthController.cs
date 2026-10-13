using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityModel.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Ocularr.Update.API.Util;

namespace Ocularr.Update.API.OAuth
{
    public class OAuthToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; }
    }

    [ApiController]
    [Route("v1/oauth/{provider}")]
    [EnableRateLimiting(RateLimitPolicy)]
    public class OAuthController : ControllerBase
    {
        public const string RateLimitPolicy = "oauth";
        public const string NonceCookie = "ocularr_oauth";

        private readonly OAuthOptions _options;
        private readonly OAuthStateProtector _states;
        private readonly IHttpClientFactory _httpClientFactory;

        public OAuthController(IOptions<OAuthOptions> options, OAuthStateProtector states, IHttpClientFactory httpClientFactory)
        {
            _options = options.Value;
            _states = states;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("authorize")]
        public IActionResult Authorize([FromRoute] string provider, [FromQuery] string state)
        {
            if (!_options.Providers.TryGetValue(provider, out var settings))
            {
                return NotFound();
            }

            if (!OAuthStateProtector.IsCallback(state))
            {
                return Page(400, "The address to return to is not an Ocularr sign-in page.");
            }

            var verifier = settings.Pkce == PkceMethod.None ? null : Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

            var url = new RequestUrl(settings.AuthorizeUrl).CreateAuthorizeUrl(
                clientId: settings.ClientId,
                responseType: "code",
                scope: settings.Scope,
                redirectUri: settings.RedirectUrl,
                state: _states.Protect(new OAuthState { Provider = provider, Callback = state, Verifier = verifier, Nonce = nonce }),
                codeChallenge: settings.Pkce == PkceMethod.S256 ? Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) : verifier,
                codeChallengeMethod: settings.Pkce == PkceMethod.None ? null : settings.Pkce == PkceMethod.S256 ? "S256" : "plain");

            Response.Cookies.Append(NonceCookie, nonce, NonceCookieOptions());
            NoStore();
            return Redirect(url);
        }

        [HttpGet("auth")]
        public async Task<IActionResult> Auth([FromRoute] string provider, [FromQuery] string code, [FromQuery] string state, [FromQuery] string error, CancellationToken cancellationToken)
        {
            if (!_options.Providers.TryGetValue(provider, out var settings))
            {
                return NotFound();
            }

            var saved = _states.Unprotect(state);
            var nonce = Request.Cookies[NonceCookie];
            Response.Cookies.Delete(NonceCookie, NonceCookieOptions());

            if (saved == null || !string.Equals(saved.Provider, provider, StringComparison.OrdinalIgnoreCase) || !ApiKeyAttribute.Matches(saved.Nonce, nonce))
            {
                return Page(400, "This sign-in has expired or is invalid. Start it again from Ocularr.");
            }

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            {
                return Page(400, "The sign-in was cancelled or refused. Start it again from Ocularr.");
            }

            var response = await Client().RequestAuthorizationCodeTokenAsync(
                new AuthorizationCodeTokenRequest
                {
                    Address = settings.TokenUrl,
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret,
                    ClientCredentialStyle = CredentialStyle(settings),
                    Code = code,
                    RedirectUri = settings.RedirectUrl,
                    CodeVerifier = saved.Verifier
                },
                cancellationToken);

            if (response.IsError || string.IsNullOrEmpty(response.AccessToken))
            {
                return Page(502, "The sign-in could not be completed. Start it again from Ocularr.");
            }

            var link = QueryHelpers.AddQueryString(saved.Callback, new Dictionary<string, string>
            {
                ["access_token"] = response.AccessToken,
                ["expires_in"] = response.ExpiresIn.ToString(CultureInfo.InvariantCulture),
                ["refresh_token"] = response.RefreshToken ?? string.Empty
            });

            return Page(200, "Signed in. Continue only if you started this sign-in from your Ocularr at this address:", link);
        }

        [HttpGet("renew")]
        public async Task<IActionResult> Renew([FromRoute] string provider, [FromQuery(Name = "refresh_token")][Required] string refreshToken, CancellationToken cancellationToken)
        {
            if (!_options.Providers.TryGetValue(provider, out var settings))
            {
                return NotFound();
            }

            var response = await Client().RequestRefreshTokenAsync(
                new RefreshTokenRequest
                {
                    Address = settings.TokenUrl,
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret,
                    ClientCredentialStyle = CredentialStyle(settings),
                    RefreshToken = refreshToken
                },
                cancellationToken);

            NoStore();

            if (response.IsError)
            {
                return response.ErrorType == ResponseErrorType.Protocol ? BadRequest(new { error = response.Error }) : StatusCode(502);
            }

            return Ok(new OAuthToken
            {
                AccessToken = response.AccessToken,
                TokenType = response.TokenType,
                ExpiresIn = response.ExpiresIn,
                RefreshToken = response.RefreshToken
            });
        }

        private static ClientCredentialStyle CredentialStyle(OAuthProvider settings) =>
            settings.ClientAuthentication == ClientAuthentication.Post ? ClientCredentialStyle.PostBody : ClientCredentialStyle.AuthorizationHeader;

        private HttpClient Client() => _httpClientFactory.CreateClient(nameof(OAuthController));

        private CookieOptions NonceCookieOptions() => new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = Request.PathBase.Add("/v1/oauth").Value,
            MaxAge = OAuthStateProtector.Lifetime
        };

        private void NoStore()
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
        }

        private ContentResult Page(int statusCode, string message, string link = null)
        {
            NoStore();
            Response.Headers.ContentSecurityPolicy = "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers.XFrameOptions = "DENY";
            Response.Headers.XContentTypeOptions = "nosniff";

            var html = HtmlEncoder.Default;
            var continueLink = link == null
                ? string.Empty
                : $"<p><a href=\"{html.Encode(link)}\">Continue to {html.Encode(new Uri(link).Authority)}</a></p>";

            return new ContentResult
            {
                StatusCode = statusCode,
                ContentType = "text/html; charset=utf-8",
                Content = $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Ocularr sign-in</title></head><body><p>{html.Encode(message)}</p>{continueLink}</body></html>"
            };
        }
    }
}
