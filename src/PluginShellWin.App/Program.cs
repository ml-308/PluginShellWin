using PluginShellWin.App.Presentation;
using PluginShellWin.Core.Application;
using PluginShellWin.Core.Domain;
using PluginShellWin.Infrastructure;

namespace PluginShellWin.App;

/// <summary>
/// 组合根：解析参数 → 路径/配置/日志 → 单实例 → 启动插件运行时 → 自检或开窗。
/// 依赖方向严格单向：App → Application/Core → Domain。
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 「发现新版本」检查：命令行开关 + 无人值守 probe
        UpdateKit.UpdateCheck.Configure(args);
        var probeExit = UpdateKit.UpdateCheck.TryProbe(args);
        if (probeExit.HasValue) return probeExit.Value;

        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ShowHelpException)
        {
            SafeConsole.WriteLine(CommandLineOptions.Usage);
            return 0;
        }

        if (options.ShowVersion)
        {
            SafeConsole.WriteLine($"{AppInfo.Name} {AppInfo.Version} (protocol {AppInfo.Protocol})");
            return 0;
        }

        var paths = AppPaths.Discover();
        paths.EnsureDirectories();

        AppConfig config;
        try
        {
            config = AppConfig.Load(paths);
        }
        catch (ConfigurationException ex)
        {
            SafeConsole.WriteLine("[配置错误] " + ex.Message);
            ShowError("配置错误", ex.Message);
            return 2;
        }

        using var logger = new AppLogger(
            paths.LogsDirectory,
            options.Verbose ? "DEBUG" : config.LogLevel,
            echoToConsole: options.Check);

        try
        {
            return Run(options, config, paths, logger);
        }
        catch (Exception ex)
        {
            logger.Error("启动失败", ex);
            SafeConsole.WriteLine(ex.ToString());
            ShowError("启动失败", ex.Message);
            return 1;
        }
    }

    private static int Run(
        CommandLineOptions options, AppConfig config, AppPaths paths, AppLogger logger)
    {
        using var guard = config.SingleInstance
            ? new SingleInstance(AppInfo.Name + ".instance")
            : null;
        if (guard is not null && !guard.TryAcquire())
        {
            logger.Info("已有实例在运行，本次退出");
            if (!options.Check)
            {
                MessageBox.Show(
                    $"{AppInfo.Name} 已经在运行。", AppInfo.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                SafeConsole.WriteLine("已有实例在运行");
            }
            return 0;
        }

        var (pluginsDirectory, pluginsSource) =
            paths.ResolvePluginsDirectory(options.PluginsDir, config);
        var launch = RuntimeLocator.Locate();
        logger.Info($"插件运行时：{launch.Description} ({launch.FileName})");
        logger.Info($"插件目录：{pluginsDirectory}（{pluginsSource}）");

        using var runtime = RuntimeProcess.Start(launch, pluginsDirectory, logger);
        var service = new PluginService(runtime);
        var handshake = runtime.HandshakeAsync().GetAwaiter().GetResult();
        logger.Info(
            $"握手成功 protocol={handshake.Protocol} qt={handshake.Qt} " +
            $"python={handshake.Python} pid={handshake.Pid}");

        foreach (var folder in options.Installs)
        {
            var result = service.InstallAsync(folder).GetAwaiter().GetResult();
            logger.Info($"安装 {folder} -> ok={result.Ok} {result.Message}");
            if (!result.Ok)
            {
                SafeConsole.WriteLine($"安装失败 {folder}: {result.Message}");
            }
        }

        if (options.Check)
        {
            return CheckRunner.Run(
                service, handshake, options, paths, pluginsSource, launch, logger);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var mainForm = new MainForm(service, pluginsSource, config, logger, launch.Description);
        UpdateKit.UpdatePrompt.ScheduleAutoCheck(mainForm);
        Application.Run(mainForm);
        return 0;
    }

    private static void ShowError(string title, string message)
    {
        try
        {
            MessageBox.Show(
                message, $"{AppInfo.Name} {title}", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception)
        {
            // 没有交互式桌面时忽略
        }
    }
}
