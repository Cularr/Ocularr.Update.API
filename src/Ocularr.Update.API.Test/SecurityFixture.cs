using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Ocularr.Update.API.TaskQueue;
using Ocularr.Update.API.Util;
using Sentry;

namespace Ocularr.Update.API.Test
{
    [TestFixture]
    public class SecurityFixture
    {
        [TestCase("test-key", true)]
        [TestCase("test-kez", false)]
        [TestCase("test-key-", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void api_keys_must_match_exactly(string given, bool expected) =>
            Assert.That(ApiKeyAttribute.Matches("test-key", given), Is.EqualTo(expected));

        [TestCase("")]
        [TestCase(null)]
        public void an_empty_expected_key_matches_nothing(string expected) =>
            Assert.That(ApiKeyAttribute.Matches(expected, string.Empty), Is.False);

        [TestCase(null)]
        [TestCase("wrong")]
        [TestCase("test-key-")]
        public async Task publishing_refuses_a_wrong_key(string apiKey)
        {
            var queue = new RecordingQueue();
            using var factory = new ServiceFactory(services: x => x.AddSingleton<IBackgroundTaskQueue>(queue));
            using var client = factory.CreateClient();

            var response = await client.SendAsync(Request(HttpMethod.Post, "/v1/update", apiKey));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(queue.Count, Is.Zero);
        }

        [TestCase("/v1/notification/1", "DELETE", null)]
        [TestCase("/v1/notification/1", "DELETE", "wrong")]
        [TestCase("/v1/notification", "POST", "wrong")]
        public async Task notification_changes_refuse_a_wrong_key(string url, string method, string apiKey)
        {
            using var factory = new ServiceFactory();
            using var client = factory.CreateClient();

            var response = await client.SendAsync(Request(new HttpMethod(method), url, apiKey));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task publishing_queues_a_release_refresh()
        {
            var queue = new RecordingQueue();
            using var factory = new ServiceFactory(services: x => x.AddSingleton<IBackgroundTaskQueue>(queue));
            using var client = factory.CreateClient();
            var deploy = "{\"branch\":\"v5-main\",\"version\":\"5.0.0.2\",\"lastCommit\":\"abc\",\"hashes\":[{\"Os\":\"linux\",\"Arch\":\"x64\",\"Type\":\"archive\",\"Hash\":\"00\"}],\"gitHubRelease\":true}";

            var response = await client.SendAsync(Request(HttpMethod.Post, "/v1/update", "test-key", deploy));

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
                Assert.That(queue.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task the_key_is_not_read_from_the_query()
        {
            var queue = new RecordingQueue();
            using var factory = new ServiceFactory(services: x => x.AddSingleton<IBackgroundTaskQueue>(queue));
            using var client = factory.CreateClient();

            var response = await client.SendAsync(Request(HttpMethod.Post, "/v1/update?api_key=test-key", null));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [TestCase("")]
        [TestCase(" ")]
        public void the_service_does_not_start_without_an_api_key(string apiKey)
        {
            using var factory = new ServiceFactory(new Dictionary<string, string> { ["Update:ApiKey"] = apiKey });

            Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        }

        [Test]
        public void sentry_events_carry_no_query_string()
        {
            var sentryEvent = new SentryEvent();
            sentryEvent.Request.Url = "https://system.ocularr.ink/v1/oauth/mangabaka/renew?refresh_token=secret";
            sentryEvent.Request.QueryString = "refresh_token=secret";

            Program.WithoutQuery(sentryEvent, new SentryHint());

            Assert.Multiple(() =>
            {
                Assert.That(sentryEvent.Request.Url, Is.EqualTo("https://system.ocularr.ink/v1/oauth/mangabaka/renew"));
                Assert.That(sentryEvent.Request.QueryString, Is.Null);
            });
        }

        private static HttpRequestMessage Request(HttpMethod method, string url, string apiKey, string body = "{}")
        {
            var request = new HttpRequestMessage(method, url) { Content = new StringContent(body, null, "application/json") };

            if (apiKey != null)
            {
                request.Headers.Add("X-Api-Key", apiKey);
            }

            return request;
        }
    }
}
