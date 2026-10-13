using System;
using System.Collections.Generic;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Options;
using Serilog;

namespace ServarrAPI.Stats
{
    public class StatsOptions
    {
        public string Url { get; set; }
        public string Token { get; set; }
        public string Org { get; set; }
        public string Bucket { get; set; }
    }

    public interface IUsageStats
    {
        void Write(string source, IReadOnlyDictionary<string, string> tags);
    }

    public sealed class UsageStats : IUsageStats, IDisposable
    {
        private static readonly ILogger Logger = Log.ForContext<UsageStats>();

        private readonly InfluxDBClient _client;
        private readonly WriteApi _writeApi;

        public UsageStats(IOptions<StatsOptions> options)
        {
            var stats = options.Value;

            if (string.IsNullOrWhiteSpace(stats.Url))
            {
                return;
            }

            var clientOptions = new InfluxDBClientOptions(stats.Url)
            {
                Token = stats.Token,
                Org = stats.Org,
                Bucket = stats.Bucket
            };

            var applicationPath = Environment.GetEnvironmentVariable("SERVICE_HOSTNAME");

            if (!string.IsNullOrWhiteSpace(applicationPath))
            {
                clientOptions.AddDefaultTag("application_path", applicationPath);
            }

            _client = new InfluxDBClient(clientOptions);
            _writeApi = _client.GetWriteApi(new WriteOptions { FlushInterval = 2000 });
            _writeApi.EventHandler += (_, e) =>
            {
                if (e is WriteErrorEvent error)
                {
                    Logger.Warning("Writing usage stats failed: {Message}", error.Exception.Message);
                }
            };
        }

        public void Write(string source, IReadOnlyDictionary<string, string> tags)
        {
            if (_writeApi == null)
            {
                return;
            }

            var point = PointData.Measurement("userstats")
                .Field("source", source ?? "Unknown")
                .Timestamp(DateTime.UtcNow, WritePrecision.Ns);

            foreach (var (name, value) in tags)
            {
                point = point.Tag(name, value);
            }

            _writeApi.WritePoint(point);
        }

        public void Dispose()
        {
            _writeApi?.Dispose();
            _client?.Dispose();
        }
    }
}
