using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ocularr.Update.API.Util
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ApiKeyAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var expected = context.HttpContext.RequestServices.GetRequiredService<IOptions<Config>>().Value.ApiKey;
            var given = context.HttpContext.Request.Headers["X-Api-Key"].ToString();

            if (!SecretComparer.Matches(expected, given))
            {
                context.Result = new UnauthorizedResult();
            }
        }
    }
}
