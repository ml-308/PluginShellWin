namespace PluginShellWin.App;

/// <summary>
/// WinExe 没有控制台：从终端启动时 stdout 可能连的是管道，也可能完全不可写。
/// 这里统一兜底，避免"打印一行日志把应用弄崩"这种低级问题。
/// </summary>
internal static class SafeConsole
{
    private static bool _encodingReady;

    public static void WriteLine(string message)
    {
        try
        {
            if (!_encodingReady)
            {
                _encodingReady = true;
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            var stdout = Console.Out;
            if (stdout is null)
            {
                return;
            }
            stdout.WriteLine(message);
            stdout.Flush();
        }
        catch (IOException)
        {
            // 没有控制台
        }
        catch (ObjectDisposedException)
        {
            // 管道已关闭
        }
    }
}
