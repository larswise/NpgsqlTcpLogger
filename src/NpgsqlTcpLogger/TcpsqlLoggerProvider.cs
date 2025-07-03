namespace NpgsqlTcpLogger;

public class TcpsqlLoggerProvider : ILoggerProvider
{
    private readonly int _port;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly bool _enableCallerDetection;

    public TcpsqlLoggerProvider(IHttpContextAccessor httpContextAccessor, int port = 6000, bool enableCallerDetection = true)
    {
        _port = port;
        _httpContextAccessor = httpContextAccessor;
        _enableCallerDetection = enableCallerDetection;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TcpsqlLogger(categoryName, _port, _httpContextAccessor, _enableCallerDetection);
    }

    public void Dispose()
    {
        // Nothing to dispose here
    }
}
