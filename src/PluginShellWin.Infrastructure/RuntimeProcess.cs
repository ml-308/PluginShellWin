using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PluginShellWin.Core.Application;
using PluginShellWin.Core.Domain;

namespace PluginShellWin.Infrastructure;

/// <summary>
/// 插件运行时的子进程适配器：stdin/stdout 上的 JSON-Lines 请求-响应，
/// 每个请求带自增 id，异常/超时都翻译成 <see cref="PluginRuntimeException"/>。
/// </summary>
public sealed class RuntimeProcess : IPluginRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Process _process;
    private readonly AppLogger? _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private long _nextId;
    private bool _disposed;

    private RuntimeProcess(Process process, string pluginsDirectory, string description, AppLogger? logger)
    {
        _process = process;
        PluginsDirectory = pluginsDirectory;
        Description = description;
        _logger = logger;
        _stdoutPump = Task.Run(PumpStdoutAsync);
        _stderrPump = Task.Run(PumpStderrAsync);
    }

    public string PluginsDirectory { get; }

    public string Description { get; }

    public static RuntimeProcess Start(
        RuntimeLaunch launch, string pluginsDirectory, AppLogger? logger = null)
    {
        Directory.CreateDirectory(pluginsDirectory);

        var startInfo = new ProcessStartInfo(launch.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add("--plugins");
        startInfo.ArgumentList.Add(pluginsDirectory);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new PluginRuntimeException("插件运行时进程启动失败（Process.Start 返回 null）");
        }
        catch (PluginRuntimeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PluginRuntimeException($"无法启动插件运行时：{launch.FileName}", ex);
        }

        logger?.Info($"插件运行时已启动 pid={process.Id} 方式={launch.Description}");
        return new RuntimeProcess(process, pluginsDirectory, launch.Description, logger);
    }

    // ------------------------------------------------------------------
    public async Task<RuntimeHandshake> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("hello", null, cancellationToken, 90_000).ConfigureAwait(false);
        var handshake = new RuntimeHandshake(
            ReadString(result, "protocol"),
            ReadString(result, "app"),
            ReadString(result, "qt"),
            ReadString(result, "python"),
            result.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var pidValue) ? pidValue : 0,
            ReadString(result, "pluginsDir"));

        if (!handshake.Compatible)
        {
            throw new PluginRuntimeException(
                $"插件运行时协议 {handshake.Protocol} 与宿主协议 {AppInfo.Protocol} 不兼容");
        }
        return handshake;
    }

    public async Task<IReadOnlyList<PluginInfo>> GetPluginsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("plugins", null, cancellationToken).ConfigureAwait(false);
        var list = new List<PluginInfo>();
        if (!result.TryGetProperty("plugins", out var plugins)
            || plugins.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in plugins.EnumerateArray())
        {
            var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
            if (item.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in deps.EnumerateObject())
                {
                    dependencies[property.Name] = property.Value.GetString() ?? "*";
                }
            }

            var requires = new List<string>();
            if (item.TryGetProperty("requiresServices", out var services)
                && services.ValueKind == JsonValueKind.Array)
            {
                requires.AddRange(services.EnumerateArray()
                    .Select(entry => entry.GetString())
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Select(value => value!));
            }

            list.Add(new PluginInfo(
                ReadString(item, "id"),
                ReadString(item, "name"),
                ReadString(item, "version"),
                PluginStateExtensions.FromWire(ReadString(item, "state")),
                ReadString(item, "categoryId"),
                ReadString(item, "categoryLabel"),
                ReadString(item, "description"),
                ReadString(item, "message"),
                dependencies,
                requires));
        }
        return list;
    }

    public async Task<(IReadOnlyList<CategoryInfo> Categories, int Total)> GetCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("categories", null, cancellationToken).ConfigureAwait(false);
        var categories = new List<CategoryInfo>();
        if (result.TryGetProperty("categories", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                categories.Add(new CategoryInfo(
                    ReadString(item, "id"),
                    ReadString(item, "label"),
                    item.TryGetProperty("order", out var order) && order.TryGetInt32(out var orderValue)
                        ? orderValue
                        : 100,
                    item.TryGetProperty("count", out var count) && count.TryGetInt32(out var countValue)
                        ? countValue
                        : 0));
            }
        }
        var total = result.TryGetProperty("total", out var totalProp) && totalProp.TryGetInt32(out var value)
            ? value
            : categories.Sum(item => item.Count);
        return (categories, total);
    }

    public async Task<OperationResult> InstallAsync(string path, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(
            "install", new Dictionary<string, object?> { ["path"] = path }, cancellationToken)
            .ConfigureAwait(false);
        return ToResult(result);
    }

    public async Task<OperationResult> EnableAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(
            "enable", new Dictionary<string, object?> { ["pluginId"] = pluginId }, cancellationToken)
            .ConfigureAwait(false);
        return ToResult(result);
    }

    public async Task<OperationResult> DisableAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(
            "disable", new Dictionary<string, object?> { ["pluginId"] = pluginId }, cancellationToken)
            .ConfigureAwait(false);
        return ToResult(result);
    }

    public async Task<OperationResult> UninstallAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(
            "uninstall", new Dictionary<string, object?> { ["pluginId"] = pluginId }, cancellationToken)
            .ConfigureAwait(false);
        return ToResult(result);
    }

    public async Task<IReadOnlyList<string>> GetLogsAsync(
        int limit = 120, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(
            "logs", new Dictionary<string, object?> { ["limit"] = limit }, cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetProperty("logs", out var logs) || logs.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }
        return logs.EnumerateArray()
            .Select(entry => entry.GetString() ?? string.Empty)
            .Where(text => text.Length > 0)
            .ToList();
    }

    public async Task<RuntimeWindow> ShowContentAsync(
        string? categoryId, int width, int height, CancellationToken cancellationToken = default)
    {
        var result = await CallAsync(
            "show",
            new Dictionary<string, object?>
            {
                ["category"] = categoryId,
                ["width"] = width,
                ["height"] = height,
            },
            cancellationToken).ConfigureAwait(false);

        var hwnd = result.TryGetProperty("hwnd", out var handle) ? handle.GetInt64() : 0;
        var count = result.TryGetProperty("count", out var panelCount) && panelCount.TryGetInt32(out var value)
            ? value
            : 0;
        if (hwnd == 0)
        {
            throw new PluginRuntimeException("插件运行时没有返回有效的窗口句柄");
        }
        return new RuntimeWindow(hwnd, count);
    }

    public async Task ResizeContentAsync(int width, int height, CancellationToken cancellationToken = default)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }
        await CallAsync(
            "resize",
            new Dictionary<string, object?> { ["width"] = width, ["height"] = height },
            cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    private async Task<JsonElement> CallAsync(
        string command,
        Dictionary<string, object?>? arguments,
        CancellationToken cancellationToken,
        int timeoutMs = 30_000)
    {
        if (_disposed)
        {
            throw new PluginRuntimeException("插件运行时已释放");
        }

        var id = Interlocked.Increment(ref _nextId);
        var payload = arguments is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(arguments);
        payload["id"] = id;
        payload["cmd"] = command;
        var json = JsonSerializer.Serialize(payload);

        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending)
        {
            _pending[id] = completion;
        }

        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(json).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            return await completion.Task
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            lock (_pending)
            {
                _pending.Remove(id);
            }
            throw new PluginRuntimeException($"插件运行时响应超时：{command}（{timeoutMs} ms）");
        }
        catch (OperationCanceledException)
        {
            lock (_pending)
            {
                _pending.Remove(id);
            }
            throw;
        }
    }

    private async Task PumpStdoutAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
                if (line.Length == 0)
                {
                    continue;
                }
                Dispatch(line);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"读取插件运行时输出失败：{ex.Message}");
        }
        FailAllPending("插件运行时已退出");
    }

    private async Task PumpStderrAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var line = await _process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _logger?.Debug("[runtime] " + line);
                }
            }
        }
        catch (Exception)
        {
            // 进程退出时管道关闭，属正常情况
        }
    }

    private void Dispatch(string line)
    {
        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(line, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"运行时返回了无法解析的行：{ex.Message}");
            return;
        }

        if (!root.TryGetProperty("id", out var idProperty) || idProperty.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        var id = idProperty.GetInt64();
        TaskCompletionSource<JsonElement> completion;
        lock (_pending)
        {
            if (!_pending.TryGetValue(id, out completion!))
            {
                return;
            }
            _pending.Remove(id);
        }

        var ok = root.TryGetProperty("ok", out var okProperty)
                 && okProperty.ValueKind == JsonValueKind.True;
        if (ok)
        {
            var result = root.TryGetProperty("result", out var resultProperty)
                ? resultProperty.Clone()
                : default;
            completion.TrySetResult(result);
        }
        else
        {
            var error = root.TryGetProperty("error", out var errorProperty)
                ? errorProperty.GetString() ?? "插件运行时返回未知错误"
                : "插件运行时返回未知错误";
            completion.TrySetException(new PluginRuntimeException(error));
        }
    }

    private void FailAllPending(string reason)
    {
        List<TaskCompletionSource<JsonElement>> pending;
        lock (_pending)
        {
            pending = _pending.Values.ToList();
            _pending.Clear();
        }
        foreach (var item in pending)
        {
            item.TrySetException(new PluginRuntimeException(reason));
        }
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static OperationResult ToResult(JsonElement element)
    {
        var ok = element.TryGetProperty("ok", out var okProperty)
                 && okProperty.ValueKind == JsonValueKind.True;
        var message = ReadString(element, "message");
        return ok ? OperationResult.Success(message) : OperationResult.Failure(message);
    }

    // ------------------------------------------------------------------
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    var request = JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["id"] = Interlocked.Increment(ref _nextId),
                        ["cmd"] = "shutdown",
                    });
                    _process.StandardInput.WriteLine(request);
                    _process.StandardInput.Flush();
                }
                catch (Exception)
                {
                    // 进程可能已退出
                }

                if (!_process.WaitForExit(3000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception)
        {
            // 忽略关闭期异常
        }
        finally
        {
            _lifetime.Cancel();
            FailAllPending("插件运行时已关闭");
            _process.Dispose();
            _writeLock.Dispose();
            _lifetime.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
