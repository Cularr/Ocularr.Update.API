using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ocularr.Update.API.Release.Github;
using Ocularr.Update.API.TaskQueue;

namespace Ocularr.Update.API.Release
{
    public sealed class ReleaseRefreshService : BackgroundService
    {
        private readonly IBackgroundTaskQueue _queue;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly Config _config;

        public ReleaseRefreshService(IBackgroundTaskQueue queue, IServiceScopeFactory serviceScopeFactory, IOptions<Config> config)
        {
            _queue = queue;
            _serviceScopeFactory = serviceScopeFactory;
            _config = config.Value;
        }

        public static void QueueRefresh(IBackgroundTaskQueue queue, IServiceScopeFactory serviceScopeFactory)
        {
            queue.QueueBackgroundWorkItem(async token =>
            {
                using var scope = serviceScopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<GithubReleaseSource>().FetchReleasesAsync();
            });
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_config.RefreshInterval <= TimeSpan.Zero)
            {
                return;
            }

            using var timer = new PeriodicTimer(_config.RefreshInterval);

            do
            {
                QueueRefresh(_queue, _serviceScopeFactory);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
