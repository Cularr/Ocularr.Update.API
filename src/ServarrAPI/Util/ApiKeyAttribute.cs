using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ServarrAPI.Util
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ApiKeyAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var expected = context.HttpContext.RequestServices.GetRequiredService<IOptions<Config>>().Value.ApiKey;
            var given = context.HttpContext.Request.Headers["X-Api-Key"].ToString();

            if (!Matches(expected, given))
            {
                context.Result = new UnauthorizedResult();
            }
        }

        public static bool Matches(string expected, string given) =>
            !string.IsNullOrEmpty(expected) &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(given ?? string.Empty)));
    }
}
