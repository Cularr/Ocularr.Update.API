using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NUnit.Framework;
using Ocularr.Update.API.Datastore.Migration.Framework;

namespace Ocularr.Update.API.Test
{
    [TestFixture]
    [NonParallelizable]
    public class MigrationFixture
    {
        [Test]
        public void only_netcore_files_survive_and_runtime_columns_are_dropped()
        {
            using var database = new TestDatabase();
            var migrations = new MigrationController(NullLogger<MigrationController>.Instance);
            migrations.Migrate(database.ConnectionString, new MigrationContext(4));
            Execute(database,
                "INSERT INTO update (version, releasedate, branch, new, fixed, intversion) VALUES ('5.0.0.1', now(), 'master', '[]', '[]', 50000000001)",
                "INSERT INTO updatefile (updateid, operatingsystem, runtime, architecture, filename, url, hash, installer) SELECT id, 1, r, 1, f, 'u', 'h', false FROM update, (VALUES (0, 'dotnet.tar.gz'), (1, 'mono.tar.gz'), (2, 'netcore.tar.gz')) AS v(r, f)",
                "INSERT INTO notification (type, message, operatingsystems, runtimes, architectures, versions, branches) VALUES (1, 'netcore', '[]', '[\"netCore\"]', '[]', '[]', '[]'), (1, 'number', '[]', '[1, 2]', '[]', '[]', '[]'), (1, 'all', '[]', '[]', '[]', '[]', '[]'), (1, 'mono', '[]', '[\"mono\"]', '[]', '[]', '[]'), (1, 'dotnet', '[]', '[0, 1]', '[]', '[]', '[]')");

            migrations.Migrate(database.ConnectionString, new MigrationContext());

            Assert.Multiple(() =>
            {
                Assert.That(Scalar(database, "SELECT string_agg(filename, ',') FROM updatefile"), Is.EqualTo("netcore.tar.gz"));
                Assert.That(Scalar(database, "SELECT count(*) FROM information_schema.columns WHERE column_name IN ('runtime', 'runtimes')"), Is.EqualTo(0));
                Assert.That(Scalar(database, "SELECT string_agg(message, ',' ORDER BY message) FROM notification"), Is.EqualTo("all,netcore,number"));
            });
        }

        private static void Execute(TestDatabase database, params string[] statements)
        {
            using var connection = new NpgsqlConnection(database.ConnectionString);
            connection.Open();

            foreach (var sql in statements)
            {
                using var command = new NpgsqlCommand(sql, connection);
                command.ExecuteNonQuery();
            }
        }

        private static object Scalar(TestDatabase database, string sql)
        {
            using var connection = new NpgsqlConnection(database.ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand(sql, connection);
            var value = command.ExecuteScalar();
            return value is long count ? (int)count : value;
        }
    }
}
