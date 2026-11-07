using PluginShellWin.Core.Domain;

namespace PluginShellWin.Infrastructure;

/// <summary>运行时可执行体（或源码入口）的位置与启动参数。</summary>
public sealed record RuntimeLaunch(
    string FileName,
    IReadOnlyList<string> Arguments,
    string Description);

/// <summary>
/// 定位插件运行时：已发布形态优先用随应用分发的 PluginRuntime.exe，
/// 开发形态回退到 python runtime_main.py，便于在本机直接跑测试。
/// </summary>
public static class RuntimeLocator
{
    public static RuntimeLaunch Locate()
    {
        var explicitRuntime = Environment.GetEnvironmentVariable("PLUGINSHELLWIN_RUNTIME");
        if (!string.IsNullOrWhiteSpace(explicitRuntime) && File.Exists(explicitRuntime))
        {
            return new RuntimeLaunch(explicitRuntime!, Array.Empty<string>(), "PLUGINSHELLWIN_RUNTIME");
        }

        var baseDirectory = AppContext.BaseDirectory;
        foreach (var candidate in new[]
                 {
                     Path.Combine(baseDirectory, "runtime", "PluginRuntime.exe"),
                     Path.Combine(baseDirectory, "PluginRuntime.exe"),
                 })
        {
            if (File.Exists(candidate))
            {
                return new RuntimeLaunch(candidate, Array.Empty<string>(), "随应用分发");
            }
        }

        var directory = new DirectoryInfo(baseDirectory);
        for (var depth = 0; depth < 10 && directory is not null; depth++, directory = directory.Parent)
        {
            var exe = Path.Combine(directory.FullName, "runtime", "dist", "PluginRuntime.exe");
            if (File.Exists(exe))
            {
                return new RuntimeLaunch(exe, Array.Empty<string>(), "开发构建（runtime/dist）");
            }

            var script = Path.Combine(directory.FullName, "runtime", "runtime_main.py");
            if (File.Exists(script))
            {
                var python = Environment.GetEnvironmentVariable("PLUGINSHELLWIN_PYTHON") ?? "python";
                return new RuntimeLaunch(
                    python,
                    new[] { script },
                    "源码模式（python runtime/runtime_main.py）");
            }
        }

        throw new ConfigurationException(
            "找不到插件运行时：既没有 runtime/PluginRuntime.exe，也没有 runtime/runtime_main.py");
    }
}
