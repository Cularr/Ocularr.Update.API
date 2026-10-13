using System;
using System.IO;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using ServarrAPI.Cloudflare;
using ServarrAPI.Datastore;
using ServarrAPI.Datastore.Migration.Framework;
using ServarrAPI.Model;
using ServarrAPI.OAuth;
using ServarrAPI.Release;
using ServarrAPI.Release.Azure;
using ServarrAPI.Release.Github;
using ServarrAPI.Stats;
using ServarrAPI.TaskQueue;

namespace ServarrAPI
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            // Loading .NetCore style of config variables from json and environment
            Config = configuration;
            ConfigServarr = Config.GetSection("Update").Get<Config>();

            if (ConfigServarr == null)
            {
                throw new Exception("Update config not found");
            }

            Log.Debug($@"Config Variables
            ----------------
            Project        : {ConfigServarr.Project}
            DataDirectory  : {ConfigServarr.DataDirectory}");

            SetupDataDirectory();
        }

        public IConfiguration Config { get; }

        public Config ConfigServarr { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions<Config>()
                .Bind(Config.GetSection("Update"))
                .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey), "Update:ApiKey is required.")
                .ValidateOnStart();
            services.Configure<StatsOptions>(Config.GetSection("Stats"));

            services.AddSingleton<IMigrationController, MigrationController>();
            services.AddSingleton<IDatabase, Database>();
            services.AddSingleton<IUpdateRepository, UpdateRepository>();
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<IUpdateFileRepository, UpdateFileRepository>();
            services.AddSingleton<IUpdateFileService, UpdateFileService>();
            services.AddSingleton<INotificationRepository, NotificationRepository>();
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<ICloudflareProxy, CloudflareProxy>();
            services.AddSingleton(x => GithubReleaseSource.CreateClient(x.GetRequiredService<IOptions<Config>>().Value));
            services.AddHttpClient(nameof(GithubReleaseSource), (x, client) => GithubReleaseSource.ConfigureDownloads(client, x.GetRequiredService<IOptions<Config>>().Value));
            services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
            services.AddSingleton<IUsageStats, UsageStats>();

            services.AddTransient<ReleaseService>();
            services.AddTransient<GithubReleaseSource>();
            services.AddTransient<AzureReleaseSource>();

            services.AddHostedService<QueuedHostedService>();
            services.AddHostedService<ReleaseRefreshService>();

            services.AddOptions<OAuthOptions>().Bind(Config.GetSection("OAuth")).ValidateOnStart();
            services.AddSingleton<IValidateOptions<OAuthOptions>, OAuthOptionsValidator>();
            services.AddSingleton<OAuthStateProtector>();
            services.AddHttpClient(nameof(OAuthController), client => client.Timeout = TimeSpan.FromSeconds(30));
            services.AddDataProtection()
                .SetApplicationName("ServarrAPI")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(ConfigServarr.DataDirectory, "keys")));
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
            SqlBuilderExtensions.LogSql = ConfigServarr.LogSql;

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSerilogRequestLogging();

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
            if (!Path.IsPathRooted(ConfigServarr.DataDirectory))
            {
                throw new Exception($"DataDirectory path must be absolute.\nDataDirectory: {ConfigServarr.DataDirectory}");
            }

            // Create
            Directory.CreateDirectory(ConfigServarr.DataDirectory);
        }
    }
}
