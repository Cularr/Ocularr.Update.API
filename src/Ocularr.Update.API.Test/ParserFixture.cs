using System.Runtime.InteropServices;
using NUnit.Framework;
using Ocularr.Update.API.Util;
using OperatingSystem = Ocularr.Update.API.Model.OperatingSystem;

namespace Ocularr.Update.API.Test
{
    [TestFixture]
    public class ParserFixture
    {
        [TestCase("Ocularr.main.5.0.3.40.linux-x64.tar.gz", OperatingSystem.Linux, Architecture.X64, false)]
        [TestCase("Ocularr.main.5.0.3.40.linux-arm64.tar.gz", OperatingSystem.Linux, Architecture.Arm64, false)]
        [TestCase("Ocularr.main.5.0.3.40.linux-arm.tar.gz", OperatingSystem.Linux, Architecture.Arm, false)]
        [TestCase("Ocularr.main.5.0.3.40.linux-musl-x64.tar.gz", OperatingSystem.LinuxMusl, Architecture.X64, false)]
        [TestCase("Ocularr.main.5.0.3.40.linux-musl-arm64.tar.gz", OperatingSystem.LinuxMusl, Architecture.Arm64, false)]
        [TestCase("Ocularr.main.5.0.3.40.osx-x64.tar.gz", OperatingSystem.Osx, Architecture.X64, false)]
        [TestCase("Ocularr.main.5.0.3.40.osx-arm64.tar.gz", OperatingSystem.Osx, Architecture.Arm64, false)]
        [TestCase("Ocularr.main.5.0.3.40.osx-arm64-app.zip", OperatingSystem.Osx, Architecture.Arm64, true)]
        [TestCase("Ocularr.main.5.0.3.40.win-x64.zip", OperatingSystem.Windows, Architecture.X64, false)]
        [TestCase("Ocularr.main.5.0.3.40.win-x86.zip", OperatingSystem.Windows, Architecture.X86, false)]
        [TestCase("Ocularr.main.5.0.3.40.win-x64-installer.exe", OperatingSystem.Windows, Architecture.X64, true)]
        [TestCase("Ocularr.main.5.0.3.40.freebsd-x64.tar.gz", OperatingSystem.Bsd, Architecture.X64, false)]
        public void ocularr_runtime_assets_are_parsed(string file, OperatingSystem os, Architecture arch, bool installer)
        {
            Assert.That(
                (Parser.ParseOS(file), Parser.ParseArchitecture(file), Parser.ParseInstaller(file)),
                Is.EqualTo(((OperatingSystem?)os, arch, installer)));
        }

        [TestCase("Ocularr.main.5.0.3.40.linux-x64.tar.gz.sha256")]
        [TestCase("Ocularr.main.5.0.3.40.solaris-x64.tar.gz")]
        [TestCase("source.zip")]
        [TestCase("Lidarr.master.1.0.0.1.linux-core-arm64.tar.gz")]
        [TestCase("Lidarr.master.1.0.0.1.windows-core-x86.zip")]
        [TestCase("Lidarr.master.1.0.0.1.linux.tar.gz")]
        [TestCase("Lidarr.master.1.0.0.1.windows.zip")]
        [TestCase("Lidarr.master.1.0.0.1.osx-app.zip")]
        public void other_assets_are_ignored(string file)
        {
            Assert.That(Parser.ParseOS(file), Is.Null);
        }
    }
}
