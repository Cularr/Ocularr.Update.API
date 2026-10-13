using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Architecture = System.Runtime.InteropServices.Architecture;
using OperatingSystem = Ocularr.Update.API.Model.OperatingSystem;

namespace Ocularr.Update.API.Stats
{
    public sealed class UpdateChecks
    {
        public const string MeterName = "Ocularr.Update.API";
        public const string CounterName = "ocularr.update.checks";

        private static readonly Regex BranchName = new (@"^[a-z0-9][a-z0-9.-]{0,31}$", RegexOptions.Compiled);

        private readonly Counter<long> _checks;

        public UpdateChecks(IMeterFactory meterFactory)
        {
            _checks = meterFactory.Create(MeterName).CreateCounter<long>(CounterName, "{check}");
        }

        public void Record(Version version, string branch, OperatingSystem os, Architecture arch, string database, bool active)
        {
            var normalizedBranch = branch?.ToLowerInvariant();

            _checks.Add(1, new TagList
            {
                { "ocularr.install.version", version.ToString() },
                { "ocularr.install.branch", normalizedBranch != null && BranchName.IsMatch(normalizedBranch) ? normalizedBranch : "other" },
                { "ocularr.install.platform", os.ToString().ToLowerInvariant() },
                { "ocularr.install.arch", arch.ToString().ToLowerInvariant() },
                { "ocularr.install.database", database?.ToLowerInvariant() is "sqlite" or "postgresql" ? database.ToLowerInvariant() : "other" },
                { "ocularr.install.active", active }
            });
        }
    }
}
