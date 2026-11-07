using System.Text;

namespace PluginShellWin.Infrastructure;

/// <summary>极简日志：滚动文件 + 控制台。写失败绝不抛异常（只读环境要能跑）。</summary>
public sealed class AppLogger : IDisposable
{
    private const long MaxBytes = 1_000_000;
    private readonly object _gate = new();
    private readonly string? _filePath;
    private readonly bool _echoToConsole;
    private readonly string _minimumLevel;

    public AppLogger(string logsDirectory, string level = "INFO", bool echoToConsole = false)
    {
        _minimumLevel = Normalize(level);
        _echoToConsole = echoToConsole;
        try
        {
            Directory.CreateDirectory(logsDirectory);
            _filePath = Path.Combine(logsDirectory, "app.log");
            RotateIfNeeded();
        }
        catch (Exception)
        {
            _filePath = null;
        }
    }

    public void Debug(string message) => Write("DEBUG", message);

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARNING", message);

    public void Error(string message, Exception? error = null)
        => Write("ERROR", error is null ? message : $"{message} :: {error.GetType().Name}: {error.Message}");

    public void Write(string level, string message)
    {
        if (Rank(level) < Rank(_minimumLevel))
        {
            return;
        }
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-7} {message}";
        lock (_gate)
        {
            if (_echoToConsole)
            {
                Console.Error.WriteLine(line);
            }
            if (_filePath is null)
            {
                return;
            }
            try
            {
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
                var info = new FileInfo(_filePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    RotateIfNeeded(force: true);
                }
            }
            catch (Exception)
            {
                // 忽略：日志失败不能影响业务
            }
        }
    }

    private void RotateIfNeeded(bool force = false)
    {
        if (_filePath is null)
        {
            return;
        }
        try
        {
            var info = new FileInfo(_filePath);
            if (!info.Exists || (!force && info.Length <= MaxBytes))
            {
                return;
            }
            var backup = _filePath + ".1";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
            File.Move(_filePath, backup);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    private static string Normalize(string level) => level.Trim().ToUpperInvariant() switch
    {
        "DEBUG" => "DEBUG",
        "WARN" => "WARNING",
        "WARNING" => "WARNING",
        "ERROR" => "ERROR",
        _ => "INFO",
    };

    private static int Rank(string level) => level switch
    {
        "DEBUG" => 0,
        "INFO" => 1,
        "WARNING" => 2,
        _ => 3,
    };

    public void Dispose()
    {
        // 无句柄持有；保留接口便于将来接入异步写入队列
    }
}
