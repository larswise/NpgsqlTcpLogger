using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NpgsqlTcpLogger;

public class TcpsqlLogger : ILogger
{
    private readonly string _category;
    private readonly int _port; // Default port, can be overridden in constructor
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly bool _enableCallerDetection;

    // Connection pooling for better performance
    private static readonly ConcurrentQueue<TcpClient> _connectionPool = new();
    private static readonly object _poolLock = new();
    private const int MaxPoolSize = 10;
    private const int MaxRetries = 3;

    // Reflection caching for better performance
    private static readonly ConcurrentDictionary<
        Type,
        (FieldInfo? BatchField, FieldInfo? SqlField, FieldInfo? ParamField)
    > _fieldCache = new();

    // Compiled regex patterns for better performance
    private static readonly Regex SqlStatementRegex = new(
        @":\s(.+?)(?:\s+Parameters:|\s*$)",
        RegexOptions.Compiled
    );
    private static readonly Regex DurationRegex = new(@"duration=(\d+)ms", RegexOptions.Compiled);
    private static readonly Regex PureSqlRegex = new(@".:\s(.*)", RegexOptions.Compiled);

    public TcpsqlLogger(
        string category,
        int port,
        IHttpContextAccessor? httpContextAccessor,
        bool enableCallerDetection = true
    )
    {
        _category = category;
        _port = port;
        _httpContextAccessor = httpContextAccessor;
        _enableCallerDetection = enableCallerDetection;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
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
            var statement = formatter(state, exception);
            var timestamp = DateTime.UtcNow;

            // Grab pure SQL from log (removes Npgsql prefix)
            var sqlMatch = SqlStatementRegex.Match(statement);
            string sql = sqlMatch.Success ? sqlMatch.Groups[1].Value.Trim() : statement;

            // Extract typed parameters from state using reflection
            var interpolatedSql = state != null ? ExtractInterpolatedSqlFromState(state) : null;

            // HTTP context info
            var httpCtx = _httpContextAccessor?.HttpContext;
            if (httpCtx?.Request.Method == HttpMethods.Options)
            {
                return; // Skip OPTIONS requests
            }

            // Enhanced caller detection - find the actual business logic method (optional for performance)
            var (callerClass, callerMethod, callerNamespace) = _enableCallerDetection
                ? GetActualCaller()
                : (null, null, null);

            // HTTP context info (fallback for web apps)
            string? endpoint =
                httpCtx != null ? $"{httpCtx.Request.Path} [{httpCtx.Request.Method}]" : null;
            var match = DurationRegex.Match(statement);
            var duration = 0;
            int.TryParse(match.Groups[1].Value, out duration);

            var pureSqlRegex = PureSqlRegex.Match(statement);
            var pureSql = pureSqlRegex.Groups[1]?.Value;

            var loggedStatement = interpolatedSql ?? "what happening here??";
            var payload = new
            {
                timestamp,
                statement = loggedStatement,
                duration,
                endpoint,
                caller_class = callerClass,
                caller_method = callerMethod,
                caller_namespace = callerNamespace,
                http_method = httpCtx?.Request.Method,
            };

            var json = JsonSerializer.Serialize(payload);
            // Make logging async to avoid blocking the main thread
            _ = Task.Run(() => SendLog(json));
        }
        catch
        {
            // Fail silently or retry logic
        }
    }

    private string? ExtractInterpolatedSqlFromState(object state)
    {
        var stateType = state.GetType();

        // Get cached field info or create new entry
        var (batchField, sqlField, paramField) = _fieldCache.GetOrAdd(
            stateType,
            type =>
            {
                var batch = type.GetField(
                    "_BatchCommands",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                var sql = type.GetField(
                    "_CommandText",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                var param = type.GetField(
                    "_Parameters",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                return (batch, sql, param);
            }
        );

        // Handle batch
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
        if (sqlField?.GetValue(state) is string singleSql)
        {
            var parameters = paramField?.GetValue(state) as object[];
            return InterpolateSql(singleSql, parameters);
        }

        return null;
    }

    string InterpolateSql(string rawSql, object[]? parameters)
    {
        string result = rawSql;

        if (parameters == null || parameters.Length == 0)
        {
            return result; // No parameters to interpolate
        }

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

    private (string? callerClass, string? callerMethod, string? callerNamespace) GetActualCaller()
    {
        var stackTrace = new StackTrace();
        var frames = stackTrace.GetFrames();

        if (frames == null)
            return (null, null, null);

        // Skip frames until we get past Npgsql, logging infrastructure, and framework code
        for (int i = 0; i < frames.Length; i++)
        {
            var method = frames[i].GetMethod();
            if (method?.DeclaringType == null)
                continue;

            var declaringType = method.DeclaringType;
            var namespaceName = declaringType.Namespace ?? "";
            var className = declaringType.Name;
            var methodName = method.Name;

            // Skip our own logging code
            if (namespaceName.StartsWith("NpgsqlTcpLogger"))
                continue;

            // Skip ALL Npgsql internal code (including AbstractBatcher, etc.)
            if (namespaceName.StartsWith("Npgsql"))
                continue;

            // Skip Microsoft logging infrastructure
            if (namespaceName.StartsWith("Microsoft.Extensions.Logging"))
                continue;

            // Skip Entity Framework if present
            if (namespaceName.StartsWith("Microsoft.EntityFrameworkCore"))
                continue;

            // Skip Dapper if present
            if (namespaceName.StartsWith("Dapper"))
                continue;

            // Skip System namespaces
            if (namespaceName.StartsWith("System"))
                continue;

            // Skip .NET runtime namespaces
            if (namespaceName.StartsWith("Microsoft.Extensions"))
                continue;

            // Skip async state machine generated methods
            if (className.Contains("<") || methodName.Contains("<"))
                continue;

            // Skip compiler-generated classes
            if (className.StartsWith("<>"))
                continue;

            // Skip specific known internal classes that might slip through
            if (
                className.Contains("Batcher")
                || className.Contains("Reader")
                || className.Contains("Command")
            )
                continue;

            // Skip generic Task/async infrastructure
            if (namespaceName.StartsWith("System.Threading.Tasks"))
                continue;

            // Skip more specific Npgsql patterns that might slip through
            if (
                className.Contains("Abstract")
                || className.Contains("Internal")
                || className.Contains("Connection")
            )
                continue;

            // Skip runtime and compiler generated stuff
            if (namespaceName.StartsWith("Microsoft.") || namespaceName.StartsWith("System."))
                continue;

            // Skip empty or null namespaces (usually internal stuff)
            if (string.IsNullOrEmpty(namespaceName))
                continue;

            // Skip common data access layer patterns - we want the business logic that calls them
            if (
                className.Contains("Loader")
                || className.Contains("Repository")
                || className.Contains("DataAccess")
                || className.Contains("Dal")
                || className.Contains("Dao")
                || className.Contains("Session") // NHibernate, custom sessions
                || className.EndsWith("Impl") // Implementation classes
                || methodName.Contains("Query")
                || methodName.Contains("Execute")
                || methodName.Contains("Fetch")
                || methodName.Contains("Load")
                || methodName.Contains("List") // Common CRUD operation
                || methodName.Contains("Get") // Common CRUD operation
                || methodName.Contains("Save") // Common CRUD operation
                || methodName.Contains("Update") // Common CRUD operation
                || methodName.Contains("Delete") // Common CRUD operation
                || methodName.Contains("Find") // Common CRUD operation
                || className.Contains("Context") // EF DbContext
                || className.Contains("Gateway")
                || className.Contains("Mapper")
                || className.Contains("Manager") // Data managers
                || className.Contains("Provider") // Data providers
                || className.Contains("Service") // Generic services (might be data services)
            )
                continue;

            // This should be our actual caller
            return (className, methodName, namespaceName);
        }

        // Fallback if we couldn't find a good caller
        return (null, null, null);
    }

    private void SendLog(string json)
    {
        for (int retry = 0; retry < MaxRetries; retry++)
        {
            TcpClient? client = null;
            try
            {
                // Try to get a connection from the pool
                if (!_connectionPool.TryDequeue(out client) || !IsClientConnected(client))
                {
                    client?.Close();
                    client = new TcpClient();
                    client.Connect("localhost", _port);
                }

                // Send the log
                using var writer = new StreamWriter(client.GetStream());
                writer.WriteLine(json);
                writer.Flush();

                // Return connection to pool if it's still good and pool isn't full
                if (IsClientConnected(client))
                {
                    lock (_poolLock)
                    {
                        if (_connectionPool.Count < MaxPoolSize)
                        {
                            _connectionPool.Enqueue(client);
                            client = null; // Don't dispose it
                        }
                    }
                }

                return; // Success, exit retry loop
            }
            catch
            {
                // Connection failed, clean up and retry
                client?.Close();
                client = null;

                if (retry == MaxRetries - 1)
                {
                    // Last retry failed, give up silently
                    return;
                }
            }
            finally
            {
                // Only dispose if we didn't return it to the pool
                client?.Close();
            }
        }
    }

    private static bool IsClientConnected(TcpClient? client)
    {
        try
        {
            return client?.Connected == true
                && client.Client?.Poll(0, SelectMode.SelectRead) == false;
        }
        catch
        {
            return false;
        }
    }
}
