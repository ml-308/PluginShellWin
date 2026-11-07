using PluginShellWin.Core.Domain;

namespace PluginShellWin.Infrastructure;

/// <summary>
/// 应用路径与插件目录解析策略。
///
/// 关键约定：**安装目录可能是只读的**（例如用户以管理员身份装到
/// C:\Program Files，之后用普通账户运行）。因此任何"在安装目录下建文件夹"
/// 的动作都必须先探测可写性，失败要自动回退到用户数据目录，绝不能直接抛异常。
/// </summary>
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
            candidates.Add(Path.Combine(LocalAppData(), AppInfo.Name));
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
    /// 插件目录优先级：--plugins &gt; 环境变量 &gt; 配置 &gt; exe 同级(可写) &gt;
    /// py 版目录 &gt; 数据目录 &gt; 临时目录。全部失败才抛 ConfigurationException。
    /// </summary>
    public (string Directory, string Source) ResolvePluginsDirectory(
        string? cliOverride, AppConfig config)
    {
        foreach (var (value, source) in new[]
                 {
                     (cliOverride, "--plugins 参数"),
                     (Environment.GetEnvironmentVariable("PLUGINSHELL_PLUGINS"),
                         "环境变量 PLUGINSHELL_PLUGINS"),
                     (config.PluginsDir, "配置文件 pluginsDir"),
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            var directory = Path.GetFullPath(value!);
            if (TryEnsureWritable(directory))
            {
                return (directory, source);
            }
            throw new ConfigurationException(
                $"插件目录不可写：{directory}{Environment.NewLine}" +
                "请换一个可写目录，或修改启动参数 / 配置文件。");
        }

        var side = Path.Combine(BaseDirectory, "plugins");
        if (HasPlugins(side))
        {
            // 已装插件在只读安装目录里：仍然要展示它们，但明确告知不能再装新插件
            return (side, IsWritable(side)
                ? "exe 同级 plugins/"
                : "exe 同级 plugins/（安装目录只读：可读取，无法安装新插件）");
        }
        if (TryEnsureWritable(side))
        {
            return (side, "exe 同级 plugins/");
        }

        var pyShell = Path.Combine(LocalAppData(), "PluginShell", "plugins");
        if (HasPlugins(pyShell))
        {
            return (pyShell, "复用 PluginShell(py 版)插件目录");
        }

        foreach (var (candidate, source) in new[]
                 {
                     (Path.Combine(DataDirectory, "plugins"), "数据目录 plugins/（安装目录只读，已回退）"),
                     (Path.Combine(Path.GetTempPath(), AppInfo.Name, "plugins"),
                         "临时目录 plugins/（只读环境回退）"),
                 })
        {
            if (TryEnsureWritable(candidate))
            {
                return (candidate, source);
            }
        }

        throw new ConfigurationException(
            "无法创建任何可写的插件目录。" + Environment.NewLine +
            "请检查磁盘空间、目录权限或安全软件限制，然后用 --plugins 指定一个可写目录。");
    }

    public static bool HasPlugins(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                   && Directory.EnumerateDirectories(directory)
                       .Any(child => File.Exists(Path.Combine(child, "manifest.json")));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsWritable(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }
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

    private static string LocalAppData()
        => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}
