using System;
using Npgsql;
using NUnit.Framework;

namespace Ocularr.Update.API.Test
{
    public sealed class TestDatabase : IDisposable
    {
        private readonly string _server;
        private readonly string _name = $"updateapi_test_{Guid.NewGuid():N}";

        public TestDatabase()
        {
            _server = Environment.GetEnvironmentVariable("UPDATE_API_TEST_POSTGRES");

            if (string.IsNullOrWhiteSpace(_server))
            {
                Assert.Ignore("Set UPDATE_API_TEST_POSTGRES to run Postgres tests.");
            }

            Execute($"CREATE DATABASE {_name}");
            ConnectionString = new NpgsqlConnectionStringBuilder(_server) { Database = _name, Pooling = false }.ConnectionString;
        }

        public string ConnectionString { get; }

        public void Dispose() => Execute($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");

        private void Execute(string sql)
        {
            using var connection = new NpgsqlConnection(_server);
            connection.Open();
            using var command = new NpgsqlCommand(sql, connection);
            command.ExecuteNonQuery();
        }
    }
}
