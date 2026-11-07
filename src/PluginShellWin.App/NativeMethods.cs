using System.Runtime.InteropServices;

namespace PluginShellWin.App;

/// <summary>
/// 把运行时创建的原生窗口嵌进 WinForms 面板所需的最小 Win32 子集。
/// </summary>
internal static class NativeMethods
{
    internal const int GWL_STYLE = -16;

    internal const long WS_CHILD = 0x40000000L;
    internal const long WS_VISIBLE = 0x10000000L;
    internal const long WS_POPUP = 0x80000000L;
    internal const long WS_CAPTION = 0x00C00000L;
    internal const long WS_THICKFRAME = 0x00040000L;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool MoveWindow(
        IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AttachConsole(int processId);
}
