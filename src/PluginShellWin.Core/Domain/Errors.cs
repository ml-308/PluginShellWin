namespace PluginShellWin.Core.Domain;

/// <summary>应用层错误基类：UI 只需捕获这一个类型。</summary>
public class PluginShellException : Exception
{
    public PluginShellException(string message) : base(message) { }

    public PluginShellException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>与插件运行时通信失败（进程未启动、超时、协议错误）。</summary>
public sealed class PluginRuntimeException : PluginShellException
{
    public PluginRuntimeException(string message) : base(message) { }

    public PluginRuntimeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>配置文件缺失或非法。</summary>
public sealed class ConfigurationException : PluginShellException
{
    public ConfigurationException(string message) : base(message) { }
}
