namespace SmartStylist.Worker;

/// <summary>
/// One log file per process run (logs/run-&lt;utc time&gt;.log): every ILogger message that passes the Logging:LogLevel
/// filters, plus whatever the CLI writes here itself.
/// </summary>
internal sealed class RunLogFile : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();

    public RunLogFile(string directory)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(FilePath, append: true) { AutoFlush = true };
    }

    public string FilePath { get; }

    public void WriteLine(string text)
    {
        lock (_lock)
        {
            _writer.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z {text}");
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger(RunLogFile file, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = $"{logLevel,-11} {category}: {formatter(state, exception)}";
            file.WriteLine(exception is null ? message : $"{message}{Environment.NewLine}{exception}");
        }
    }
}
