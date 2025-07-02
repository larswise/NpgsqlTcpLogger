using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NpgsqlTcpLogger;

public class TcpsqlLogger : ILogger
{
    private readonly string _category;
    private readonly int _port; // Default port, can be overridden in constructor
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TcpsqlLogger(string category, int port, IHttpContextAccessor httpContextAccessor)
    {
        _category = category;
        _port = port;
        _httpContextAccessor = httpContextAccessor;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        return null!;
    }

    public bool IsEnabled(LogLevel logLevel) => _category.StartsWith("Npgsql");

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        try
        {
            Console.WriteLine("Log entry");
            var statement = formatter(state, exception);
            var timestamp = DateTime.UtcNow;

            // Grab pure SQL from log (removes Npgsql prefix)
            var sqlMatch = Regex.Match(statement, @":\s(.+?)(?:\s+Parameters:|\s*$)");
            string sql = sqlMatch.Success ? sqlMatch.Groups[1].Value.Trim() : statement;

            // Extract typed parameters from state using reflection
            var interpolatedSql = ExtractInterpolatedSqlFromState(state);

            // HTTP context info
            var httpCtx = _httpContextAccessor.HttpContext;
            if (httpCtx.Request.Method == HttpMethods.Options)
            {
                Console.WriteLine("Skipping OPTIONS request");
                return; // Skip OPTIONS requests
            }
            string? path = $"{httpCtx?.Request.Path} [{httpCtx?.Request.Method}]";
            string? controller = httpCtx?.RequestServices.ToString();

            // Caller info (optional, see below)
            var stack = new StackTrace();
            var frame = stack.GetFrame(3); // Adjust depth
            var method = frame?.GetMethod();
            string? caller = $"{method?.DeclaringType?.Name}.{method?.Name}";
            var match = Regex.Match(statement, @"duration=(\d+)ms");
            var duration = 0;
            int.TryParse(match.Groups[1].Value, out duration);

            var pureSqlRegex = Regex.Match(statement, @".:\s(.*)");
            var pureSql = pureSqlRegex.Groups[1]?.Value;

            var loggedStatement = interpolatedSql ?? "what happening here??";
            Console.WriteLine($"Logged SQL: {loggedStatement}");
            var payload = new
            {
                timestamp,
                statement = loggedStatement,
                duration,
                endpoint = path,
                controller,
                caller,
                http_method = httpCtx?.Request.Method,
            };

            var json = JsonSerializer.Serialize(payload);

            var client = new TcpClient("localhost", _port); // Simple reconnect each time (optimize later)
            using var writer = new StreamWriter(client.GetStream());
            writer.WriteLine(json);
        }
        catch
        {
            // Fail silently or retry logic
        }
    }

    private string? ExtractInterpolatedSqlFromState(object state)
    {
        var stateType = state.GetType();

        // Handle batch
        var batchField = stateType.GetField(
            "_BatchCommands",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        if (batchField?.GetValue(state) is ValueTuple<string, object[]>[] batchCommands)
        {
            var sb = new System.Text.StringBuilder();

            for (int i = 0; i < batchCommands.Length; i++)
            {
                var (sql, sqlParameters) = batchCommands[i];
                sb.AppendLine($"[-- Batch Command {i + 1}]");
                sb.AppendLine(InterpolateSql(sql, sqlParameters));
                sb.AppendLine(); // Extra newline for spacing
            }

            return sb.ToString();
        }

        // Handle single command
        var sqlField = stateType.GetField(
            "_CommandText",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var paramField = stateType.GetField(
            "_Parameters",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        if (sqlField?.GetValue(state) is string singleSql)
        {
            var parameters = paramField?.GetValue(state) as object[];
            return InterpolateSql(singleSql, parameters);
        }

        return null;
    }

    string InterpolateSql(string rawSql, object[] parameters)
    {
        string result = rawSql;

        for (int i = parameters.Length; i >= 1; i--) // Descending: $10 before $1
        {
            var value = parameters[i - 1];
            string replacement;

            switch (value)
            {
                case null:
                    replacement = "NULL";
                    break;
                case string s:
                    replacement = $"'{s.Replace("'", "''")}'";
                    break;
                case bool b:
                    replacement = b ? "TRUE" : "FALSE";
                    break;
                case DateTime dt:
                    replacement = $"'{dt:yyyy-MM-dd HH:mm:ss}'";
                    break;
                case Guid guid:
                    replacement = $"'{guid}'";
                    break;
                default:
                    replacement = value.ToString()!;
                    break;
            }

            // Use \b (word boundary) to avoid matching $10 when looking for $1
            result = Regex.Replace(result, $@"\${i}\b", replacement);
        }

        return result;
    }
}
