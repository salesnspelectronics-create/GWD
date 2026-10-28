namespace GenericWeb.DeploymentManager.Services;
public sealed class LogService
{
    private readonly object _sync = new();
    private readonly AppDataService _data;
    public string LogFile { get; private set; }
    public event Action<string>? LineWritten;

    public LogService(AppDataService data)
    {
        _data = data;
        Directory.CreateDirectory(data.Logs);
        LogFile = CreateSessionLogPath();
    }

    public string StartNewSession()
    {
        lock (_sync)
        {
            LogFile = CreateSessionLogPath();
            return LogFile;
        }
    }

    public void Write(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {SecretRedactor.Redact(message).Trim()}";
        lock (_sync) File.AppendAllText(LogFile, line + Environment.NewLine);
        LineWritten?.Invoke(line);
    }

    private string CreateSessionLogPath()
    {
        Directory.CreateDirectory(_data.Logs);
        while (true)
        {
            var suffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
            var path = Path.Combine(_data.Logs, $"Deploy-{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}.log");
            if (!File.Exists(path)) return path;
        }
    }
}
