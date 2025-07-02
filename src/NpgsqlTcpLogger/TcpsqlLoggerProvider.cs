namespace NpgsqlTcpLogger;

public class TcpsqlLoggerProvider : ILoggerProvider
{
    private readonly int _port;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TcpsqlLoggerProvider(IHttpContextAccessor httpContextAccessor, int port = 6000)
    {
        _port = port;
        _httpContextAccessor = httpContextAccessor;
    }

    public ILogger CreateLogger(string categoryName)
    {
        // If IHttpContextAccessor is not provided, we can ignore it or handle it as needed.
        // For this example, we'll just pass null.
        Console.WriteLine($"Creating logger for category: {categoryName} on port {_port}");
        return new TcpsqlLogger(categoryName, _port, _httpContextAccessor);
    }

    public void Dispose()
    {
        // Nothing to dispose here
    }
}
