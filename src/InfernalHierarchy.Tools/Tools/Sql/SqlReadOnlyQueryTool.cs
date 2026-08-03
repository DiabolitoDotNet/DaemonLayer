using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Tools.Tools.Sql;

public sealed class SqlReadOnlyQueryTool : ITool
{
    private static readonly Regex ForbiddenKeywordRegex = new(
        @"\b(insert|update|delete|drop|alter|create|attach|detach|replace|truncate|vacuum|pragma|reindex|grant|revoke)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SqlReadOnlyToolOptions _options;

    public SqlReadOnlyQueryTool(IOptions<SqlReadOnlyToolOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "sql_query_readonly";

    public string Description => "Run read-only SQL SELECT queries with strict guardrails. Params: query (required), connection_string (required unless configured default).";

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "CommandText assignment is constrained by single-statement and read-only SELECT guardrails before execution.")]
    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return Fail("SQL read-only tool is disabled (SqlReadOnlyTool:Enabled=false)");
        }

        var query = GetString(parameters, "query") ?? GetString(parameters, "sql");
        if (string.IsNullOrWhiteSpace(query))
        {
            return Fail("Missing required parameter: query");
        }

        if (_options.RequireReadOnly)
        {
            var normalized = NormalizeQueryForGuardrails(query);
            if (ContainsMultipleStatements(normalized))
            {
                return Fail("Only a single SQL statement is allowed");
            }

            if (ForbiddenKeywordRegex.IsMatch(normalized))
            {
                return Fail("Only read-only SELECT queries are allowed");
            }

            if (!LooksLikeSelect(normalized))
            {
                return Fail("Query must be a SELECT statement");
            }
        }

        var connectionString = ResolveConnectionString(parameters);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Fail("No allowed connection string resolved for query execution");
        }

        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = Math.Max(1, _options.CommandTimeoutSeconds);

            var rows = new List<Dictionary<string, object?>>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

            var maxRows = Math.Max(1, _options.MaxRows);
            while (rows.Count < maxRows && await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    object? value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    if (value is string s && s.Length > _options.MaxCellChars)
                    {
                        value = s[.._options.MaxCellChars];
                    }

                    row[name] = value;
                }

                rows.Add(row);
            }

            var truncated = rows.Count >= maxRows && await reader.ReadAsync(ct).ConfigureAwait(false);
            var output = JsonSerializer.Serialize(rows);

            return new ToolResult
            {
                Success = true,
                Output = output,
                Metadata = new Dictionary<string, object>
                {
                    ["row_count"] = rows.Count,
                    ["truncated"] = truncated,
                    ["max_rows"] = maxRows,
                    ["readonly_enforced"] = _options.RequireReadOnly
                }
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private string? ResolveConnectionString(Dictionary<string, object> parameters)
    {
        var fromParameter = GetString(parameters, "connection_string") ?? GetString(parameters, "connectionString");
        if (!string.IsNullOrWhiteSpace(fromParameter))
        {
            if (!_options.AllowConnectionStringFromParameters)
            {
                return null;
            }

            return _options.AllowedConnectionStrings
                .FirstOrDefault(x => string.Equals(x, fromParameter, StringComparison.Ordinal));
        }

        return _options.AllowedConnectionStrings.FirstOrDefault();
    }

    private static string NormalizeQueryForGuardrails(string query)
    {
        var noComments = Regex.Replace(query, @"--.*?$", string.Empty, RegexOptions.Multiline);
        noComments = Regex.Replace(noComments, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return noComments.Trim();
    }

    private static bool ContainsMultipleStatements(string query)
    {
        var semicolonCount = query.Count(c => c == ';');
        if (semicolonCount == 0)
        {
            return false;
        }

        if (semicolonCount == 1 && query.EndsWith(';'))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeSelect(string query)
    {
        if (query.StartsWith("select", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (query.StartsWith("with", StringComparison.OrdinalIgnoreCase) && query.Contains("select", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string? GetString(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value.ToString();
    }

    private static ToolResult Fail(string message) => new()
    {
        Success = false,
        Error = message,
        Output = string.Empty
    };
}