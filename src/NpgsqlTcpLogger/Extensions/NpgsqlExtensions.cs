using Npgsql;

namespace NpgsqlTcpLogger.Extensions;

public static class NpgsqlExtensions
{
    /// <summary>
    /// Adds the NpgsqlTcpLogger to the logging pipeline for web applications.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="port">The port to send logs to.</param>
    /// <param name="enableCallerDetection">Whether to enable expensive caller detection for better debugging (default: true).</param>
    /// <returns>The updated web application.</returns>
    public static WebApplicationBuilder AddNpgsqlTcpLogger(
        this WebApplicationBuilder builder,
        int port = 6000,
        bool enableCallerDetection = true
    )
    {
        HttpContextAccessor? accessor = null;
        var hasAccessor = builder.Services.Any(b => b.ServiceType == typeof(IHttpContextAccessor));
        if(!hasAccessor) 
        {
            accessor = new HttpContextAccessor();
            builder.Services.AddSingleton<IHttpContextAccessor>(accessor);
        }
        else {
            accessor = builder.Services
                .First(b => b.ServiceType == typeof(IHttpContextAccessor))
                .ImplementationInstance as HttpContextAccessor;
        }

        var loggerFactory = LoggerFactory.Create(loggingBuilder =>
        {
            loggingBuilder
                .AddFilter("Npgsql", LogLevel.Information)
                .AddProvider(new TcpsqlLoggerProvider(accessor, port, enableCallerDetection));
        });

        // 🔥 Register that factory with Npgsql
        NpgsqlLoggingConfiguration.InitializeLogging(loggerFactory, true);
        return builder;
    }

    /// <summary>
    /// Adds the NpgsqlTcpLogger to the logging pipeline for console applications and hosted services.
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="port">The port to send logs to.</param>
    /// <param name="enableCallerDetection">Whether to enable expensive caller detection for better debugging (default: true).</param>
    /// <returns>The updated host builder.</returns>
    public static IHostBuilder AddNpgsqlTcpLogger(
        this IHostBuilder builder,
        int port = 6000,
        bool enableCallerDetection = true
    )
    {
        return builder.ConfigureLogging(loggingBuilder =>
        {
            var loggerFactory = LoggerFactory.Create(lb =>
            {
                lb.AddFilter("Npgsql", LogLevel.Information)
                    .AddProvider(new TcpsqlConsoleLoggerProvider(port, enableCallerDetection));
            });

            // 🔥 Register that factory with Npgsql
            NpgsqlLoggingConfiguration.InitializeLogging(loggerFactory, true);
        });
    }

    /// <summary>
    /// Adds the NpgsqlTcpLogger to the logging pipeline for console applications using HostApplicationBuilder.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="port">The port to send logs to.</param>
    /// <param name="enableCallerDetection">Whether to enable expensive caller detection for better debugging (default: true).</param>
    /// <returns>The updated host application builder.</returns>
    public static HostApplicationBuilder AddNpgsqlTcpLogger(
        this HostApplicationBuilder builder,
        int port = 6000,
        bool enableCallerDetection = true
    )
    {
        var loggerFactory = LoggerFactory.Create(loggingBuilder =>
        {
            loggingBuilder
                .AddFilter("Npgsql", LogLevel.Information)
                .AddProvider(new TcpsqlConsoleLoggerProvider(port, enableCallerDetection));
        });

        // 🔥 Register that factory with Npgsql
        NpgsqlLoggingConfiguration.InitializeLogging(loggerFactory, true);
        return builder;
    }
}
