using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ServarrAPI.Datastore;

namespace ServarrAPI.Model
{
    public interface INotificationRepository : IBasicRepository<Notification>
    {
        Task<List<Notification>> Find(string version, string branch, OperatingSystem os, Architecture arch);
    }

    public class NotificationRepository : BasicRepository<Notification>, INotificationRepository
    {
        public NotificationRepository(IDatabase database)
        : base(database)
        {
        }

        public async Task<List<Notification>> Find(string version, string branch, OperatingSystem os, Architecture arch)
        {
            return Matching(await All(), version, branch, os, arch);
        }

        public static List<Notification> Matching(IEnumerable<Notification> all, string version, string branch, OperatingSystem os, Architecture arch)
        {
            var result2 = all.Where(f => (f.OperatingSystems.Contains(os) || f.OperatingSystems.Count == 0) &&
                                                              (f.Architectures.Contains(arch) || f.Architectures.Count == 0) &&
                                                              (f.Branches.Contains(branch, StringComparer.OrdinalIgnoreCase) || f.Branches.Count == 0) &&
                                                              (VersionValidation(version, f.Versions) || f.Versions.Count == 0));
            return result2.OrderByDescending(f => f.Type).ThenByDescending(f => f.Id).ToList();
        }

        private static bool VersionValidation(string version, List<string> allowedVersions)
        {
            if (version == null)
            {
                return false;
            }

            return allowedVersions.Any(x => x.EndsWith('*') ? version.StartsWith(x[..^1], StringComparison.Ordinal) : version == x);
        }
    }
}
