using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Ocularr.Update.API.Model;
using Ocularr.Update.API.Util;

namespace Ocularr.Update.API.Controllers
{
    [ApiController]
    [Route("v1/[controller]")]
    public class NotificationController : Controller
    {
        private readonly INotificationService _notificationService;

        public NotificationController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpGet]
        public async Task<object> GetNotifications([FromQuery(Name = "branch")] string updateBranch,
                                                   [FromQuery(Name = "version")] string urlVersion,
                                                   [FromQuery(Name = "os")] OperatingSystem operatingSystem,
                                                   [FromQuery(Name = "arch")] Architecture arch = Architecture.X64)
        {
            return await _notificationService.Find(urlVersion, updateBranch, operatingSystem, arch);
        }

        [HttpPost]
        [ApiKey]
        public async Task<object> AddNotification([FromBody] Notification notification)
        {
            try
            {
                return await _notificationService.Insert(notification);
            }
            catch
            {
                return new BadRequestResult();
            }
        }

        [Route("{id}")]
        [HttpDelete]
        [ApiKey]
        public object DeleteNotification([FromRoute(Name = "id")] int id)
        {
            _notificationService.Delete(id);

            return new OkResult();
        }
    }
}
