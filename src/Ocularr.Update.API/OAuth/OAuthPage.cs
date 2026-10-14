using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Ocularr.Update.API.OAuth
{
    public sealed class OAuthPage
    {
        public const string AssetsPath = "/v1/oauth/assets";
        public const string AssetsNamespace = "Ocularr.Update.API.OAuth.Assets";

        private static readonly string Template = Load();

        private static readonly Regex Placeholder = new (@"\{\{([a-z]+)\}\}");

        public string Provider { get; init; }

        public string IconUrl { get; init; }

        public string Title { get; init; }

        public string Message { get; init; }

        public string Address { get; init; }

        public string Link { get; init; }

        public string ContentSecurityPolicy =>
            $"default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'{(string.IsNullOrEmpty(IconUrl) ? string.Empty : " " + new Uri(IconUrl).GetLeftPart(UriPartial.Authority))}; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

        public string Render(string pathBase)
        {
            var html = HtmlEncoder.Default;
            var signedIn = Link != null;
            var page = Template;

            page = Section(page, "signed-in", signedIn);
            page = Section(page, "failed", !signedIn);
            page = Section(page, "icon", !string.IsNullOrEmpty(IconUrl));
            page = Section(page, "monogram", string.IsNullOrEmpty(IconUrl));

            var values = new Dictionary<string, string>
            {
                ["assets"] = pathBase + AssetsPath,
                ["state"] = signedIn ? "signed-in" : "failed",
                ["icon"] = IconUrl,
                ["initial"] = Provider[..1].ToUpperInvariant(),
                ["provider"] = Provider,
                ["title"] = Title,
                ["message"] = Message,
                ["address"] = Address,
                ["link"] = Link
            };

            return Placeholder.Replace(page, x => html.Encode(values[x.Groups[1].Value] ?? string.Empty));
        }

        private static string Section(string page, string name, bool keep) =>
            Regex.Replace(page, $@"\{{\{{#{name}\}}\}}(.*?)\{{\{{/{name}\}}\}}", x => keep ? x.Groups[1].Value : string.Empty, RegexOptions.Singleline);

        private static string Load()
        {
            using var stream = typeof(OAuthPage).Assembly.GetManifestResourceStream("Ocularr.Update.API.OAuth.oauth.html");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
