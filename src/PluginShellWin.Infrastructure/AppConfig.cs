using System.Text.Json;
using PluginShellWin.Core.Domain;

namespace PluginShellWin.Infrastructure;

/// <summary>
/// 应用配置：内置 appsettings.json &lt; 用户 config.json &lt; 环境变量。
/// 按字段合并（不是整对象覆盖），所以用户只改一个字段不会把其它默认值冲掉。
/// </summary>
public sealed record AppConfig
{
    public string? PluginsDir { get; init; }

    public bool AutoAdopt { get; init; } = true;

    public bool SingleInstance { get; init; } = true;

    public string LogLevel { get; init; } = "INFO";

    public int WindowWidth { get; init; } = 1100;

    public int WindowHeight { get; init; } = 720;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppConfig Load(AppPaths paths)
    {
        var config = Apply(new AppConfig(), paths.BundledConfigFile);
        config = Apply(config, paths.UserConfigFile);

        var envLevel = Environment.GetEnvironmentVariable("PLUGINSHELLWIN_LOG_LEVEL");
        if (!string.IsNullOrWhiteSpace(envLevel))
        {
            config = config with { LogLevel = envLevel!.Trim().ToUpperInvariant() };
        }

        if (config.WindowWidth < 320 || config.WindowHeight < 240)
        {
            throw new ConfigurationException("窗口尺寸配置过小（width ≥ 320，height ≥ 240）");
        }
        if (config.LogLevel is not ("DEBUG" or "INFO" or "WARNING" or "ERROR"))
        {
            throw new ConfigurationException($"logLevel 取值非法：{config.LogLevel}");
        }
        return config;
    }

    /// <summary>把文件里"实际出现过"的字段覆盖到当前配置上。</summary>
    private static AppConfig Apply(AppConfig current, string path)
    {
        if (!File.Exists(path))
        {
            return current;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"配置文件不是合法 JSON：{path}（{ex.Message}）");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ConfigurationException($"配置文件根节点必须是对象：{path}");
            }

            T Pick<T>(string name, T fallback)
            {
                if (!root.TryGetProperty(name, out var element)
                    || element.ValueKind == JsonValueKind.Null)
                {
                    return fallback;
                }
                try
                {
                    return element.Deserialize<T>(Options) ?? fallback;
                }
                catch (JsonException ex)
                {
                    throw new ConfigurationException($"配置字段 {name} 类型不正确：{path}（{ex.Message}）");
                }
            }

            return current with
            {
                PluginsDir = Pick("pluginsDir", current.PluginsDir),
                AutoAdopt = Pick("autoAdopt", current.AutoAdopt),
                SingleInstance = Pick("singleInstance", current.SingleInstance),
                LogLevel = Pick("logLevel", current.LogLevel),
                WindowWidth = Pick("windowWidth", current.WindowWidth),
                WindowHeight = Pick("windowHeight", current.WindowHeight),
            };
        }
    }
}
