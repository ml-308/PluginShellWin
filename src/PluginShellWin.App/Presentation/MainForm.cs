using System.Diagnostics;
using PluginShellWin.Core.Application;
using PluginShellWin.Core.Domain;
using PluginShellWin.Infrastructure;

namespace PluginShellWin.App.Presentation;

/// <summary>
/// 主窗口（WorkBuddy 风格）：
///   顶部工具栏 · 左侧任务列表风格侧边栏 · 中间插件内容区 · 右侧结果面板(概览/产物)
/// 只调用 PluginService，不接触运行时进程与协议细节。
/// </summary>
public sealed class MainForm : Form
{
    private readonly PluginService _service;
    private readonly string _pluginsSource;
    private readonly AppLogger _logger;
    private readonly string _runtimeDescription;

    private readonly ListBox _nav = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Panel _emptyState = new() { Dock = DockStyle.Fill };
    private readonly TabControl _resultTabs = new() { Dock = DockStyle.Fill };
    private readonly ListView _overviewList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly ListView _artifactList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
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

        Text = $"{AppInfo.Name} · 插件工作台";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(config.WindowWidth, config.WindowHeight);
        MinimumSize = new Size(900, 560);
        Font = WorkBuddyTheme.UiFont;
        BackColor = WorkBuddyTheme.ContentBackground;

        _host = new RuntimePanelHost(_content);

        BuildSidebar();
        BuildResultPanel();
        BuildLayout();

