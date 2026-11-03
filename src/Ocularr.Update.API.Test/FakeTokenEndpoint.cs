using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;

namespace Ocularr.Update.API.Test
{
    public sealed class FakeTokenEndpoint : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public string Body { get; set; } = "{\"access_token\":\"access&1\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"refresh_token\":\"refresh 1\"}";

        public List<(string Url, string Authorization, Dictionary<string, string> Form)> Requests { get; } = new ();

        public List<string> UserAgents { get; } = new ();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = QueryHelpers.ParseQuery(await request.Content.ReadAsStringAsync(cancellationToken)).ToDictionary(x => x.Key, x => x.Value.ToString());
            Requests.Add((request.RequestUri.ToString(), request.Headers.Authorization?.ToString(), form));
            UserAgents.Add(request.Headers.UserAgent.ToString());

            return new HttpResponseMessage(StatusCode) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}
