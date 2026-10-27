using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityModel;
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
                return Page(400, provider, settings, "Not an Ocularr address", "The address to return to is not an Ocularr sign-in page. Start the sign-in again from Ocularr.");
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
                codeChallengeMethod: settings.Pkce == PkceMethod.None ? null : settings.Pkce == PkceMethod.S256 ? "S256" : "plain",
                extra: string.IsNullOrEmpty(settings.Resource) ? null : new Parameters { { OidcConstants.AuthorizeRequest.Resource, settings.Resource } });

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
                return Page(400, provider, settings, "Sign-in expired", "This sign-in has expired or is invalid. Start it again from Ocularr.");
            }

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            {
                return Page(400, provider, settings, "Sign-in cancelled", "The sign-in was cancelled or refused. Start it again from Ocularr.");
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
                    CodeVerifier = saved.Verifier,
                    Resource = Resources(settings)
                },
                cancellationToken);

            if (response.IsError || string.IsNullOrEmpty(response.AccessToken))
            {
                return Page(502, provider, settings, "Sign-in failed", "The sign-in could not be completed. Start it again from Ocularr.");
            }

            var link = QueryHelpers.AddQueryString(saved.Callback, new Dictionary<string, string>
            {
                ["access_token"] = response.AccessToken,
                ["expires_in"] = response.ExpiresIn.ToString(CultureInfo.InvariantCulture),
                ["refresh_token"] = response.RefreshToken ?? string.Empty
            });

            return Page(200, provider, settings, "Signed in", "Continue only if you started this sign-in from your Ocularr at this address:", saved.Callback, link);
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
                    RefreshToken = refreshToken,
                    Resource = Resources(settings)
                },
                cancellationToken);

            NoStore();

            if (response.IsError)
            {
                var error = response.TryGet(OidcConstants.TokenResponse.Error);

                return response.ErrorType == ResponseErrorType.Protocol || IsRejectedToken(response.HttpStatusCode, error) ? BadRequest(new { error }) : StatusCode(502);
            }

            return Ok(new OAuthToken
            {
                AccessToken = response.AccessToken,
                TokenType = response.TokenType,
                ExpiresIn = response.ExpiresIn,
                RefreshToken = response.RefreshToken
            });
        }

        private static HashSet<string> Resources(OAuthProvider settings) =>
            string.IsNullOrEmpty(settings.Resource) ? new () : new () { settings.Resource };

        private static bool IsRejectedToken(HttpStatusCode status, string error) =>
            status == HttpStatusCode.Unauthorized && error is OidcConstants.TokenErrors.InvalidGrant or OidcConstants.TokenErrors.InvalidRequest or OidcConstants.ProtectedResourceErrors.InvalidToken;

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

        private ContentResult Page(int statusCode, string provider, OAuthProvider settings, string title, string message, string callback = null, string link = null)
        {
            var page = new OAuthPage
            {
                Provider = string.IsNullOrWhiteSpace(settings.Name) ? provider : settings.Name,
                IconUrl = settings.IconUrl,
                Title = title,
                Message = message,
                Address = callback,
                Link = link
            };

            NoStore();
            Response.Headers.ContentSecurityPolicy = page.ContentSecurityPolicy;
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers.XFrameOptions = "DENY";
            Response.Headers.XContentTypeOptions = "nosniff";

            return new ContentResult
            {
                StatusCode = statusCode,
                ContentType = "text/html; charset=utf-8",
                Content = page.Render(Request.PathBase)
            };
        }
    }
}
