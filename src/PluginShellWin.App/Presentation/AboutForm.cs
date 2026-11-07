using PluginShellWin.Core.Domain;

namespace PluginShellWin.App.Presentation;

public sealed class AboutForm : Form
{
    public AboutForm(string pluginDirectory, string pluginsSource, string runtimeDescription)
    {
        Text = $"关于 {AppInfo.Name}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 380);

        var title = new Label
        {
            Text = $"{AppInfo.Name}  {AppInfo.Version}",
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 22),
        };
        var subtitle = new Label
        {
            Text = "C# / WinForms 原生宿主 · 插件运行时 Python + Qt · 全程无 Web 技术",
            AutoSize = true,
            Location = new Point(26, 62),
        };

        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 7,
            Location = new Point(26, 96),
            Size = new Size(566, 200),
            AutoSize = false,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(int row, string caption, string value)
        {
            grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            grid.Controls.Add(
                new Label { Text = value, AutoSize = true, MaximumSize = new Size(440, 0), Anchor = AnchorStyles.Left },
                1, row);
        }

        AddRow(0, "插件协议", AppInfo.Protocol);
        AddRow(1, "插件目录", pluginDirectory);
        AddRow(2, "目录来源", pluginsSource);
        AddRow(3, "运行时", runtimeDescription);
        AddRow(4, "本机 .NET", Environment.Version.ToString());
        AddRow(5, "操作系统", Environment.OSVersion.VersionString);
        AddRow(6, "许可", "MIT License, Copyright (c) 2026 ml-308");

        var close = new Button
        {
            Text = "关闭",
            DialogResult = DialogResult.OK,
            Location = new Point(500, 318),
            Size = new Size(92, 32),
        };

        Controls.AddRange(new Control[] { title, subtitle, grid, close });
        AcceptButton = close;
    }
}
