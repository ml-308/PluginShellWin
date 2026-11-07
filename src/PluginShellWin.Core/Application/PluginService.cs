using PluginShellWin.Core.Domain;

namespace PluginShellWin.Core.Application;

/// <summary>
/// 插件用例服务：表现层唯一的入口。
/// 命令类方法统一返回 <see cref="OperationResult"/>，把运行时异常翻译成可展示的结果。
/// </summary>
public sealed class PluginService
{
    private readonly IPluginRuntime _runtime;

    public PluginService(IPluginRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public string PluginsDirectory => _runtime.PluginsDirectory;

    public Task<RuntimeHandshake> HandshakeAsync(CancellationToken cancellationToken = default)
        => _runtime.HandshakeAsync(cancellationToken);

    public async Task<IReadOnlyList<PluginInfo>> ListPluginsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _runtime.GetPluginsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new PluginRuntimeException("读取插件列表失败：" + ex.Message, ex);
        }
    }

    public async Task<PluginSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var plugins = await ListPluginsAsync(cancellationToken).ConfigureAwait(false);
        var (categories, total) = await _runtime.GetCategoriesAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PluginSnapshot(plugins, categories, total);
    }

    public Task<OperationResult> InstallAsync(string path, CancellationToken cancellationToken = default)
        => GuardAsync(() => _runtime.InstallAsync(path, cancellationToken), "安装插件");

    public Task<OperationResult> EnableAsync(string pluginId, CancellationToken cancellationToken = default)
        => GuardAsync(() => _runtime.EnableAsync(pluginId, cancellationToken), "启用插件");

    public Task<OperationResult> DisableAsync(string pluginId, CancellationToken cancellationToken = default)
        => GuardAsync(() => _runtime.DisableAsync(pluginId, cancellationToken), "禁用插件");

    public Task<OperationResult> UninstallAsync(string pluginId, CancellationToken cancellationToken = default)
        => GuardAsync(() => _runtime.UninstallAsync(pluginId, cancellationToken), "卸载插件");

    public async Task<IReadOnlyList<string>> GetLogsAsync(
        int limit = 120, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _runtime.GetLogsAsync(limit, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new[] { $"[宿主] 读取运行时日志失败：{ex.Message}" };
        }
    }

    public Task<RuntimeWindow> ShowContentAsync(
        string? categoryId, int width, int height, CancellationToken cancellationToken = default)
        => _runtime.ShowContentAsync(categoryId, width, height, cancellationToken);

    public Task ResizeContentAsync(int width, int height, CancellationToken cancellationToken = default)
        => _runtime.ResizeContentAsync(width, height, cancellationToken);

    private static async Task<OperationResult> GuardAsync(
        Func<Task<OperationResult>> action, string label)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OperationResult.Failure($"{label}失败：{ex.Message}");
        }
    }
}

/// <summary>一次性取回的插件快照，供 UI 单次刷新使用。</summary>
public sealed record PluginSnapshot(
    IReadOnlyList<PluginInfo> Plugins,
    IReadOnlyList<CategoryInfo> Categories,
    int ContributionCount)
{
    public int EnabledCount => Plugins.Count(item => item.State == PluginState.Enabled);
}
