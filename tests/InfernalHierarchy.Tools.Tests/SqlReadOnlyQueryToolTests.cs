using FluentAssertions;
using InfernalHierarchy.Tools.Options;
using InfernalHierarchy.Tools.Tools.Sql;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace InfernalHierarchy.Tools.Tests;

public sealed class SqlReadOnlyQueryToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnRows_ForSelectQuery()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"infernal-sql-tool-{Guid.NewGuid():N}.db");
        var cs = $"Data Source={dbPath}";

        await using (var connection = new SqliteConnection(cs))
        {
            await connection.OpenAsync();

            var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE sample (id INTEGER PRIMARY KEY, name TEXT);";
            await create.ExecuteNonQueryAsync();

            var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO sample (name) VALUES ('alpha'), ('beta');";
            await insert.ExecuteNonQueryAsync();
        }

        var tool = new SqlReadOnlyQueryTool(Microsoft.Extensions.Options.Options.Create(new SqlReadOnlyToolOptions
        {
            Enabled = true,
            RequireReadOnly = true,
            AllowConnectionStringFromParameters = true,
            AllowedConnectionStrings = new List<string> { cs }
        }));

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "SELECT id, name FROM sample ORDER BY id",
            ["connection_string"] = cs
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("alpha");
        result.Output.Should().Contain("beta");

        // No explicit delete: SQLite file handles may remain briefly alive depending on provider pooling.
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRejectWriteStatements()
    {
        var tool = new SqlReadOnlyQueryTool(Microsoft.Extensions.Options.Options.Create(new SqlReadOnlyToolOptions
        {
            Enabled = true,
            RequireReadOnly = true,
            AllowConnectionStringFromParameters = true,
            AllowedConnectionStrings = new List<string> { "Data Source=:memory:" }
        }));

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["query"] = "DELETE FROM users",
            ["connection_string"] = "Data Source=:memory:"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("read-only");
    }
}