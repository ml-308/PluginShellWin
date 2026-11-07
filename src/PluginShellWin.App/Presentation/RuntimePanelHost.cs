namespace PluginShellWin.App.Presentation;

/// <summary>
/// 把插件运行时创建的原生窗口（HWND）嵌入到 WinForms 容器里。
/// 这是"宿主 C# + 插件运行时 Python/Qt"两个 UI 框架共存的关键点：
/// 只做窗口父子关系与尺寸同步，不做任何跨框架控件混排。
/// </summary>
public sealed class RuntimePanelHost : IDisposable
{
    private readonly Control _container;
    private IntPtr _child;
    private bool _disposed;

    public RuntimePanelHost(Control container)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _container.Resize += OnContainerResize;
    }

    public IntPtr ChildHandle => _child;

    public bool Attach(long windowHandle)
    {
        if (windowHandle <= 0)
        {
            return false;
        }

        Detach();
        _child = new IntPtr(windowHandle);
        NativeMethods.SetParent(_child, _container.Handle);

        var style = NativeMethods.GetWindowLongPtr(_child, NativeMethods.GWL_STYLE).ToInt64();
        style &= ~(NativeMethods.WS_POPUP | NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME);
        style |= NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE;
        NativeMethods.SetWindowLongPtr(_child, NativeMethods.GWL_STYLE, new IntPtr(style));

        NativeMethods.ShowWindow(_child, 5 /* SW_SHOW */);
        ResizeChild();
        return NativeMethods.GetParent(_child) == _container.Handle;
    }

    public void Detach()
    {
        if (_child == IntPtr.Zero)
        {
            return;
        }
        NativeMethods.SetParent(_child, IntPtr.Zero);
        _child = IntPtr.Zero;
    }

    private void OnContainerResize(object? sender, EventArgs e) => ResizeChild();

    private void ResizeChild()
    {
        if (_child == IntPtr.Zero)
        {
            return;
        }
        var size = _container.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }
        NativeMethods.MoveWindow(_child, 0, 0, size.Width, size.Height, true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _container.Resize -= OnContainerResize;
        Detach();
    }
}
