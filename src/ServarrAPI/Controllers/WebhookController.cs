using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ServarrAPI.Cloudflare;
using ServarrAPI.Release;
using ServarrAPI.Release.Azure;
using ServarrAPI.Release.Github;
using ServarrAPI.TaskQueue;
using ServarrAPI.Util;

namespace ServarrAPI.Controllers
{
    [Route("v1/[controller]")]
    [ApiKey]
    public class WebhookController : ControllerBase
    {
        private readonly IBackgroundTaskQueue _queue;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ICloudflareProxy _cloudflare;

        public WebhookController(IBackgroundTaskQueue queue,
                                 IServiceScopeFactory serviceScopeFactory,
                                 ICloudflareProxy cloudflare)
        {
            _queue = queue;
            _serviceScopeFactory = serviceScopeFactory;
            _cloudflare = cloudflare;
        }

        [Route("refresh")]
        [HttpGet]
        [HttpPost]
        public IActionResult Refresh([FromQuery] string source)
        {
            var type = source?.ToLowerInvariant() switch
            {
                "azure" => typeof(AzureReleaseSource),
                "github" => typeof(GithubReleaseSource),
                _ => null
            };

            if (type == null)
            {
                return BadRequest("Unknown source.");
            }

            ReleaseRefreshService.QueueRefresh(_queue, _serviceScopeFactory, type);

            return Ok("Thank you.");
        }

        [Route("branch/{branch}/refresh")]
        [HttpGet]
        public async Task<IActionResult> InvalidateCache([FromRoute(Name = "branch")] string branch)
        {
            if (string.IsNullOrWhiteSpace(branch))
            {
                return BadRequest("Unknown branch.");
            }

            await _cloudflare.InvalidateBranches(new[] { branch }).ConfigureAwait(false);

            return Ok("Thank you.");
        }
    }
}
