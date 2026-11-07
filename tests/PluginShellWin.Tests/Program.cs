using PluginShellWin.Core.Application;
using PluginShellWin.Core.Domain;
using PluginShellWin.Infrastructure;

namespace PluginShellWin.Tests;

/// <summary>
/// 零依赖测试运行器（不引入 xunit/nunit，避免额外 NuGet 依赖与离线环境问题）。
/// 覆盖：配置校验、路径策略、用例服务、以及与真实插件运行时的集成。
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static int _skipped;

    private static async Task<int> Main()
    {
        Environment.SetEnvironmentVariable("QT_QPA_PLATFORM", "offscreen");

        // 插件运行时是 PyInstaller 单文件，需要在 TEMP 下解包。
        // 受限环境（沙箱/服务账户）默认 TEMP 不可创建子目录，这里统一指到工作区。
        var repository = FindRepositoryRoot() ?? AppContext.BaseDirectory;
        var runtimeTemp = Path.Combine(repository, ".tmp", "pyi-runtime");
        Directory.CreateDirectory(runtimeTemp);
        Environment.SetEnvironmentVariable("TEMP", runtimeTemp);
        Environment.SetEnvironmentVariable("TMP", runtimeTemp);

        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (Exception)
        {
            // 没有控制台时忽略
        }

        Console.WriteLine("PluginShellWin.Tests");

        await Run("Config: 默认值", ConfigDefaults);
        await Run("Config: 非法日志级别被拒绝", ConfigRejectsBadLevel);
        await Run("Config: 窗口尺寸校验", ConfigRejectsTinyWindow);
        await Run("Paths: 识别含插件的目录", PathsDetectPlugins);
        await Run("Paths: --plugins 覆盖优先", PathsExplicitOverride);
        await Run("Paths: 不可写目录给出明确错误", PathsInvalidOverride);
        await Run("Paths: 可写性探测", PathsWritabilityProbe);
        await Run("Service: 命令失败返回结果而非抛异常", ServiceReturnsResults);
        await Run("Service: 列表异常翻译为 PluginRuntimeException", ServiceTranslatesErrors);
        await Run("Runtime: 协议握手与插件装载（集成）", RuntimeIntegrationAsync);

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed} / 失败 {_failed} / 跳过 {_skipped}");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    private static async Task Run(string name, Func<Task> body)
    {
        try
        {
            await body();
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        catch (SkipException ex)
        {
            _skipped++;
            Console.WriteLine($"  SKIP  {name} —— {ex.Message}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FAIL  {name} —— {ex.Message}");
        }
    }

    private static Task Run(string name, Action body) => Run(name, () =>
    {
        body();
        return Task.CompletedTask;
    });

    // ------------------------------------------------------------------
    private static void ConfigDefaults()
    {
        var config = new AppConfig();
        Assert(config.AutoAdopt, "autoAdopt 默认应为 true");
        Assert(config.SingleInstance, "singleInstance 默认应为 true");
        Assert(config.WindowWidth >= 800, "默认窗口宽度应合理");
        Assert(config.LogLevel == "INFO", "默认日志级别应为 INFO");
    }

    private static void ConfigRejectsBadLevel()
    {
        using var temp = new TempDirectory();
        var dataDir = Path.Combine(temp.Path, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "config.json"), """{"logLevel":"TRACE"}""");
        var paths = new AppPaths(temp.Path, dataDir);
        Expect<ConfigurationException>(() => AppConfig.Load(paths), "非法 logLevel 应抛 ConfigurationException");
    }

    private static void ConfigRejectsTinyWindow()
    {
        using var temp = new TempDirectory();
        var dataDir = Path.Combine(temp.Path, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "config.json"), """{"windowWidth":100}""");
        var paths = new AppPaths(temp.Path, dataDir);
        Expect<ConfigurationException>(() => AppConfig.Load(paths), "过小窗口应被拒绝");
    }

    private static void PathsDetectPlugins()
    {
        using var temp = new TempDirectory();
        var plugins = Path.Combine(temp.Path, "plugins");
        var plugin = Path.Combine(plugins, "com.demo.x");
        Directory.CreateDirectory(plugin);
        Assert(!AppPaths.HasPlugins(plugins), "没有 manifest 前不应识别为插件目录");
        File.WriteAllText(Path.Combine(plugin, "manifest.json"), "{}");
        Assert(AppPaths.HasPlugins(plugins), "有 manifest 后应识别为插件目录");
    }

    private static void PathsExplicitOverride()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(Path.Combine(temp.Path, "exe"), Path.Combine(temp.Path, "data"));
        var (directory, source) = paths.ResolvePluginsDirectory(
            Path.Combine(temp.Path, "custom"), new AppConfig());
        Assert(directory == Path.GetFullPath(Path.Combine(temp.Path, "custom")), "--plugins 应优先");
        Assert(source.Contains("--plugins"), "来源应标注为参数");
    }

    private static void PathsInvalidOverride()
    {
        using var temp = new TempDirectory();
        // 用一个"文件"当父目录，保证 CreateDirectory 必然失败
        var blocker = Path.Combine(temp.Path, "blocker.bin");
        File.WriteAllText(blocker, "x");
        var paths = new AppPaths(Path.Combine(temp.Path, "exe"), Path.Combine(temp.Path, "data"));

        try
        {
            paths.ResolvePluginsDirectory(Path.Combine(blocker, "plugins"), new AppConfig());
            throw new Exception("不可写目录应抛 ConfigurationException");
        }
        catch (ConfigurationException ex)
        {
            Assert(ex.Message.Contains("不可写"), "错误信息应说明目录不可写");
            Assert(ex.Message.Contains("--plugins") || ex.Message.Contains("参数"), "应提示如何指定目录");
        }
    }

    private static void PathsWritabilityProbe()
    {
        using var temp = new TempDirectory();
        Assert(AppPaths.IsWritable(temp.Path), "临时目录应可写");
        Assert(!AppPaths.IsWritable(Path.Combine(temp.Path, "missing")), "不存在的目录应判定不可写");
    }

    private static async Task ServiceReturnsResults()
    {
        var runtime = new FakeRuntime();
        var service = new PluginService(runtime);

        var install = await service.InstallAsync("C:/plugins/demo");
        Assert(install.Ok, "正常安装应成功");

        var enable = await service.EnableAsync("com.demo.x");
        Assert(enable.Ok, "启用应成功");

        runtime.FailNext = true;
        var disable = await service.DisableAsync("com.demo.x");
        Assert(!disable.Ok, "运行时异常应翻译为失败结果");
        Assert(disable.Message.Contains("禁用插件失败"), "失败信息应包含操作名");

        var uninstall = await service.UninstallAsync("com.demo.x");
        Assert(uninstall.Ok, "异常后仍可继续调用");
    }

    private static async Task ServiceTranslatesErrors()
    {
        var runtime = new FakeRuntime { ThrowOnList = true };
        var service = new PluginService(runtime);
        try
        {
            await service.ListPluginsAsync();
            throw new Exception("预期抛出 PluginRuntimeException");
        }
        catch (PluginRuntimeException ex)
        {
            Assert(ex.Message.Contains("读取插件列表失败"), "应包装为读取失败");
        }
    }

    private static async Task RuntimeIntegrationAsync()
    {
        var repository = FindRepositoryRoot();
        if (repository is null)
        {
            throw new SkipException("找不到仓库根（runtime/runtime_main.py）");
        }

        var launch = RuntimeLocator.Locate();
        using var temp = new TempDirectory();
        var pluginsDir = Path.Combine(temp.Path, "plugins");

        await using var runtime = RuntimeProcess.Start(launch, pluginsDir);
        var handshake = await runtime.HandshakeAsync();
        Assert(handshake.Protocol == AppInfo.Protocol, $"协议应为 {AppInfo.Protocol}");
        Assert(handshake.Qt.StartsWith("6."), "Qt 版本应可用");
        Assert(handshake.Python.Length > 0, "应返回 Python 版本");

        var plugins = await runtime.GetPluginsAsync();
        Assert(plugins.Count == 0, "空目录应无插件");

        var samples = Path.Combine(repository, "..", "PluginShell", "sample_plugins");
        samples = Path.GetFullPath(samples);
        if (!Directory.Exists(samples))
        {
            Console.WriteLine("        （缺少同级 PluginShell 示例插件，跳过装载断言）");
            return;
        }

        foreach (var pluginId in new[]
                 {
                     "com.example.content_a",
                     "com.example.content_b",
                     "com.example.lab_coordinate",
                 })
        {
            var result = await runtime.InstallAsync(Path.Combine(samples, pluginId));
            Assert(result.Ok, $"安装 {pluginId} 失败：{result.Message}");
        }

        plugins = await runtime.GetPluginsAsync();
        Assert(plugins.Count == 3, "应安装 3 个插件");
        Assert(plugins.All(item => item.State == PluginState.Enabled), "3 个插件都应处于启用状态");

        var (categories, total) = await runtime.GetCategoriesAsync();
        Assert(total == 3, "应有 3 个内容贡献");
        var ids = categories.Select(item => item.Id).ToArray();
        Assert(ids.SequenceEqual(new[] { "lesson", "tool", "lab" }), "分类应来自插件清单");

        // 先卸载依赖方：依赖被依赖方时应被运行时拒绝（这条也顺带验证依赖保护）
        var blocked = await runtime.UninstallAsync("com.example.content_a");
        Assert(!blocked.Ok, "被依赖的插件应拒绝卸载");
        Assert(blocked.Message.Contains("依赖"), "拒绝原因应说明依赖关系");

        foreach (var pluginId in new[]
                 {
                     "com.example.content_b",
                     "com.example.lab_coordinate",
                     "com.example.content_a",
                 })
        {
            var result = await runtime.UninstallAsync(pluginId);
            Assert(result.Ok, $"卸载 {pluginId} 失败：{result.Message}");
        }
        Assert((await runtime.GetPluginsAsync()).Count == 0, "卸载后应为空");
    }

    // ------------------------------------------------------------------
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 10 && directory is not null; depth++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "runtime", "runtime_main.py")))
            {
                return directory.FullName;
            }
        }
        return null;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception(message);
    }

    private sealed class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            // 注意：受限环境（沙箱/服务账户）下系统 TEMP 可能不可写，
            // 因此测试统一使用仓库内的 .tmp 目录，保证可重复。
            var root = System.IO.Path.Combine(
                FindRepositoryRoot() ?? AppContext.BaseDirectory, ".tmp");
            Directory.CreateDirectory(root);
            Path = System.IO.Path.Combine(root, "psw-tests-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception)
            {
                // 忽略清理失败
            }
        }
    }

    /// <summary>用于应用层测试的假运行时，不启动任何进程。</summary>
    private sealed class FakeRuntime : IPluginRuntime
    {
        private readonly List<PluginInfo> _plugins = new();

        public bool ThrowOnList { get; set; }

        public bool FailNext { get; set; }

        public string PluginsDirectory => "C:/plugins";

        public Task<RuntimeHandshake> HandshakeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RuntimeHandshake(
                AppInfo.Protocol, "fake", "6.12.0", "3.13.14", 1, PluginsDirectory));

        public Task<IReadOnlyList<PluginInfo>> GetPluginsAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnList)
            {
                throw new InvalidOperationException("runtime down");
            }
            return Task.FromResult<IReadOnlyList<PluginInfo>>(_plugins.ToList());
        }

        public Task<(IReadOnlyList<CategoryInfo> Categories, int Total)> GetCategoriesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<(IReadOnlyList<CategoryInfo>, int)>(
                (Array.Empty<CategoryInfo>(), _plugins.Count));

        public Task<OperationResult> InstallAsync(string path, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                throw new InvalidOperationException("boom");
            }
            _plugins.Add(new PluginInfo(
                "com.demo.x", "Demo", "1.0.0", PluginState.Enabled, "demo", "演示",
                string.Empty, string.Empty,
                new Dictionary<string, string>(), Array.Empty<string>()));
            return Task.FromResult(OperationResult.Success("已安装"));
        }

        public Task<OperationResult> EnableAsync(string pluginId, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult.Success("已启用"));

        public Task<OperationResult> DisableAsync(string pluginId, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("boom");
            }
            return Task.FromResult(OperationResult.Success("已禁用"));
        }

        public Task<OperationResult> UninstallAsync(string pluginId, CancellationToken cancellationToken = default)
        {
            _plugins.RemoveAll(item => item.Id == pluginId);
            return Task.FromResult(OperationResult.Success("已卸载"));
        }

        public Task<IReadOnlyList<string>> GetLogsAsync(int limit = 120, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<RuntimeWindow> ShowContentAsync(
            string? categoryId, int width, int height, CancellationToken cancellationToken = default)
            => Task.FromResult(new RuntimeWindow(1, 0));

        public Task ResizeContentAsync(int width, int height, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
