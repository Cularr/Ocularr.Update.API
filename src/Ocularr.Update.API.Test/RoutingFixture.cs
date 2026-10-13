using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Ocularr.Update.API.Test
{
    [TestFixture]
    public class RoutingFixture
    {
        [TestCase("/v1/ping", "Pong")]
        [TestCase("/v1/time", "dateTimeUtc")]
        [TestCase("/v1", "version")]
        public async Task endpoints_answer_under_v1(string path, string expected)
        {
            using var factory = new ServiceFactory();

            var response = await factory.CreateClient().GetAsync(path);

            Assert.Multiple(async () =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(expected));
            });
        }

        [TestCase("/ping")]
        [TestCase("/time")]
        [TestCase("/update/master?version=1.0.0.0&os=linux&runtime=netcore&arch=X64")]
        [TestCase("/notification")]
        public async Task paths_without_v1_are_not_served(string path)
        {
            using var factory = new ServiceFactory();

            Assert.That((await factory.CreateClient().GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }
}
