using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Ocularr.Update.API.Model;
using OperatingSystem = Ocularr.Update.API.Model.OperatingSystem;

namespace Ocularr.Update.API.Util
{
    public static class Parser
    {
        public static readonly Regex InstallerRegex = new Regex(@"(-installer\.exe|-app\.zip)$", RegexOptions.Compiled);

        public static readonly Regex RidAsset = new Regex(@"\.(?<os>linux-musl|linux|osx|win|freebsd)-(?<arch>x86|x64|arm64|arm)(?:-installer\.exe|-app\.zip|\.tar\.gz|\.zip)$", RegexOptions.Compiled);

        public static OperatingSystem? ParseOS(string file)
        {
            var rid = RidAsset.Match(file);
            if (rid.Success)
            {
                return rid.Groups["os"].Value switch
                {
                    "win" => OperatingSystem.Windows,
                    "linux" => OperatingSystem.Linux,
                    "linux-musl" => OperatingSystem.LinuxMusl,
                    "osx" => OperatingSystem.Osx,
                    _ => OperatingSystem.Bsd
                };
            }

            return null;
        }

        public static bool ParseInstaller(string file)
        {
            return InstallerRegex.IsMatch(file);
        }

        public static Architecture ParseArchitecture(string file)
        {
            return RidAsset.Match(file).Groups["arch"].Value switch
            {
                "arm64" => Architecture.Arm64,
                "arm" => Architecture.Arm,
                "x64" => Architecture.X64,
                "x86" => Architecture.X86,
                _     => throw new ArgumentException(message: "Invalid architecture")
            };
        }
    }
}
