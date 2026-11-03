using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Ocularr.Update.API.OAuth;

namespace Ocularr.Update.API.Test
{
    public static class OAuthService
    {
        public const string Callback = "https://ocularr.home.test:8787/oauth.html";
        public const string RedirectUrl = "https://system.test/v1/oauth/mangabaka/auth";

        public static ServiceFactory Create(FakeTokenEndpoint tokens, Dictionary<string, string> settings = null, System.Action<IServiceCollection> services = null)
        {
            var all = new Dictionary<string, string>
            {
                ["OAuth:Providers:mangabaka:AuthorizeUrl"] = "https://auth.test/authorize",
                ["OAuth:Providers:mangabaka:TokenUrl"] = "https://auth.test/token",
                ["OAuth:Providers:mangabaka:ClientId"] = "client",
                ["OAuth:Providers:mangabaka:ClientSecret"] = "secret",
                ["OAuth:Providers:mangabaka:Scope"] = "library.read offline_access",
                ["OAuth:Providers:mangabaka:RedirectUrl"] = RedirectUrl,
                ["OAuth:Providers:other:AuthorizeUrl"] = "https://other.test/authorize",
                ["OAuth:Providers:other:TokenUrl"] = "https://other.test/token",
                ["OAuth:Providers:other:ClientId"] = "other",
                ["OAuth:Providers:other:RedirectUrl"] = "https://system.test/v1/oauth/other/auth"
            };

            foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            {
                all[key] = value;
            }

            return new ServiceFactory(all, x =>
            {
                x.AddHttpClient(nameof(OAuthController)).ConfigurePrimaryHttpMessageHandler(() => tokens);
                services?.Invoke(x);
            });
        }

        public static HttpClient Client(this ServiceFactory factory) =>
            factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        public static Dictionary<string, string> Query(string url) =>
            QueryHelpers.ParseQuery(new System.Uri(url).Query).ToDictionary(x => x.Key, x => x.Value.ToString());
    }
}
