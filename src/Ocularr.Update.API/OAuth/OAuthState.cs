using System;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Ocularr.Update.API.OAuth
{
    public class OAuthState
    {
        public string Provider { get; init; }
        public string Callback { get; init; }
        public string Verifier { get; init; }
        public string Nonce { get; init; }
    }

    public sealed class OAuthStateProtector
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        private readonly ITimeLimitedDataProtector _protector;

        public OAuthStateProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("Ocularr.Update.API.OAuth.State").ToTimeLimitedDataProtector();
        }

        public string Protect(OAuthState state) => _protector.Protect(JsonSerializer.Serialize(state), Lifetime);

        public OAuthState Unprotect(string protectedState)
        {
            if (string.IsNullOrEmpty(protectedState))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<OAuthState>(_protector.Unprotect(protectedState));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
            {
                return null;
            }
        }

        public static bool IsCallback(string callback) =>
            Uri.TryCreate(callback, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) &&
            uri.AbsolutePath.EndsWith("/oauth.html", StringComparison.Ordinal);
    }
}
