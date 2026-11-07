using PluginShellWin.Core.Domain;

namespace PluginShellWin.Infrastructure;

/// <summary>应用路径与插件目录解析策略（宿主策略，不属于插件运行时）。</summary>
public sealed class AppPaths
{
    public AppPaths(string baseDirectory, string dataDirectory)
    {
        BaseDirectory = baseDirectory;
        DataDirectory = dataDirectory;
    }

    public string BaseDirectory { get; }

    public string DataDirectory { get; }

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public string UserConfigFile => Path.Combine(DataDirectory, "config.json");

    public string BundledConfigFile => Path.Combine(BaseDirectory, "config", "appsettings.json");

    public static AppPaths Discover()
    {
        var baseDirectory = AppContext.BaseDirectory;

        var overrideData = Environment.GetEnvironmentVariable("PLUGINSHELLWIN_DATA_DIR");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(overrideData))
        {
            candidates.Add(Path.GetFullPath(overrideData));
        }
        else
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, AppInfo.Name));
        }
        candidates.Add(Path.Combine(baseDirectory, "data"));
        candidates.Add(Path.Combine(Path.GetTempPath(), AppInfo.Name));

        var dataDirectory = candidates[0];
        foreach (var candidate in candidates)
        {
            if (TryEnsureWritable(candidate))
            {
                dataDirectory = candidate;
                break;
            }
        }
        return new AppPaths(baseDirectory, dataDirectory);
    }

    public void EnsureDirectories()
    {
        foreach (var directory in new[] { DataDirectory, LogsDirectory })
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception)
            {
                // 只读位置：日志/报告各自兜底，绝不因为目录问题让应用起不来。
            }
        }
    }

    /// <summary>
    /// 插件目录优先级：--plugins &gt; 环境变量 &gt; exe 同级(有插件) &gt;
    /// py 版目录(有插件) &gt; exe 同级(默认)。
    /// </summary>
    public (string Directory, string Source) ResolvePluginsDirectory(
        string? cliOverride, AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(cliOverride))
        {
            return (Path.GetFullPath(cliOverride!), "--plugins 参数");
        }

        var env = Environment.GetEnvironmentVariable("PLUGINSHELL_PLUGINS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return (Path.GetFullPath(env!), "环境变量 PLUGINSHELL_PLUGINS");
        }

        if (!string.IsNullOrWhiteSpace(config.PluginsDir))
        {
            return (Path.GetFullPath(config.PluginsDir!), "配置文件 pluginsDir");
        }

        var side = Path.Combine(BaseDirectory, "plugins");
        if (HasPlugins(side))
        {
            return (side, "exe 同级 plugins/");
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pyShell = Path.Combine(local, "PluginShell", "plugins");
        if (HasPlugins(pyShell))
        {
            return (pyShell, "复用 PluginShell(py 版)插件目录");
        }

        try
        {
            Directory.CreateDirectory(side);
            return (side, "exe 同级 plugins/（默认）");
        }
        catch (Exception)
        {
            var fallback = Path.Combine(DataDirectory, "plugins");
            Directory.CreateDirectory(fallback);
            return (fallback, "数据目录 plugins/");
        }
    }

    public static bool HasPlugins(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }
            return Directory.EnumerateDirectories(directory)
                .Any(child => File.Exists(Path.Combine(child, "manifest.json")));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryEnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
