using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using ServarrAPI.Model;
using Architecture = System.Runtime.InteropServices.Architecture;
using OperatingSystem = ServarrAPI.Model.OperatingSystem;

namespace ServarrAPI.Test
{
    [TestFixture]
    public class NotificationFixture
    {
        [TestCase("5.0.0.1", "5.0.0.1", true)]
        [TestCase("5.0.0.1", "5.0.0.10", false)]
        [TestCase("5.0.*", "5.0.3.1", true)]
        [TestCase("5.0.*", "5.1.0.1", false)]
        [TestCase("5.0.*", null, false)]
        public void versions_match_exactly_or_by_wildcard_prefix(string allowed, string version, bool expected) =>
            Assert.That(Match(new Notification { Versions = { allowed } }, version).Any(), Is.EqualTo(expected));

        [TestCase("master", true)]
        [TestCase("Master", true)]
        [TestCase("develop", false)]
        [TestCase(null, false)]
        public void branches_match_ignoring_case(string branch, bool expected) =>
            Assert.That(Match(new Notification { Branches = { "master" } }, "5.0.0.1", branch).Any(), Is.EqualTo(expected));

        [TestCase("main", "master")]
        [TestCase("Main", "master")]
        [TestCase("develop", "develop")]
        [TestCase(null, null)]
        public void branches_follow_redirects(string branch, string expected) =>
            Assert.That(new Config { BranchRedirects = new Dictionary<string, string> { ["main"] = "master" } }.MapBranch(branch), Is.EqualTo(expected));

        [Test]
        public void the_most_severe_and_newest_notification_comes_first()
        {
            var notifications = new[]
            {
                new Notification { Id = 1, Type = NotificationType.Notice },
                new Notification { Id = 2, Type = NotificationType.Error },
                new Notification { Id = 3, Type = NotificationType.Warning },
                new Notification { Id = 4, Type = NotificationType.Error }
            };

            Assert.That(NotificationRepository.Matching(notifications, "5.0.0.1", "master", OperatingSystem.Linux, Runtime.NetCore, Architecture.X64).Select(x => x.Id), Is.EqualTo(new[] { 4, 2, 3, 1 }));
        }

        [Test]
        public void filters_narrow_by_platform()
        {
            var notification = new Notification { OperatingSystems = { OperatingSystem.Windows }, Runtimes = { Runtime.NetCore }, Architectures = { Architecture.Arm64 } };

            Assert.Multiple(() =>
            {
                Assert.That(NotificationRepository.Matching(new[] { notification }, "5.0.0.1", "master", OperatingSystem.Windows, Runtime.NetCore, Architecture.Arm64), Has.Count.EqualTo(1));
                Assert.That(NotificationRepository.Matching(new[] { notification }, "5.0.0.1", "master", OperatingSystem.Linux, Runtime.NetCore, Architecture.Arm64), Is.Empty);
                Assert.That(NotificationRepository.Matching(new[] { notification }, "5.0.0.1", "master", OperatingSystem.Windows, Runtime.DotNet, Architecture.Arm64), Is.Empty);
                Assert.That(NotificationRepository.Matching(new[] { notification }, "5.0.0.1", "master", OperatingSystem.Windows, Runtime.NetCore, Architecture.X64), Is.Empty);
            });
        }

        [Test]
        [NonParallelizable]
        public async Task ocularr_receives_its_notifications_most_severe_first()
        {
            using var database = new TestDatabase();
            using var factory = new ServiceFactory(new Dictionary<string, string>
            {
                ["ConnectionStrings:Database"] = database.ConnectionString,
                ["Update:BranchRedirects:main"] = "master"
            });
            using var client = factory.CreateClient();

            foreach (var (type, message, runtime) in new[] { (1, "notice", 2), (3, "error", 2), (2, "warning", 2), (3, "mono", 1) })
            {
                var created = await client.PostAsJsonAsync("/v1/notification?api_key=test-key", new { type, message, branches = new[] { "master" }, runtimes = new[] { runtime } });
                Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }

            var notifications = await client.GetFromJsonAsync<JsonElement[]>("/v1/notification?version=5.0.0.1&os=linux&arch=X64&branch=main");

            Assert.That(notifications.Select(x => x.GetProperty("message").GetString()), Is.EqualTo(new[] { "error", "warning", "notice" }));
        }

        private static List<Notification> Match(Notification notification, string version, string branch = "master") =>
            NotificationRepository.Matching(new[] { notification }, version, branch, OperatingSystem.Linux, Runtime.NetCore, Architecture.X64);
    }
}
