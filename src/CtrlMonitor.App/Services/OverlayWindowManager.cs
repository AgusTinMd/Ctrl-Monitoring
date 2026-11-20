using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CtrlMonitor.App.ViewModels;
using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Mantiene la ventana del overlay: la muestra solo cuando una app ocupa la
/// pantalla completa, la deja click-through y registra el atajo global. No
/// interactúa con el proceso del juego de ninguna forma.
/// </summary>
public sealed class OverlayWindowManager : IDisposable
{
    private const int HotKeyId = 0x0C7A;
    private const int WmHotKey = 0x0312;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int HalfVisibleMargin = 80;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _timer;

    private OverlayWindow? _window;
    private HwndSource? _source;
    private bool? _manualVisible;
    private bool _lastFullscreen;
    private bool _disposed;
    private bool _started;

    public OverlayWindowManager(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(800),
            DispatcherPriority.Background,
            OnTimerTick,
            Dispatcher.CurrentDispatcher);
        _timer.Stop();
    }

    public void Start()
    {
        if (_started || _disposed)
            return;

        _started = true;

        _window = new OverlayWindow(_viewModel);
        _window.Closing += OnWindowClosing;

        IntPtr handle = new WindowInteropHelper(_window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        // La ventana nace click-through. En modo edición se quita ese estilo
        // para poder arrastrarla con el mouse.
        ApplyClickThrough();

        if (!RegisterHotKey(handle, HotKeyId, ModControl | ModAlt | ModNoRepeat, 0x4F))
            AppLog.Warn("No se pudo registrar el atajo Ctrl+Alt+O del overlay.");

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _lastFullscreen = IsForegroundFullscreen();
        ApplyVisibility();
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        bool fullscreen = IsForegroundFullscreen();

        // Al cambiar entre escritorio y pantalla completa se vuelve a la
        // visibilidad automática, descartando el override del atajo.
        if (fullscreen != _lastFullscreen)
        {
            _lastFullscreen = fullscreen;
            _manualVisible = null;
        }

        ApplyVisibility();
        KeepOnTop();
    }

    /// <summary>
    /// Algunos juegos en borderless también se marcan como topmost y quedan
    /// por encima de la barra. Se reafirma el orden en cada tick, sin activar
    /// la ventana ni quitarle el foco al juego.
    /// </summary>
    private void KeepOnTop()
    {
        if (_window?.IsVisible != true)
            return;

        IntPtr handle = new WindowInteropHelper(_window).Handle;

        if (handle != IntPtr.Zero)
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
            return;

        switch (e.PropertyName)
        {
            case nameof(MainViewModel.OverlayEnabled):
                if (!_viewModel.OverlayEnabled)
                    _manualVisible = null;

                ApplyVisibility();
                ApplyClickThrough();
                break;

            case nameof(MainViewModel.IsOverlayEditing):
                ApplyClickThrough();
                ApplyVisibility();
                break;

            case nameof(MainViewModel.OverlayLeft):
            case nameof(MainViewModel.OverlayTop):
                if (_window?.IsVisible == true)
                    EnsurePosition();

                break;
        }
    }

    private void ApplyVisibility()
    {
        if (_window is null || _disposed)
            return;

        bool visible = _viewModel.OverlayEnabled
                       && (_viewModel.IsOverlayEditing
                           || (_manualVisible ?? _lastFullscreen));

        if (visible)
            ShowOverlay();
        else
            _window.Hide();
    }

    private void ShowOverlay()
    {
        if (_window is null || _window.IsVisible)
            return;

        if (_viewModel.OverlayLeft is double left && _viewModel.OverlayTop is double top)
        {
            _window.Left = left;
            _window.Top = top;
        }
        else
        {
            Rect area = SystemParameters.WorkArea;
            _window.Left = area.Left + (area.Width / 2);
            _window.Top = area.Top + 24;
        }

        _window.Show();
        EnsurePosition();
    }

    private void EnsurePosition()
    {
        if (_window is null)
            return;

        double width = Math.Max(_window.ActualWidth, 120);
        double height = Math.Max(_window.ActualHeight, 24);
        double left;
        double top;

        if (_viewModel.OverlayLeft is double savedLeft && _viewModel.OverlayTop is double savedTop)
        {
            left = savedLeft;
            top = savedTop;
        }
        else
        {
            Rect area = SystemParameters.WorkArea;
            left = area.Left + ((area.Width - width) / 2);
            top = area.Top + 24;
        }

        // Si el monitor cambió o la barra quedó fuera de pantalla, se acerca
        // dejando al menos una franja visible para poder recuperarla.
        double virtualLeft = SystemParameters.VirtualScreenLeft;
        double virtualTop = SystemParameters.VirtualScreenTop;
        double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        left = Math.Clamp(left, virtualLeft - width + HalfVisibleMargin, virtualRight - HalfVisibleMargin);
        top = Math.Clamp(top, virtualTop, virtualBottom - Math.Min(height, HalfVisibleMargin));

        _window.Left = left;
        _window.Top = top;
    }

    private void ApplyClickThrough()
    {
        if (_window is null)
            return;

        IntPtr handle = new WindowInteropHelper(_window).Handle;

        if (handle == IntPtr.Zero)
            return;

        long style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow;

        if (_viewModel.IsOverlayEditing)
        {
            style &= ~(WsExTransparent | WsExNoActivate);
        }
        else
        {
            style |= WsExTransparent | WsExNoActivate;
        }

        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            handled = true;

            // El atajo no cambia la configuración: solo muestra u oculta la
            // barra mientras el overlay está activo.
            if (_viewModel.OverlayEnabled && !_viewModel.IsOverlayEditing)
            {
                _manualVisible = _window?.IsVisible != true;
                ApplyVisibility();
            }
        }

        return IntPtr.Zero;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_disposed)
            return;

        // Alt+F4 sobre el overlay no debe cerrar nada: solo sale del modo
        // edición y vuelve a quedar click-through.
        e.Cancel = true;
        _viewModel.IsOverlayEditing = false;
        _window?.Hide();
    }

    /// <summary>
    /// Comprueba si la ventana en primer plano ocupa todo su monitor. Se
    /// excluyen el escritorio, la barra de tareas y las ventanas maximizadas
    /// normales, que nunca deben disparar el overlay.
    /// </summary>
    private static bool IsForegroundFullscreen()
    {
        IntPtr window = GetForegroundWindow();

        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window) || IsZoomed(window))
            return false;

        GetWindowThreadProcessId(window, out uint processId);

        if (processId == (uint)Environment.ProcessId)
            return false;

        var className = new System.Text.StringBuilder(64);

        if (GetClassName(window, className, className.Capacity) > 0)
        {
            string name = className.ToString();

            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "MultitaskingViewFrame"
                or "Windows.UI.Core.CoreWindow")
            {
                return false;
            }
        }

        if (!GetWindowRect(window, out NativeRect rect))
            return false;

        IntPtr monitor = MonitorFromWindow(window, MonitorDefaultToNearest);

        if (monitor == IntPtr.Zero)
            return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (!GetMonitorInfo(monitor, ref info))
            return false;

        const int tolerance = 2;

        return rect.Left <= info.Monitor.Left + tolerance
               && rect.Top <= info.Monitor.Top + tolerance
               && rect.Right >= info.Monitor.Right - tolerance
               && rect.Bottom >= info.Monitor.Bottom - tolerance;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_window is not null)
        {
            _window.Closing -= OnWindowClosing;
            IntPtr handle = new WindowInteropHelper(_window).Handle;

            if (handle != IntPtr.Zero)
                UnregisterHotKey(handle, HotKeyId);

            _source?.RemoveHook(WndProc);
            _source = null;
            _window.Close();
            _window = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);
}
