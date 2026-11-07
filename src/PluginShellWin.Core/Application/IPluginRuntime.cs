using PluginShellWin.Core.Domain;

namespace PluginShellWin.Core.Application;

/// <summary>
/// 插件运行时的端口（端口-适配器模式）：应用层只依赖这个接口，
/// 具体实现既可以是子进程 + JSON-Lines，将来也可以是本地 DLL 或远程服务。
/// </summary>
public interface IPluginRuntime : IAsyncDisposable, IDisposable
{
    string PluginsDirectory { get; }

    Task<RuntimeHandshake> HandshakeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PluginInfo>> GetPluginsAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CategoryInfo> Categories, int Total)> GetCategoriesAsync(
        CancellationToken cancellationToken = default);

    Task<OperationResult> InstallAsync(string path, CancellationToken cancellationToken = default);

    Task<OperationResult> EnableAsync(string pluginId, CancellationToken cancellationToken = default);

    Task<OperationResult> DisableAsync(string pluginId, CancellationToken cancellationToken = default);

    Task<OperationResult> UninstallAsync(string pluginId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetLogsAsync(int limit = 120, CancellationToken cancellationToken = default);

    Task<RuntimeWindow> ShowContentAsync(
        string? categoryId, int width, int height, CancellationToken cancellationToken = default);

    Task ResizeContentAsync(int width, int height, CancellationToken cancellationToken = default);
}
