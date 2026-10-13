using FluentMigrator;
using Microsoft.Extensions.Logging;
using Ocularr.Update.API.Datastore.Migration.Framework;

namespace Ocularr.Update.API.Datastore.Migration
{
    [Migration(3)]
    public class InstallerSupport : DatabaseMigration
    {
        public InstallerSupport(ILogger<InitialSetup> logger)
            : base(logger)
        {
        }

        protected override void MainDbUpgrade()
        {
            Create.Column("installer").OnTable("updatefile").AsBoolean().NotNullable().WithDefaultValue(false);
        }
    }
}
