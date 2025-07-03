# NpgsqlTcpLogger

NpgsqlTcpLogger is a high-performance logging library that captures PostgreSQL database queries from Npgsql and transmits them via TCP to a monitoring application in real-time. It provides detailed SQL query logging with parameter interpolation, execution duration tracking, and intelligent caller detection to identify the exact business logic method that triggered each database call. The logger is optimized for production use with connection pooling, reflection caching, compiled regex patterns, and async logging to minimize performance impact on your application.

## Installation

Install the package via NuGet:

```bash
dotnet add package NpgsqlTcpLogger
```

## Usage

### For ASP.NET Core Web Applications

```csharp
var app = WebApplication.CreateBuilder(args).Build();

// Add NpgsqlTcpLogger with default settings
app.AddNpgsqlTcpLogger();

// Or customize port and performance settings
app.AddNpgsqlTcpLogger(port: 6000, enableCallerDetection: true);

app.Run();
```

### For Console Applications and Hosted Services

```csharp
// Using IHostBuilder
var host = Host.CreateDefaultBuilder(args)
    .AddNpgsqlTcpLogger(port: 6000)
    .Build();

// Using HostApplicationBuilder
var builder = Host.CreateApplicationBuilder(args);
builder.AddNpgsqlTcpLogger(port: 6000);
var host = builder.Build();
```

### Performance Optimization

For high-throughput applications, you can disable caller detection to improve performance:

```csharp
app.AddNpgsqlTcpLogger(port: 6000, enableCallerDetection: false);
```

## Monitoring Application

To view the captured SQL queries, you'll need the companion monitoring application [npgsql-mon](https://github.com/larswise/npgsql-mon) which provides a real-time dashboard for analyzing your database interactions.

## Features

- **Real-time SQL logging** with parameter interpolation
- **Execution duration tracking** for performance analysis
- **Intelligent caller detection** to identify source business logic
- **High-performance design** with connection pooling and caching
- **Async logging** to prevent blocking your application
- **Support for both web and console applications**
- **Configurable performance settings** for different use cases

## Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| `port` | 6000 | TCP port for sending logs |
| `enableCallerDetection` | true | Enable stack trace analysis (disable for better performance) |