        _nav.SelectedIndexChanged += OnCategoryChanged;
        Shown += async (_, _) => await RunSafeAsync(RefreshAsync);
    }

    // ------------------------------------------------------------------
    private void BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 1,
            BackColor = WorkBuddyTheme.Border,
        };

        split.Panel1.Controls.Add(_nav);
        split.Panel1MinSize = WorkBuddyTheme.SidebarWidth;
        split.SplitterDistance = WorkBuddyTheme.SidebarWidth;

        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 1,
            BackColor = WorkBuddyTheme.Border,
        };
        var contentWrap = new Panel { Dock = DockStyle.Fill };
        contentWrap.Controls.Add(_content);
        contentWrap.Controls.Add(_emptyState);
        rightSplit.Panel1.Controls.Add(contentWrap);
        rightSplit.Panel2.Controls.Add(_resultTabs);
        rightSplit.Panel2MinSize = WorkBuddyTheme.ResultPanelWidth;
        rightSplit.SplitterDistance = rightSplit.Width - WorkBuddyTheme.ResultPanelWidth;
        split.Panel2.Controls.Add(rightSplit);

        var toolbar = BuildToolbar();
        var status = new StatusStrip { BackColor = WorkBuddyTheme.ContentBackground };
        status.Items.Add(_statusLabel);

        Controls.Add(split);
        Controls.Add(toolbar);
        Controls.Add(status);
    }

    private void BuildSidebar()
    {
        _nav.Dock = DockStyle.Fill;
        _nav.IntegralHeight = false;
        _nav.BorderStyle = BorderStyle.None;
        _nav.BackColor = WorkBuddyTheme.SidebarBackground;
        _nav.ForeColor = WorkBuddyTheme.SidebarForeground;
        _nav.Font = WorkBuddyTheme.UiFont;
        _nav.ItemHeight = WorkBuddyTheme.RowHeight;
        _nav.DrawMode = DrawMode.OwnerDrawFixed;
        _nav.DrawItem += DrawSidebarItem;
    }

    private void DrawSidebarItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var hover = (e.State & DrawItemState.HotLight) == DrawItemState.HotLight;

        using var background = new SolidBrush(
            selected ? WorkBuddyTheme.SidebarSelected
            : hover ? WorkBuddyTheme.SidebarHover
            : WorkBuddyTheme.SidebarBackground);
        e.Graphics.FillRectangle(background, e.Bounds);

        if (selected)
        {
            using var accent = new SolidBrush(WorkBuddyTheme.Accent);
            e.Graphics.FillRectangle(accent, new Rectangle(e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height));
        }

        var text = _nav.Items[e.Index].ToString();
        TextRenderer.DrawText(
            e.Graphics, text, WorkBuddyTheme.UiFont,
            new Rectangle(e.Bounds.Left + 18, e.Bounds.Top, e.Bounds.Width - 24, e.Bounds.Height),
            selected ? WorkBuddyTheme.SidebarForeground : WorkBuddyTheme.SidebarMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private void BuildResultPanel()
    {
        _resultTabs.Appearance = TabAppearance.FlatButtons;
        _resultTabs.ItemSize = new Size((WorkBuddyTheme.ResultPanelWidth - 8) / 2, 34);
        _resultTabs.SizeMode = TabSizeMode.Fixed;

        var overviewPage = new TabPage("概览");
        var artifactPage = new TabPage("产物");
        _resultTabs.TabPages.Add(overviewPage);
        _resultTabs.TabPages.Add(artifactPage);

        ConfigureResultList(_overviewList);
        _overviewList.Columns.Add("项", 140);
        _overviewList.Columns.Add("值", 150);
        overviewPage.Controls.Add(_overviewList);

        ConfigureResultList(_artifactList);
        _artifactList.Columns.Add("内容贡献", 280);
        artifactPage.Controls.Add(_artifactList);
    }

    private static void ConfigureResultList(ListView list)
    {
        list.BorderStyle = BorderStyle.None;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        list.BackColor = WorkBuddyTheme.PanelBackground;
        list.ForeColor = WorkBuddyTheme.TextPrimary;
        list.FullRowSelect = true;
    }

    private ToolStrip BuildToolbar()
    {
        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            BackColor = WorkBuddyTheme.PanelBackground,
            Padding = new Padding(8, 4, 8, 4),
        };

        toolbar.Items.Add(new ToolStripLabel(AppInfo.Name)
        {
            Font = WorkBuddyTheme.TitleFont,
            ForeColor = WorkBuddyTheme.TextPrimary,
        });
        toolbar.Items.Add(new ToolStripSeparator());

        AddToolbarButton(toolbar, "插件管理…", async () =>
        {
            using var dialog = new PluginManagerForm(_service, _logger);
            dialog.ShowDialog(this);
            await RefreshAsync();
        });
        AddToolbarButton(toolbar, "刷新", RefreshAsync);
        AddToolbarButton(toolbar, "打开插件目录", () =>
        {
            Directory.CreateDirectory(_service.PluginsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", _service.PluginsDirectory)
            {
                UseShellExecute = true,
            });
            return Task.CompletedTask;
        });
        AddToolbarButton(toolbar, "关于", () =>
        {
            using var dialog = new AboutForm(_service.PluginsDirectory, _pluginsSource, _runtimeDescription);
            dialog.ShowDialog(this);
            return Task.CompletedTask;
        });
        return toolbar;
    }

    private void AddToolbarButton(ToolStrip toolbar, string text, Func<Task> action)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += async (_, _) => await RunSafeAsync(action);
        toolbar.Items.Add(button);
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

        BuildResultPanelData(snapshot);
        _statusLabel.Text =
            $"已安装 {snapshot.Plugins.Count} · 已启用 {snapshot.EnabledCount} · " +
            $"分类 {snapshot.Categories.Count} · 内容贡献 {snapshot.ContributionCount}";
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

    private void BuildResultPanelData(PluginSnapshot snapshot)
    {
        _overviewList.BeginUpdate();
        _overviewList.Items.Clear();
        AddOverviewRow("已安装", snapshot.Plugins.Count.ToString());
        AddOverviewRow("已启用", snapshot.EnabledCount.ToString());
        AddOverviewRow("分类", snapshot.Categories.Count.ToString());
        AddOverviewRow("内容贡献", snapshot.ContributionCount.ToString());
        AddOverviewRow("插件目录", Path.GetFileName(_service.PluginsDirectory));
        _overviewList.EndUpdate();

        _artifactList.BeginUpdate();
        _artifactList.Items.Clear();
        foreach (var category in snapshot.Categories)
        {
            foreach (var plugin in snapshot.Plugins)
            {
                if (plugin.CategoryId == category.Id)
                {
                    _artifactList.Items.Add($"{category.Label} · {plugin.Name}");
                }
            }
        }
        _artifactList.EndUpdate();
    }

    private void AddOverviewRow(string caption, string value)
    {
        var item = new ListViewItem(caption) { ForeColor = WorkBuddyTheme.TextMuted };
        item.SubItems.Add(new ListViewItem.ListViewSubItem(item, value)
        {
            ForeColor = WorkBuddyTheme.TextPrimary,
        });
        _overviewList.Items.Add(item);
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
                this, "插件窗口嵌入失败，请查看日志后重试。", AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BuildEmptyState()
    {
        _emptyState.BackColor = WorkBuddyTheme.ContentBackground;
        if (_emptyState.Controls.Count > 0)
        {
            return;
        }
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = WorkBuddyTheme.ContentBackground,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 54));

        var title = new Label
        {
            Text = "请安装插件",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Font = new Font(WorkBuddyTheme.UiFont.FontFamily, 20F, FontStyle.Bold),
            ForeColor = WorkBuddyTheme.TextPrimary,
        };
        var hint = new Label
        {
            Text = "当前宿主没有任何业务内容；所有内容都必须由插件提供。",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            ForeColor = WorkBuddyTheme.TextMuted,
        };
        var directory = new Label
        {
            Text = _service.PluginsDirectory,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            ForeColor = WorkBuddyTheme.TextMuted,
        };
        var button = new Button
        {
            Text = "打开插件管理…",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            BackColor = WorkBuddyTheme.Accent,
            ForeColor = WorkBuddyTheme.AccentText,
            FlatStyle = FlatStyle.Flat,
            Padding = new Padding(16, 6, 16, 6),
        };
        button.FlatAppearance.BorderSize = 0;
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
            MessageBox.Show(this, "发生未预期错误：" + ex.Message, AppInfo.Name,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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
