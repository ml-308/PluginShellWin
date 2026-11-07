namespace PluginShellWin.App;

/// <summary>命令行参数（窗口形态与控制台自检形态共用）。</summary>
public sealed record CommandLineOptions(
    bool Check,
    string? PluginsDir,
    IReadOnlyList<string> Installs,
    string? Report,
    bool NoEmbedCheck,
    bool RequireEmbed,
    bool Verbose,
    bool ShowVersion)
{
    public static CommandLineOptions Parse(string[] args)
    {
        var check = false;
        var noEmbed = false;
        var requireEmbed = false;
        var verbose = false;
        var version = false;
        string? pluginsDir = null;
        string? report = null;
        var installs = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            switch (current)
            {
                case "--check":
                    check = true;
                    break;
                case "--no-embed-check":
                    noEmbed = true;
                    break;
                case "--require-embed":
                    requireEmbed = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--version":
                case "-v":
                    version = true;
                    break;
                case "--plugins" when index + 1 < args.Length:
                    pluginsDir = args[++index];
                    break;
                case "--install" when index + 1 < args.Length:
                    installs.Add(args[++index]);
                    break;
                case "--report" when index + 1 < args.Length:
                    report = args[++index];
                    break;
                case "--help":
                case "-h":
                    throw new ShowHelpException();
            }
        }

        return new CommandLineOptions(
            check, pluginsDir, installs, report, noEmbed, requireEmbed, verbose, version);
    }

    public const string Usage = """
        PluginShellWin —— C# 原生插件宿主（兼容 PluginShell 插件协议 1.0）

        用法:
          PluginShellWin.exe                                启动原生窗口
          PluginShellWin.exe --plugins <目录>               覆盖插件目录
          PluginShellWin.exe --install <插件目录>            启动前安装插件（可重复）
          PluginShellWin.exe --check --report <文件>        无界面自检并写 JSON 报告
          PluginShellWin.exe --version                      打印版本

        选项:
          --check             自检模式（会尝试嵌入插件窗口并校验 SetParent）
          --no-embed-check    自检时跳过窗口嵌入校验
          --require-embed     嵌入校验失败即返回非零退出码
          --report <文件>      报告输出路径（默认写到数据目录 last-check.json）
          --verbose           输出 DEBUG 日志
        """;
}

public sealed class ShowHelpException : Exception
{
}
