using System.IO;
using Microsoft.Extensions.Logging;

namespace ElBruno.FoundryLocalMonitor.Services;

/// <summary>Simple append-only file logger — writes timestamped lines to a single file.</summary>
public sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    private readonly StreamWriter _writer = new(path, append: true) { AutoFlush = true };
    private readonly Lock _lock = new();

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _writer, _lock);

    public void Dispose() => _writer.Dispose();
}

file sealed class FileLogger(string category, StreamWriter writer, Lock @lock) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Debug;

    public void Log<TState>(LogLevel level, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level,-11}] {ShortCategory(category)}: {formatter(state, exception)}";
        if (exception != null) line += $"\n  {exception.GetType().Name}: {exception.Message}";
        lock (@lock) writer.WriteLine(line);
    }

    private static string ShortCategory(string cat)
    {
        var dot = cat.LastIndexOf('.');
        return dot >= 0 ? cat[(dot + 1)..] : cat;
    }
}
