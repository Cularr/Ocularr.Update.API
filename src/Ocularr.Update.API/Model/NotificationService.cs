using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ocularr.Update.API.Model
{
    public interface INotificationService
    {
        Task<IEnumerable<Notification>> All();
        Task<List<Notification>> Find(string version, string branch, OperatingSystem os, Architecture arch);
        Task<Notification> Insert(Notification entity);
        void Delete(int id);
    }

    public class NotificationService : INotificationService
    {
        private readonly ILogger _logger;
        private readonly INotificationRepository _repo;
        private readonly Config _config;

        public NotificationService(INotificationRepository repo,
                                   ILogger<INotificationService> logger,
                                   IOptions<Config> config)
        {
            _repo = repo;
            _logger = logger;
            _config = config.Value;
        }

        public Task<IEnumerable<Notification>> All()
        {
            return _repo.All();
        }

        public Task<List<Notification>> Find(string version, string branch, OperatingSystem os, Architecture arch)
        {
            return _repo.Find(version, _config.MapBranch(branch), os, arch);
        }

        public Task<Notification> Insert(Notification entity)
        {
            _logger.LogDebug("Adding notification {0}: {1}", entity.Type, entity.Message);
            return _repo.Insert(entity);
        }

        public void Delete(int id)
        {
            _logger.LogDebug("Deleting notification {0}", id);
            _repo.Delete(id);
        }
    }
}
