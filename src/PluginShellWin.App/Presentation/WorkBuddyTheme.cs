namespace PluginShellWin.App.Presentation;

/// <summary>
/// WorkBuddy 风格的配色与尺寸常量（WinForms 版）。
/// 深色侧边栏 + 浅色主内容区，与 WorkBuddy 的整体视觉基调一致。
/// </summary>
internal static class WorkBuddyTheme
{
    // 侧边栏（深色，类似 WorkBuddy 左侧任务列表）
    public static readonly Color SidebarBackground = Color.FromArgb(0x1E, 0x1E, 0x24);
    public static readonly Color SidebarForeground = Color.FromArgb(0xD6, 0xD6, 0xDE);
    public static readonly Color SidebarMuted = Color.FromArgb(0x8A, 0x8A, 0x96);
    public static readonly Color SidebarHover = Color.FromArgb(0x2C, 0x2C, 0x34);
    public static readonly Color SidebarSelected = Color.FromArgb(0x35, 0x3A, 0x46);

    // 强调色（WorkBuddy 系蓝紫）
    public static readonly Color Accent = Color.FromArgb(0x4C, 0x6E, 0xF5);
    public static readonly Color AccentText = Color.White;

    // 主内容区（浅色）
    public static readonly Color ContentBackground = Color.FromArgb(0xF7, 0xF7, 0xFA);
    public static readonly Color PanelBackground = Color.White;
    public static readonly Color Border = Color.FromArgb(0xE4, 0xE4, 0xEC);
    public static readonly Color TextPrimary = Color.FromArgb(0x24, 0x26, 0x2E);
    public static readonly Color TextMuted = Color.FromArgb(0x8A, 0x8A, 0x96);

    public static readonly Font UiFont = new("Microsoft YaHei UI", 9F);
    public static readonly Font UiFontBold = new("Microsoft YaHei UI", 9F, FontStyle.Bold);
    public static readonly Font TitleFont = new("Microsoft YaHei UI", 13F, FontStyle.Bold);

    public const int SidebarWidth = 232;
    public const int ResultPanelWidth = 300;
    public const int RowHeight = 36;
}
