using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using NUnit.Framework;
using OpenTelemetry.Metrics;
using ServarrAPI.Model;
using ServarrAPI.Stats;

namespace ServarrAPI.Test
{
    [TestFixture]
    public class StatsFixture
    {
        private const string Update = "/v1/update/master?version=5.0.0.100&os=linux&runtime=netcore&runtimeVer=10.0.0&arch=Arm64&dbType=PostgreSQL";

        [TestCase("&active=true", true)]
        [TestCase("&active=false", false)]
        [TestCase("", null)]
        public async Task checks_are_counted_only_when_the_client_sends_active(string active, bool? expected)
        {
            using var factory = Create();
            using var checks = Collector(factory);

            await factory.CreateClient().GetAsync(Update + active);

            Assert.That(checks.GetMeasurementSnapshot().Select(x => (bool?)x.Tags["ocularr.install.active"]), Is.EqualTo(expected == null ? Array.Empty<bool?>() : new[] { expected }));
        }

        [Test]
        public async Task a_check_carries_only_the_install_attributes()
        {
            using var factory = Create();
            using var checks = Collector(factory);
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Ocularr/5.0.0.100 (ubuntu 24.04)");

            await client.GetAsync(Update + "&active=true");

            var check = checks.GetMeasurementSnapshot().Single();
            Assert.Multiple(() =>
            {
                Assert.That(check.Value, Is.EqualTo(1));
                Assert.That(check.Tags, Is.EquivalentTo(new Dictionary<string, object>
                {
                    ["ocularr.install.version"] = "5.0.0.100",
                    ["ocularr.install.branch"] = "master",
                    ["ocularr.install.platform"] = "linux",
                    ["ocularr.install.arch"] = "arm64",
                    ["ocularr.install.database"] = "postgresql",
                    ["ocularr.install.active"] = true
                }));
            });
        }

        [TestCase("Main", "SQLite", "main", "sqlite")]
        [TestCase("v5-develop", "Oracle", "v5-develop", "other")]
        [TestCase("a-branch-name-that-is-far-too-long-to-count", "", "other", "other")]
        [TestCase("evil%20branch", null, "other", "other")]
        public async Task free_text_attributes_are_normalized(string branch, string database, string expectedBranch, string expectedDatabase)
        {
            using var factory = Create();
            using var checks = Collector(factory);

            await factory.CreateClient().GetAsync($"/v1/update/{branch}?version=5.0.0.100&os=linux&arch=X64&active=true{(database == null ? string.Empty : "&dbType=" + database)}");

            var tags = checks.GetMeasurementSnapshot().Single().Tags;
            Assert.That((tags["ocularr.install.branch"], tags["ocularr.install.database"]), Is.EqualTo(((object)expectedBranch, (object)expectedDatabase)));
        }

        [Test]
        public async Task development_builds_are_not_counted()
        {
            using var factory = Create();
            using var checks = Collector(factory);

            await factory.CreateClient().GetAsync("/v1/update/master?version=10.0.0.1&os=linux&arch=X64&active=true");

            Assert.That(checks.GetMeasurementSnapshot(), Is.Empty);
        }

        [Test]
        public void metrics_are_not_exported_without_an_otlp_endpoint()
        {
            using var factory = Create();

            Assert.That(factory.Services.GetService<MeterProvider>(), Is.Null);
        }

        [Test]
        public async Task checks_are_exported_over_otlp()
        {
            var port = FreePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var received = ReceiveAsync(listener);

            using (var factory = Create(new Dictionary<string, string>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = $"http://127.0.0.1:{port}/v1/metrics",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf"
            }))
            {
                await factory.CreateClient().GetAsync(Update + "&active=true");
            }

            var (path, body) = await received.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Multiple(() =>
            {
                Assert.That(path, Is.EqualTo("/v1/metrics"));
                Assert.That(body, Does.Contain(UpdateChecks.CounterName).And.Contains("ocularr.install.arch").And.Contains("arm64"));
            });
        }

        private static ServiceFactory Create(Dictionary<string, string> settings = null) =>
            new ServiceFactory(settings, x => x.AddSingleton<IUpdateFileService, EmptyUpdateFiles>());

        private static MetricCollector<long> Collector(ServiceFactory factory) =>
            new MetricCollector<long>(factory.Services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), UpdateChecks.MeterName, UpdateChecks.CounterName);

        private static async Task<(string Path, string Body)> ReceiveAsync(HttpListener listener)
        {
            var context = await listener.GetContextAsync();
            using var body = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(body);
            context.Response.StatusCode = 200;
            context.Response.Close();
            return (context.Request.Url.AbsolutePath, Encoding.UTF8.GetString(body.ToArray()));
        }

        private static int FreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }
    }
}
