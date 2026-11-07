using PluginShellWin.Core.Application;
using PluginShellWin.Core.Domain;
using PluginShellWin.Infrastructure;

namespace PluginShellWin.App.Presentation;

/// <summary>插件管理：列出 / 安装 / 启用 / 禁用 / 卸载，按分类筛选。</summary>
public sealed class PluginManagerForm : Form
{
    private const string AllCategories = "__all__";

    private readonly PluginService _service;
    private readonly AppLogger _logger;
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly DataGridView _grid = new();
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
    };
    private PluginSnapshot _snapshot = new(Array.Empty<PluginInfo>(), Array.Empty<CategoryInfo>(), 0);

    public PluginManagerForm(PluginService service, AppLogger logger)
    {
        _service = service;
        _logger = logger;

        Text = "插件管理";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(940, 620);
        MinimumSize = new Size(780, 520);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        var pathBar = new Label
        {
            Text = "插件目录：" + service.PluginsDirectory,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };
        layout.Controls.Add(pathBar, 0, 0);

        var filterBar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        filterBar.Controls.Add(new Label { Text = "分类筛选：", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        filterBar.Controls.Add(_filter);
        var refresh = new Button { Text = "刷新", AutoSize = true };
        refresh.Click += async (_, _) => await SafeAsync(ReloadAsync);
        filterBar.Controls.Add(refresh);
        layout.Controls.Add(filterBar, 0, 1);

        ConfigureGrid();
        layout.Controls.Add(_grid, 0, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        AddButton(buttons, "安装文件夹…", OnInstallAsync);
        AddButton(buttons, "启用", OnEnableAsync);
        AddButton(buttons, "禁用", OnDisableAsync);
        AddButton(buttons, "卸载", OnUninstallAsync);
        var close = new Button { Text = "关闭", AutoSize = true };
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        layout.Controls.Add(buttons, 0, 3);

        var logGroup = new GroupBox { Text = "运行时日志", Dock = DockStyle.Fill };
        logGroup.Controls.Add(_log);
        layout.Controls.Add(logGroup, 0, 4);

        Controls.Add(layout);
        _filter.SelectedIndexChanged += async (_, _) => await SafeAsync(async () =>
        {
            RenderRows();
            await Task.CompletedTask;
        });

        Shown += async (_, _) => await SafeAsync(ReloadAsync);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "插件 ID", FillWeight = 26 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名称", FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Category", HeaderText = "分类", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "版本", FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "状态", FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Detail", HeaderText = "依赖 / 说明", FillWeight = 24 });
    }

    private static void AddButton(Control parent, string text, Func<Task> handler)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) => await handler();
        parent.Controls.Add(button);
    }

    // ------------------------------------------------------------------
    private async Task ReloadAsync()
    {
        _snapshot = await _service.GetSnapshotAsync();

        var previous = _filter.SelectedItem as CategoryFilter ?? CategoryFilter.All;
        _filter.Items.Clear();
        _filter.Items.Add(CategoryFilter.All);
        foreach (var category in _snapshot.Categories)
        {
            _filter.Items.Add(new CategoryFilter(category.Id, $"{category.Label} ({category.Id})"));
        }
        var restore = _filter.Items.OfType<CategoryFilter>()
            .FirstOrDefault(item => item.Id == previous.Id) ?? CategoryFilter.All;
        _filter.SelectedItem = restore;

        RenderRows();
        _log.Text = string.Join(Environment.NewLine, await _service.GetLogsAsync(160));
    }

    private void RenderRows()
    {
        var selected = _filter.SelectedItem as CategoryFilter ?? CategoryFilter.All;
        _grid.Rows.Clear();
        foreach (var plugin in _snapshot.Plugins)
        {
            if (selected.Id != AllCategories && plugin.CategoryId != selected.Id)
            {
                continue;
            }
            var detail = string.IsNullOrWhiteSpace(plugin.Message)
                ? (string.IsNullOrWhiteSpace(plugin.Description) ? plugin.DependencyText : plugin.Description)
                : plugin.Message;
            var index = _grid.Rows.Add(
                plugin.Id,
                plugin.Name,
                $"{plugin.CategoryLabel} [{plugin.CategoryId}]",
                plugin.Version,
                plugin.StateLabel,
                $"{plugin.DependencyText} · {detail}");
            _grid.Rows[index].Tag = plugin.Id;
        }
    }

    private string? SelectedPluginId()
    {
        if (_grid.CurrentRow?.Tag is string id)
        {
            return id;
        }
        MessageBox.Show(this, "请先在列表中选择一个插件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return null;
    }

    // ------------------------------------------------------------------
    private async Task OnInstallAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择插件文件夹（必须包含 manifest.json）",
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        await ApplyAsync(() => _service.InstallAsync(dialog.SelectedPath), "安装");
    }

    private async Task OnEnableAsync()
    {
        var id = SelectedPluginId();
        if (id is not null)
        {
            await ApplyAsync(() => _service.EnableAsync(id), "启用");
        }
    }

    private async Task OnDisableAsync()
    {
        var id = SelectedPluginId();
        if (id is not null)
        {
            await ApplyAsync(() => _service.DisableAsync(id), "禁用");
        }
    }

    private async Task OnUninstallAsync()
    {
        var id = SelectedPluginId();
        if (id is null)
        {
            return;
        }
        var confirm = MessageBox.Show(
            this,
            $"确定卸载插件 {id} 吗？其目录与全部运行时副作用都会被清理。",
            "确认卸载",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm == DialogResult.Yes)
        {
            await ApplyAsync(() => _service.UninstallAsync(id), "卸载");
        }
    }

    private async Task ApplyAsync(Func<Task<OperationResult>> action, string label)
    {
        try
        {
            var result = await action();
            if (!result.Ok)
            {
                _logger.Warn($"{label}未成功：{result.Message}");
                MessageBox.Show(this, result.Message, "插件操作未成功", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            await ReloadAsync();
        }
        catch (PluginShellException ex)
        {
            _logger.Error($"{label}异常", ex);
            MessageBox.Show(this, ex.Message, "插件操作被拒绝", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (PluginShellException ex)
        {
            MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _logger.Error("插件管理界面异常", ex);
            MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed record CategoryFilter(string Id, string Text)
    {
        public static CategoryFilter All { get; } = new(AllCategories, "全部分类");

        public override string ToString() => Text;
    }
}
