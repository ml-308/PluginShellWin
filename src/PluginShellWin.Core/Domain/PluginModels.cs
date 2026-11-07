using System.Text.Json.Serialization;

namespace PluginShellWin.Core.Domain;

/// <summary>插件运行状态。与运行时返回的字符串一一对应。</summary>
public enum PluginState
{
    Disabled,
    Enabled,
    Failed,
}

public static class PluginStateExtensions
{
    public static PluginState FromWire(string? value) => value switch
    {
        "enabled" => PluginState.Enabled,
        "failed" => PluginState.Failed,
        _ => PluginState.Disabled,
    };

    public static string ToLabel(this PluginState state) => state switch
    {
        PluginState.Enabled => "已启用",
        PluginState.Failed => "加载失败",
        _ => "已禁用",
    };
}

/// <summary>跨层传递的插件视图（不可变）。</summary>
public sealed record PluginInfo(
    string Id,
    string Name,
    string Version,
    PluginState State,
    string CategoryId,
    string CategoryLabel,
    string Description,
    string Message,
    IReadOnlyDictionary<string, string> Dependencies,
    IReadOnlyList<string> RequiresServices)
{
    [JsonIgnore]
    public string StateLabel => State.ToLabel();

    [JsonIgnore]
    public string DependencyText => Dependencies.Count == 0
        ? "—"
        : string.Join(", ", Dependencies.Select(pair => $"{pair.Key} ({pair.Value})"));
}

/// <summary>插件自报的分类（内核不内置任何分类）。</summary>
public sealed record CategoryInfo(string Id, string Label, int Order, int Count);

/// <summary>命令类操作的统一返回值：失败也是正常结果，不抛异常给 UI。</summary>
public readonly record struct OperationResult(bool Ok, string Message)
{
    public static OperationResult Success(string message = "") => new(true, message);

    public static OperationResult Failure(string message) => new(false, message);
}

/// <summary>运行时握手信息，用于确认协议版本是否兼容。</summary>
public sealed record RuntimeHandshake(
    string Protocol,
    string App,
    string Qt,
    string Python,
    int Pid,
    string PluginsDir)
{
    [JsonIgnore]
    public bool Compatible => Protocol == AppInfo.Protocol;
}

/// <summary>运行时创建出来的原生窗口句柄，供宿主 SetParent 嵌入。</summary>
public sealed record RuntimeWindow(long Hwnd, int PanelCount);
