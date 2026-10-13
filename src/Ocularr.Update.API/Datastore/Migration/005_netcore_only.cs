using FluentMigrator;
using Microsoft.Extensions.Logging;
using Ocularr.Update.API.Datastore.Migration.Framework;

namespace Ocularr.Update.API.Datastore.Migration
{
    [Migration(5)]
    public class NetCoreOnly : DatabaseMigration
    {
        public NetCoreOnly(ILogger<InitialSetup> logger)
            : base(logger)
        {
        }

        protected override void MainDbUpgrade()
        {
            Execute.Sql("DELETE FROM updatefile WHERE runtime <> 2");
            Execute.Sql("DELETE FROM notification WHERE runtimes LIKE '[%' AND jsonb_array_length(runtimes::jsonb) > 0 AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements_text(runtimes::jsonb) r WHERE lower(r) IN ('netcore', '2'))");
            Delete.Column("runtime").FromTable("updatefile");
            Delete.Column("runtimes").FromTable("notification");
        }
    }
}
