namespace PluginShellWin.Infrastructure;

/// <summary>单实例守卫（桌面 App 常规行为）：避免重复双击开出多个窗口。</summary>
public sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;
    private readonly string _name;

    public SingleInstance(string name) => _name = name;

    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, name: $@"Local\{_name}", out var createdNew);
            if (createdNew)
            {
                return true;
            }
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch (Exception)
        {
            // 守卫本身失败时放行，不能因为单实例机制让用户打不开应用
            return true;
        }
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (Exception)
        {
            // 非持有线程释放会抛异常，忽略
        }
        _mutex.Dispose();
        _mutex = null;
    }
}
