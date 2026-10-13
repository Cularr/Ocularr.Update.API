using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ServarrAPI.Test
{
    public sealed class ServiceFactory : WebApplicationFactory<Startup>
    {
        private readonly Dictionary<string, string> _settings;
        private readonly Action<IServiceCollection> _services;

        public ServiceFactory(Dictionary<string, string> settings = null, Action<IServiceCollection> services = null)
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), $"servarrapi-test-{Guid.NewGuid():N}");
            _settings = new Dictionary<string, string>
            {
                ["Update:DataDirectory"] = DataDirectory,
                ["Update:ApiKey"] = "test-key",
                ["Update:Project"] = "Ocularr",
                ["Sentry:Dsn"] = string.Empty,
                ["Update:RefreshInterval"] = "00:00:00"
            };

            foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            {
                _settings[key] = value;
            }

            _services = services;
        }

        public string DataDirectory { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(x => x.AddInMemoryCollection(_settings));
            builder.ConfigureServices(x => _services?.Invoke(x));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(DataDirectory))
            {
                Directory.Delete(DataDirectory, true);
            }
        }
    }
}
