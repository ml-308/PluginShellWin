using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PluginShellWin.App.Presentation;
using PluginShellWin.Core.Application;
using PluginShellWin.Core.Domain;
using PluginShellWin.Infrastructure;

namespace PluginShellWin.App;

/// <summary>
/// 无界面自检：握手 → 插件/分类快照 → 真实窗口嵌入校验 → 写 JSON 报告。
/// 这是"完成测试"在 C# 侧的可自动化证据。
/// </summary>
internal static class CheckRunner
{
    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal sealed record EmbedResult(bool Ok, long Hwnd, int Panels, string Error, bool Skipped);

    public static int Run(
        PluginService service,
        RuntimeHandshake handshake,
        CommandLineOptions options,
        AppPaths paths,
        string pluginsSource,
        RuntimeLaunch launch,
        AppLogger logger)
    {
        var snapshot = service.GetSnapshotAsync().GetAwaiter().GetResult();
        var logs = service.GetLogsAsync(60).GetAwaiter().GetResult();
        var embed = options.NoEmbedCheck
            ? new EmbedResult(false, 0, 0, string.Empty, Skipped: true)
            : EmbedCheck(service);

        var report = new
        {
            app = AppInfo.Name,
            version = AppInfo.Version,
            protocol = AppInfo.Protocol,
            host = ".NET " + Environment.Version + " / WinForms",
            dotnet = Environment.Version.ToString(),
            runtime = new
            {
                description = launch.Description,
                handshake.Protocol,
                handshake.Qt,
                handshake.Python,
                handshake.Pid,
                handshake.PluginsDir,
            },
            pluginsDir = service.PluginsDirectory,
            pluginsSource,
            installed = snapshot.Plugins.Count,
            enabled = snapshot.EnabledCount,
            contributions = snapshot.ContributionCount,
            categories = snapshot.Categories.Select(item => new
            {
                item.Id,
                item.Label,
                item.Order,
                item.Count,
            }),
            plugins = snapshot.Plugins.Select(item => new
            {
                item.Id,
                item.Name,
                item.Version,
                state = item.State.ToString().ToLowerInvariant(),
                item.CategoryId,
                item.CategoryLabel,
                item.Message,
                dependencies = item.Dependencies,
            }),
            embed = new
            {
                skipped = embed.Skipped,
                ok = embed.Ok,
                embed.Hwnd,
                panels = embed.Panels,
                error = embed.Error,
            },
            failed = snapshot.Plugins
                .Where(item => item.State == PluginState.Failed)
                .Select(item => item.Id)
                .ToArray(),
            logs,
        };

        var destination = string.IsNullOrWhiteSpace(options.Report)
            ? Path.Combine(paths.DataDirectory, "last-check.json")
            : Path.GetFullPath(options.Report!);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(
                destination,
                JsonSerializer.Serialize(report, ReportOptions),
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            SafeConsole.WriteLine("报告写入失败: " + ex.Message);
            return 2;
        }

        SafeConsole.WriteLine($"{AppInfo.Name} {AppInfo.Version}  protocol={AppInfo.Protocol}");
        SafeConsole.WriteLine($"宿主: {report.host}   运行时: {handshake.Qt} / Python {handshake.Python}");
        SafeConsole.WriteLine($"插件目录: {service.PluginsDirectory}  ({pluginsSource})");
        SafeConsole.WriteLine(
            $"已安装 {snapshot.Plugins.Count} / 已启用 {snapshot.EnabledCount} / " +
            $"内容贡献 {snapshot.ContributionCount}");
        SafeConsole.WriteLine("分类: " + string.Join(", ", snapshot.Categories.Select(item => item.Id)));
        SafeConsole.WriteLine(
            embed.Skipped
                ? "窗口嵌入: 跳过"
                : $"窗口嵌入: ok={embed.Ok} hwnd=0x{embed.Hwnd:X} 面板={embed.Panels} {embed.Error}");
        SafeConsole.WriteLine("报告: " + destination);

        logger.Info(
            $"自检完成 installed={snapshot.Plugins.Count} enabled={snapshot.EnabledCount} " +
            $"contributions={snapshot.ContributionCount} embed={embed.Ok}");

        var failed = snapshot.Plugins.Count(item => item.State == PluginState.Failed);
        if (failed > 0)
        {
            SafeConsole.WriteLine("失败插件: " + string.Join(", ",
                snapshot.Plugins.Where(item => item.State == PluginState.Failed).Select(item => item.Id)));
            return 3;
        }
        if (!embed.Skipped && !embed.Ok && options.RequireEmbed)
        {
            SafeConsole.WriteLine("窗口嵌入校验失败（--require-embed）");
            return 4;
        }
        return 0;
    }

    private static EmbedResult EmbedCheck(PluginService service)
    {
        try
        {
            using var form = new Form
            {
                Text = "embed-check",
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                Size = new Size(900, 600),
                Opacity = 0,
            };
            var panel = new Panel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();

            var window = service.ShowContentAsync(
                    null,
                    Math.Max(640, panel.ClientSize.Width),
                    Math.Max(420, panel.ClientSize.Height))
                .GetAwaiter().GetResult();

            using var host = new RuntimePanelHost(panel);
            var attached = host.Attach(window.Hwnd);
            Application.DoEvents();
            var parent = NativeMethods.GetParent(new IntPtr(window.Hwnd));
            var parentMatches = parent == panel.Handle;
            form.Close();

            var error = attached
                ? (parentMatches ? string.Empty : "GetParent 校验失败")
                : "SetParent 未生效";
            return new EmbedResult(attached && parentMatches, window.Hwnd, window.PanelCount, error, false);
        }
        catch (Exception ex)
        {
            return new EmbedResult(false, 0, 0, ex.Message, false);
        }
    }
}
