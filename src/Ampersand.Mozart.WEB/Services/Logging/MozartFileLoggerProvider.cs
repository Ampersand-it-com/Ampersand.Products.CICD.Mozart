using Ampersand.Mozart.WEB.Models;

namespace Ampersand.Mozart.WEB.Services.Logging;

public sealed class MozartFileLoggerProvider(MozartErrorLog errorLog) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new MozartFileLogger(categoryName, errorLog);

    public void Dispose()
    {
    }
}

internal sealed class MozartFileLogger(string category, MozartErrorLog errorLog) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        errorLog.Write(category, message, exception);
    }
}
