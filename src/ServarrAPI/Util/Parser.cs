using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ServarrAPI.Model;
using OperatingSystem = ServarrAPI.Model.OperatingSystem;

namespace ServarrAPI.Util
{
    public static class Parser
    {
        public static readonly Regex NetCoreAsset = new Regex(@"(bsd|linux|linux-musl|osx|osx-app|windows)-core-(x86|x64|arm|arm64)", RegexOptions.Compiled);

        public static readonly Regex WindowsAsset = new Regex(@"(windows(-core-(x86|x64|arm|arm64))?\.zip|installer.exe)$", RegexOptions.Compiled);

        public static readonly Regex LinuxAsset = new Regex(@"linux(-core-(x86|x64|arm|arm64))?\.tar.gz$", RegexOptions.Compiled);

        public static readonly Regex LinuxMuslAsset = new Regex(@"linux-musl(-core-(x64|arm|arm64))?\.tar.gz$", RegexOptions.Compiled);

        public static readonly Regex OsxAsset = new Regex(@"osx(-app)?(-core-(x64|arm|arm64))?\.(tar.gz|zip)$", RegexOptions.Compiled);

        public static readonly Regex BsdAsset = new Regex(@"bsd(-core-(x64|arm|arm64))?\.tar.gz$", RegexOptions.Compiled);

        public static readonly Regex ArchRegex = new Regex(@"core-(?<arch>x86|x64|arm|arm64)(-installer)?\.", RegexOptions.Compiled);

        public static readonly Regex InstallerRegex = new Regex(@"installer\.exe|osx-app|-app\.zip$", RegexOptions.Compiled);

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

            if (WindowsAsset.IsMatch(file))
            {
                return OperatingSystem.Windows;
            }
            else if (LinuxAsset.IsMatch(file))
            {
                return OperatingSystem.Linux;
            }
            else if (LinuxMuslAsset.IsMatch(file))
            {
                return OperatingSystem.LinuxMusl;
            }
            else if (BsdAsset.IsMatch(file))
            {
                return OperatingSystem.Bsd;
            }
            else if (OsxAsset.IsMatch(file))
            {
                return OperatingSystem.Osx;
            }

            return null;
        }

        public static Runtime ParseRuntime(string file)
        {
            return NetCoreAsset.IsMatch(file) || RidAsset.IsMatch(file) ? Runtime.NetCore : Runtime.DotNet;
        }

        public static bool ParseInstaller(string file)
        {
            return InstallerRegex.IsMatch(file);
        }

        public static Architecture ParseArchitecture(string file)
        {
            var match = RidAsset.Match(file);

            if (!match.Success)
            {
                match = ArchRegex.Match(file);
            }

            if (!match.Success)
            {
                return Architecture.X64;
            }

            return match.Groups["arch"].Value switch
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
