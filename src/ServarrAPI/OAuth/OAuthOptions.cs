using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.Extensions.Options;

namespace ServarrAPI.OAuth
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

        [Required]
        [Url]
        public string RedirectUrl { get; set; }

        public ClientAuthentication ClientAuthentication { get; set; }

        public PkceMethod Pkce { get; set; }
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
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
