using System.Collections.Generic;
using Ocularr.Update.API.Datastore;

namespace Ocularr.Update.API.Model
{
    public class Notification : ModelBase
    {
        public Notification()
        {
            OperatingSystems = new List<OperatingSystem>();
            Architectures = new List<System.Runtime.InteropServices.Architecture>();
            Versions = new List<string>();
            Branches = new List<string>();
        }

        public NotificationType Type { get; set; }
        public string Message { get; set; }
        public string WikiUrl { get; set; }
        public List<OperatingSystem> OperatingSystems { get; set; }
        public List<System.Runtime.InteropServices.Architecture> Architectures { get; set; }
        public List<string> Versions { get; set; }
        public List<string> Branches { get; set; }
    }

    public enum NotificationType
    {
        Notice = 1,
        Warning = 2,
        Error = 3
    }
}
