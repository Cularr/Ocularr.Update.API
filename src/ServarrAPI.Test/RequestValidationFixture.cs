using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ServarrAPI.Model;

namespace ServarrAPI.Test
{
    [TestFixture]
    public class RequestValidationFixture
    {
        [TestCase("/v1/update/master?version=five&os=linux&runtime=netcore&arch=X64", "version")]
        [TestCase("/v1/update/master?os=linux&runtime=netcore&arch=X64", "version")]
        [TestCase("/v1/update/master?version=5.0.0.1&os=amiga&runtime=netcore&arch=X64", "os")]
        [TestCase("/v1/update/master?version=5.0.0.1&os=linux&runtime=netcore&arch=Z80", "arch")]
        [TestCase("/v1/update/master?version=5.0.0.1&os=linux&runtime=netcore&arch=X64&active=maybe", "active")]
        [TestCase("/v1/notification?version=5.0.0.1&os=amiga", "os")]
        public async Task invalid_requests_are_refused_with_problem_details(string url, string field)
        {
            using var factory = new ServiceFactory(services: x => x.AddSingleton<IUpdateFileService, EmptyUpdateFiles>());
            using var client = factory.CreateClient();

            var response = await client.GetAsync(url);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(problem.GetProperty("errors").TryGetProperty(field, out _), Is.True);
            });
        }

        [Test]
        public async Task a_valid_request_without_an_update_is_answered()
        {
            using var factory = new ServiceFactory(services: x => x.AddSingleton<IUpdateFileService, EmptyUpdateFiles>());
            using var client = factory.CreateClient();

            var response = await client.GetFromJsonAsync<JsonElement>("/v1/update/master?version=5.0.0.1&os=linux&runtime=netcore&arch=Arm64");

            Assert.That(response.GetProperty("available").GetBoolean(), Is.False);
        }
    }
}
