using FluentMigrator;
using Microsoft.Extensions.Logging;
using Ocularr.Update.API.Datastore.Migration.Framework;

namespace Ocularr.Update.API.Datastore.Migration
{
    [Migration(2)]
    public class BreakUpVersion : DatabaseMigration
    {
        public BreakUpVersion(ILogger<InitialSetup> logger)
            : base(logger)
        {
        }

        protected override void MainDbUpgrade()
        {
            Create.Column("intversion").OnTable("update").AsInt64().NotNullable().WithDefaultValue(0).Indexed();

            // Create a sortable integer for version that can easily be compared against a max version,
            // supports any major, minor to 99, patch to 99, builds up to 999,999.
            Execute.Sql("UPDATE update SET intversion = " +
                "((string_to_array(version, '.')::int[])[1] * 10000000000) + " +
                "((string_to_array(version, '.')::int[])[2] * 100000000) + " +
                "((string_to_array(version, '.')::int[])[3] * 1000000) + " +
                "((string_to_array(version, '.')::int[])[4])");
        }
    }
}
