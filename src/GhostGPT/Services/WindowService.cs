using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
namespace GhostGPT.Services;
public sealed class WindowService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    private readonly Window window;
    private readonly HwndSource source;
    private readonly List<int> registered = [];
    public event Action<int>? Hotkey;
    public bool CanHide { get; private set; }
    public WindowService(Window window)
    {
        this.window = window;
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source.AddHook(Hook);
    }
    public string Apply(Settings settings)
    {
        window.Topmost = settings.AlwaysOnTop;
        bool supported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
        bool ok = SetWindowDisplayAffinity(source.Handle, settings.ExcludeCapture && supported ? 0x11u : 0u);
        return !ok ? "Не удалось изменить исключение из захвата." : settings.ExcludeCapture
            ? supported ? "Исключение из захвата включено · проверьте в программе демонстрации" : "Исключение не поддерживается: нужна Windows 10 2004+"
            : "Окно видно при захвате экрана";
    }
    public string Register(Settings settings)
    {
        foreach (var id in registered) UnregisterHotKey(source.Handle, id);
        registered.Clear(); CanHide = false;
        var keys = new[] { settings.ToggleKey, settings.AnswerKey, settings.ScreenshotKey, settings.PanicKey };
        List<string> errors = [];
        for (int i = 0; i < keys.Length; i++)
        {
            if (!Enum.TryParse<Key>(keys[i], true, out var key) || key == Key.None || !RegisterHotKey(source.Handle, i + 1, 0x4006, (uint)KeyInterop.VirtualKeyFromKey(key))) errors.Add(keys[i]);
            else { registered.Add(i + 1); if (i == 0) CanHide = true; }
        }
        return errors.Count > 0 ? "Не зарегистрированы Ctrl+Shift+" + string.Join(", ", errors) + ". Измените клавиши в настройках." : "Горячие клавиши готовы";
    }
    public void RestoreBounds(Settings settings)
    {
        window.Width = double.IsFinite(settings.Width) ? Math.Clamp(settings.Width, 480, 1600) : 740;
        window.Height = double.IsFinite(settings.Height) ? Math.Clamp(settings.Height, 400, 1400) : 790;
        window.Left = double.IsFinite(settings.Left) ? settings.Left : 100;
        window.Top = double.IsFinite(settings.Top) ? settings.Top : 100;
        ClampToMonitor();
    }
    public void ClampToMonitor()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(source.Handle, 2), ref info)) return;
        var transform = source.CompositionTarget.TransformFromDevice;
        var a = transform.Transform(new Point(info.Work.Left, info.Work.Top));
        var b = transform.Transform(new Point(info.Work.Right, info.Work.Bottom));
        window.Left = Math.Clamp(window.Left, a.X, Math.Max(a.X, b.X - window.Width));
        window.Top = Math.Clamp(window.Top, a.Y, Math.Max(a.Y, b.Y - window.Height));
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x312) { handled = true; Hotkey?.Invoke(wParam.ToInt32()); }
        if (msg == 0x7E) window.Dispatcher.BeginInvoke(ClampToMonitor);
        return IntPtr.Zero;
    }
    public void Dispose() { foreach (var id in registered) UnregisterHotKey(source.Handle, id); source.RemoveHook(Hook); }
}
