using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ocularr.Update.API.Datastore;
using Ocularr.Update.API.Datastore.Migration.Framework;
using Ocularr.Update.API.Model;
using Ocularr.Update.API.OAuth;
using Ocularr.Update.API.Release;
using Ocularr.Update.API.Release.Github;
using Ocularr.Update.API.Stats;
using Ocularr.Update.API.TaskQueue;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;

namespace Ocularr.Update.API
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            // Loading .NetCore style of config variables from json and environment
            Config = configuration;
            UpdateConfig = Config.GetSection("Update").Get<Config>();

            if (UpdateConfig == null)
            {
                throw new Exception("Update config not found");
            }

            Log.Debug($@"Config Variables
            ----------------
            Project        : {UpdateConfig.Project}
            DataDirectory  : {UpdateConfig.DataDirectory}");

            SetupDataDirectory();
        }

        public IConfiguration Config { get; }

        public Config UpdateConfig { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions<Config>()
                .Bind(Config.GetSection("Update"))
                .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "Update:ApiKey is required.")
                .ValidateOnStart();

            services.AddSingleton<IMigrationController, MigrationController>();
            services.AddSingleton<IDatabase, Database>();
            services.AddSingleton<IUpdateRepository, UpdateRepository>();
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<IUpdateFileRepository, UpdateFileRepository>();
            services.AddSingleton<IUpdateFileService, UpdateFileService>();
            services.AddSingleton<INotificationRepository, NotificationRepository>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton(x => GithubReleaseSource.CreateClient(x.GetRequiredService<IOptions<Config>>().Value));
            services.AddHttpClient(nameof(GithubReleaseSource), (x, client) => GithubReleaseSource.ConfigureDownloads(client, x.GetRequiredService<IOptions<Config>>().Value));
            services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
            services.AddSingleton<UpdateChecks>();

            if (!string.IsNullOrWhiteSpace(Config["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            {
                services.AddOpenTelemetry()
                    .ConfigureResource(resource => resource.AddService(UpdateChecks.MeterName))
                    .WithMetrics(metrics => metrics
                        .AddMeter(UpdateChecks.MeterName)
                        .AddView(UpdateChecks.CounterName, new MetricStreamConfiguration { CardinalityLimit = 20_000 })
                        .AddOtlpExporter());
            }

            services.AddTransient<GithubReleaseSource>();

            services.AddHostedService<QueuedHostedService>();
            services.AddHostedService<ReleaseRefreshService>();

            services.AddOptions<OAuthOptions>().Bind(Config.GetSection("OAuth")).ValidateOnStart();
            services.AddSingleton<IValidateOptions<OAuthOptions>, OAuthOptionsValidator>();
            services.AddSingleton<OAuthStateProtector>();
            services.AddHttpClient(nameof(OAuthController), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Ocularr API/{ApiInfo.Version}");
            });
            services.AddDataProtection()
                .SetApplicationName("Ocularr.Update.API")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(UpdateConfig.DataDirectory, "keys")));
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(OAuthController.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
            });

            services
                .AddControllers()
                .AddJsonOptions(options => options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            SqlBuilderExtensions.LogSql = UpdateConfig.LogSql;

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSerilogRequestLogging();

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new EmbeddedFileProvider(typeof(Startup).Assembly, OAuthPage.AssetsNamespace),
                RequestPath = OAuthPage.AssetsPath,
                OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=3600";
                    context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                }
            });

            var icons = Path.Combine(UpdateConfig.DataDirectory, OAuthIcons.Directory);
            Directory.CreateDirectory(icons);

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(icons),
                RequestPath = OAuthIcons.Path,
                ContentTypeProvider = new FileExtensionContentTypeProvider(new Dictionary<string, string>(OAuthIcons.ContentTypes, StringComparer.OrdinalIgnoreCase)),
                ServeUnknownFileTypes = false,
                OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers.CacheControl = "public, max-age=3600";
                    context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                    context.Context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
                }
            });

            app.UseRouting();

            app.UseRateLimiter();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

        private void SetupDataDirectory()
        {
            // Check data path
            if (!Path.IsPathRooted(UpdateConfig.DataDirectory))
            {
                throw new Exception($"DataDirectory path must be absolute.\nDataDirectory: {UpdateConfig.DataDirectory}");
            }

            // Create
            Directory.CreateDirectory(UpdateConfig.DataDirectory);
        }
    }
}
