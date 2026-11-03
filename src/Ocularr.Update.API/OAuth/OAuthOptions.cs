using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Ocularr.Update.API.OAuth
{
    public enum ClientAuthentication
    {
        Basic,
        Post
    }

    public enum PkceMethod
    {
        S256,
        Plain,
        None
    }

    public enum IconShape
    {
        Round,
        Fill,
        Square,
        Wide
    }

    public class OAuthOptions
    {
        public Dictionary<string, OAuthProvider> Providers { get; set; } = new (StringComparer.OrdinalIgnoreCase);
    }

    public class OAuthProvider
    {
        [Required]
        [Url]
        public string AuthorizeUrl { get; set; }

        [Required]
        [Url]
        public string TokenUrl { get; set; }

        [Required]
        public string ClientId { get; set; }

        public string ClientSecret { get; set; }

        public string Scope { get; set; }

        [Url]
        public string Resource { get; set; }

        [Required]
        [Url]
        public string RedirectUrl { get; set; }

        public ClientAuthentication ClientAuthentication { get; set; }

        public PkceMethod Pkce { get; set; }

        public string Name { get; set; }

        public string IconUrl { get; set; }

        public string IconShape { get; set; }
    }

    public static class OAuthIcons
    {
        public const string Path = "/v1/oauth/icons";
        public const string Directory = "icons";

        public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".svg"] = "image/svg+xml",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".jpg"] = "image/jpeg",
            [".ico"] = "image/x-icon"
        };

        private static readonly Regex FileName = new (@"^[A-Za-z0-9][A-Za-z0-9._-]*$");

        public static bool IsLocal(string icon) =>
            !string.IsNullOrEmpty(icon) && FileName.IsMatch(icon) && ContentTypes.ContainsKey(System.IO.Path.GetExtension(icon));

        public static bool TryShape(string value, out IconShape shape)
        {
            var name = string.IsNullOrEmpty(value) ? nameof(IconShape.Round) : Enum.GetNames<IconShape>().FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
            shape = name == null ? IconShape.Round : Enum.Parse<IconShape>(name);
            return name != null;
        }

        public static bool IsRemote(string icon) =>
            Uri.TryCreate(icon, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host);
    }

    public sealed class OAuthOptionsValidator : IValidateOptions<OAuthOptions>
    {
        public ValidateOptionsResult Validate(string name, OAuthOptions options)
        {
            var failures = new List<string>();

            foreach (var (provider, settings) in options.Providers)
            {
                var results = new List<ValidationResult>();

                if (!Validator.TryValidateObject(settings, new ValidationContext(settings), results, true))
                {
                    failures.AddRange(results.Select(x => $"OAuth:Providers:{provider}: {x.ErrorMessage}"));
                }
                else if (!string.IsNullOrEmpty(settings.IconUrl) && !OAuthIcons.IsRemote(settings.IconUrl) && !OAuthIcons.IsLocal(settings.IconUrl))
                {
                    failures.Add($"OAuth:Providers:{provider}: The IconUrl field must be an https URL or the file name of an svg, png, webp, jpg or ico in the icons directory.");
                }
                else if (!OAuthIcons.TryShape(settings.IconShape, out _))
                {
                    failures.Add($"OAuth:Providers:{provider}: The IconShape field must be Round, Fill, Square or Wide.");
                }
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
