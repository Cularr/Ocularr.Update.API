using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ServarrAPI.Release.Github;
using ServarrAPI.TaskQueue;

namespace ServarrAPI.Release
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

        public static void QueueRefresh(IBackgroundTaskQueue queue, IServiceScopeFactory serviceScopeFactory, Type releaseSource)
        {
            queue.QueueBackgroundWorkItem(async token =>
            {
                using var scope = serviceScopeFactory.CreateScope();
                var releaseService = scope.ServiceProvider.GetRequiredService<ReleaseService>();

                await releaseService.UpdateReleasesAsync(releaseSource);
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
                QueueRefresh(_queue, _serviceScopeFactory, typeof(GithubReleaseSource));
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
    }
}
