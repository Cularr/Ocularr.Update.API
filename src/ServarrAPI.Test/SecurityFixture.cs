using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Sentry;
using ServarrAPI.TaskQueue;
using ServarrAPI.Util;

namespace ServarrAPI.Test
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

        [TestCase("/v1/webhook/refresh?source=github", "POST")]
        [TestCase("/v1/webhook/refresh?source=github&api_key=wrong", "GET")]
        [TestCase("/v1/webhook/branch/master/refresh?api_key=wrong", "GET")]
        public async Task webhooks_refuse_a_wrong_key(string url, string method)
        {
            var queue = new RecordingQueue();
            using var factory = new ServiceFactory(services: x => x.AddSingleton<IBackgroundTaskQueue>(queue));
            using var client = factory.CreateClient();

            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(queue.Count, Is.Zero);
        }

        [TestCase("/v1/notification/1", "DELETE")]
        [TestCase("/v1/notification/1?api_key=wrong", "DELETE")]
        [TestCase("/v1/notification?api_key=wrong", "POST")]
        public async Task notification_changes_refuse_a_wrong_key(string url, string method)
        {
            using var factory = new ServiceFactory();
            using var client = factory.CreateClient();

            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = new StringContent("{}", null, "application/json") });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [TestCase("")]
        [TestCase("&source=")]
        [TestCase("&source=gitlab")]
        public async Task refresh_refuses_an_unknown_source(string source)
        {
            var queue = new RecordingQueue();
            using var factory = new ServiceFactory(services: x => x.AddSingleton<IBackgroundTaskQueue>(queue));
            using var client = factory.CreateClient();

            var response = await client.PostAsync($"/v1/webhook/refresh?api_key=test-key{source}", null);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(queue.Count, Is.Zero);
        }

        [TestCase("github")]
        [TestCase("GitHub")]
        public async Task refresh_queues_a_known_source(string source)
        {
            var queue = new RecordingQueue();
            using var factory = new ServiceFactory(services: x => x.AddSingleton<IBackgroundTaskQueue>(queue));
            using var client = factory.CreateClient();

            var response = await client.PostAsync($"/v1/webhook/refresh?source={source}&api_key=test-key", null);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(queue.Count, Is.EqualTo(1));
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
    }
}
