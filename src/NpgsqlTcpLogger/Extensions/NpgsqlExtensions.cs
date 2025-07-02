using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace NpgsqlTcpLogger.Extensions;

public static class NpgsqlExtensions
{
    /// <summary>
    /// Adds the NpgsqlTcpLogger to the logging pipeline.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="port">The port to send logs to.</param>
    /// <returns>The updated logging builder.</returns>
    public static WebApplication AddNpgsqlTcpLogger(this WebApplication app, int port = 6000)
    {
        var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();

        var loggerFactory = LoggerFactory.Create(loggingBuilder =>
        {
            loggingBuilder
                .AddFilter("Npgsql", LogLevel.Information)
                .AddProvider(new TcpsqlLoggerProvider(accessor));
        });

        // 🔥 Register that factory with Npgsql
        NpgsqlLoggingConfiguration.InitializeLogging(loggerFactory, true);
        return app;
    }
}
