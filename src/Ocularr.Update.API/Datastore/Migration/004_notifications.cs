using FluentMigrator;
using Microsoft.Extensions.Logging;
using Ocularr.Update.API.Datastore.Migration.Framework;

namespace Ocularr.Update.API.Datastore.Migration
{
    [Migration(4)]
    public class AddNotifications : DatabaseMigration
    {
        public AddNotifications(ILogger<InitialSetup> logger)
            : base(logger)
        {
        }

        protected override void MainDbUpgrade()
        {
            Create.Table("notification")
                .WithColumn("id").AsInt32().PrimaryKey().Identity()
                .WithColumn("type").AsInt32()
                .WithColumn("message").AsString()
                .WithColumn("wikiurl").AsString().Nullable()
                .WithColumn("operatingsystems").AsString()
                .WithColumn("runtimes").AsString()
                .WithColumn("architectures").AsString()
                .WithColumn("versions").AsString()
                .WithColumn("branches").AsString();
        }
    }
}
