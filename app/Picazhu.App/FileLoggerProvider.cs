using System.IO;
using Microsoft.Extensions.Logging;

namespace Picazhu.App;

public sealed class FileLoggerProvider(string logsPath) : ILoggerProvider
{
    private readonly string _logFilePath = Path.Combine(logsPath, $"picazhu-{DateTime.UtcNow:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoryName) => new FileLogger(_logFilePath, categoryName);
    public void Dispose() { }

    private sealed class FileLogger(string path, string category) : ILogger
    {
        private static readonly object Sync = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = $"{DateTimeOffset.UtcNow:O}\t{logLevel}\t{category}\t{formatter(state, exception)}";
            lock (Sync)
            {
                File.AppendAllLines(path, exception is null ? [line] : [line, exception.ToString()]);
            }
        }
    }
}
