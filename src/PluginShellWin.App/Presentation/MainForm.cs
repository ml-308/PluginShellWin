using System.Diagnostics;
using PluginShellWin.Core.Application;
using PluginShellWin.Core.Domain;
using PluginShellWin.Infrastructure;

namespace PluginShellWin.App.Presentation;

/// <summary>
/// 主窗口：左侧分类导航 + 右侧插件内容（由运行时窗口嵌入），空状态与插件管理入口。
/// 只调用 PluginService，不接触运行时进程与协议细节。
/// </summary>
public sealed class MainForm : Form
{
    private readonly PluginService _service;
    private readonly string _pluginsSource;
    private readonly AppLogger _logger;
    private readonly string _runtimeDescription;

    private readonly ListBox _nav = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private readonly Panel _emptyState = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly RuntimePanelHost _host;

    private string? _activeCategoryId;
    private bool _suppressNavigation;

    public MainForm(
        PluginService service,
        string pluginsSource,
        AppConfig config,
        AppLogger logger,
        string runtimeDescription)
    {
        _service = service;
        _pluginsSource = pluginsSource;
        _logger = logger;
        _runtimeDescription = runtimeDescription;

        Text = $"{AppInfo.Name} · C# 原生插件宿主";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(config.WindowWidth, config.WindowHeight);
        MinimumSize = new Size(720, 480);

        _host = new RuntimePanelHost(_content);

        var toolbar = BuildToolbar();
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 220,
            Panel1MinSize = 160,
        };
        split.Panel1.Controls.Add(_nav);
        split.Panel2.Controls.Add(_content);
        split.Panel2.Controls.Add(_emptyState);

        var status = new StatusStrip();
        status.Items.Add(_statusLabel);

        Controls.Add(split);
        Controls.Add(toolbar);
        Controls.Add(status);

        _nav.SelectedIndexChanged += OnCategoryChanged;
        Shown += async (_, _) => await RunSafeAsync(RefreshAsync);
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

        var manage = new ToolStripButton("插件管理…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        manage.Click += async (_, _) => await RunSafeAsync(async () =>
        {
            using var dialog = new PluginManagerForm(_service, _logger);
            dialog.ShowDialog(this);
            await RefreshAsync();
        });

        var refresh = new ToolStripButton("刷新") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        refresh.Click += async (_, _) => await RunSafeAsync(RefreshAsync);

        var openFolder = new ToolStripButton("打开插件目录") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        openFolder.Click += (_, _) => RunSafe(() =>
        {
            Directory.CreateDirectory(_service.PluginsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", _service.PluginsDirectory)
            {
                UseShellExecute = true,
            });
        });

        var about = new ToolStripButton("关于") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        about.Click += (_, _) =>
        {
            using var dialog = new AboutForm(
                _service.PluginsDirectory, _pluginsSource, _runtimeDescription);
            dialog.ShowDialog(this);
        };

        toolbar.Items.AddRange(new ToolStripItem[] { manage, refresh, openFolder, about });
        return toolbar;
    }

    // ------------------------------------------------------------------
    private async Task RefreshAsync()
    {
        var snapshot = await _service.GetSnapshotAsync();
        RebuildNavigation(snapshot);

        var hasContent = snapshot.Plugins.Count > 0 && snapshot.ContributionCount > 0;
        _content.Visible = hasContent;
        _emptyState.Visible = !hasContent;

        if (hasContent)
        {
            await ShowCategoryAsync(_activeCategoryId);
        }
        else
        {
            BuildEmptyState();
        }

        _statusLabel.Text =
            $"已安装 {snapshot.Plugins.Count} · 已启用 {snapshot.EnabledCount} · " +
            $"分类 {snapshot.Categories.Count} · 内容贡献 {snapshot.ContributionCount} · " +
            $"插件目录 {_service.PluginsDirectory}";
    }

    private void RebuildNavigation(PluginSnapshot snapshot)
    {
        _suppressNavigation = true;
        try
        {
            _nav.BeginUpdate();
            _nav.Items.Clear();
            _nav.Items.Add(new NavItem(null, $"全部  ({snapshot.ContributionCount})"));
            foreach (var category in snapshot.Categories)
            {
                _nav.Items.Add(new NavItem(category.Id, $"{category.Label}  ({category.Count})"));
            }

            var target = 0;
            if (_activeCategoryId is not null)
            {
                for (var index = 0; index < _nav.Items.Count; index++)
                {
                    if (_nav.Items[index] is NavItem item && item.Id == _activeCategoryId)
                    {
                        target = index;
                        break;
                    }
                }
                if (target == 0)
                {
                    _activeCategoryId = null;
                }
            }
            _nav.SelectedIndex = target;
        }
        finally
        {
            _nav.EndUpdate();
            _suppressNavigation = false;
        }
    }

    private async void OnCategoryChanged(object? sender, EventArgs e)
    {
        if (_suppressNavigation || _nav.SelectedItem is not NavItem item)
        {
            return;
        }
        _activeCategoryId = item.Id;
        if (!_content.Visible)
        {
            return;
        }
        await RunSafeAsync(() => ShowCategoryAsync(_activeCategoryId));
    }

    private async Task ShowCategoryAsync(string? categoryId)
    {
        var size = _content.ClientSize;
        var window = await _service.ShowContentAsync(
            categoryId,
            Math.Max(480, size.Width),
            Math.Max(360, size.Height));

        var attached = _host.Attach(window.Hwnd);
        _logger.Debug(
            $"嵌入插件窗口 hwnd=0x{window.Hwnd:X} 面板={window.PanelCount} attached={attached}");
        if (!attached)
        {
            MessageBox.Show(
                this,
                "插件窗口嵌入失败，请查看日志后重试。",
                AppInfo.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void BuildEmptyState()
    {
        if (_emptyState.Controls.Count > 0)
        {
            return;
        }
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var title = new Label
        {
            Text = "请安装插件",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = new Font(Font.FontFamily, 20F, FontStyle.Bold),
        };
        var hint = new Label
        {
            Text = "当前宿主没有任何业务内容；所有内容都必须由插件提供。",
            AutoSize = true,
            Anchor = AnchorStyles.None,
        };
        var directory = new Label
        {
            Text = _service.PluginsDirectory,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            ForeColor = SystemColors.GrayText,
        };
        var button = new Button
        {
            Text = "打开插件管理…",
            AutoSize = true,
            Anchor = AnchorStyles.None,
        };
        button.Click += async (_, _) => await RunSafeAsync(async () =>
        {
            using var dialog = new PluginManagerForm(_service, _logger);
            dialog.ShowDialog(this);
            await RefreshAsync();
        });

        layout.Controls.Add(title, 0, 1);
        layout.Controls.Add(hint, 0, 2);
        layout.Controls.Add(directory, 0, 3);
        layout.Controls.Add(button, 0, 4);
        _emptyState.Controls.Add(layout);
    }

    // ------------------------------------------------------------------
    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (PluginShellException ex)
        {
            _logger.Error("操作失败", ex);
            MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _logger.Error("未预期异常", ex);
            MessageBox.Show(
                this, "发生未预期错误：" + ex.Message, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RunSafe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.Error("操作失败", ex);
            MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _host.Dispose();
        base.OnFormClosing(e);
    }

    private sealed record NavItem(string? Id, string Text)
    {
        public override string ToString() => Text;
    }
}
