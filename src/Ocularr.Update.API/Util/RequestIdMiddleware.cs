using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Ocularr.Update.API.Util
{
    public sealed class RequestIdMiddleware
    {
        public const string HeaderName = "X-Request-Id";

        private readonly RequestDelegate _next;

        public RequestIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public static string For(HttpContext context) =>
            Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity
                ? activity.TraceId.ToHexString()
                : context.TraceIdentifier;

        public Task InvokeAsync(HttpContext context)
        {
            context.Response.OnStarting(SetHeader, (context.Response, For(context)));
            return _next(context);
        }

        private static Task SetHeader(object state)
        {
            var (response, requestId) = ((HttpResponse, string))state;
            response.Headers[HeaderName] = requestId;
            return Task.CompletedTask;
        }
    }
}
