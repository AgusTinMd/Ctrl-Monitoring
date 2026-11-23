using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CtrlMonitor.App.ViewModels;
using CtrlMonitor.Core.Diagnostics;
using CtrlMonitor.Core.Overlay;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Mantiene la ventana del overlay: la muestra solo cuando una app ocupa la
/// pantalla completa, la deja click-through, la ubica según la posición
/// elegida y registra el atajo global. No interactúa con el proceso del juego
/// de ninguna forma.
/// </summary>
public sealed class OverlayWindowManager : IDisposable
{
    private const int HotKeyId = 0x0C7A;
    private const int ModeHotKeyId = 0x0C7B;
    private const int RecordHotKeyId = 0x0C7C;
    private const int WmHotKey = 0x0312;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const int HalfVisibleMargin = 80;
    private const double SnapThreshold = 18;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _timer;

    private OverlayWindow? _window;
    private OverlayGuideWindow? _guides;
    private HwndSource? _source;
    private IntPtr _targetMonitor;
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
        _window.DragCompleted += OnDragCompleted;
        _window.SizeChanged += OnWindowSizeChanged;

        // Con el overlay a la vista la app mide aunque la ventana esté oculta.
        _window.IsVisibleChanged += (_, _) => _viewModel.SetOverlayShown(_window.IsVisible);

        IntPtr handle = new WindowInteropHelper(_window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        // La ventana nace click-through. Desbloqueada se quita ese estilo para
        // poder arrastrarla con el mouse.
        ApplyClickThrough();

        RegisterHotkeys();
        _viewModel.HotkeysChanged += OnHotkeysChanged;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _lastFullscreen = IsForegroundFullscreen(out IntPtr monitor, out _);

        if (_lastFullscreen)
            _targetMonitor = monitor;

        ApplyVisibility();
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        // Con el overlay apagado no hace falta vigilar la ventana en primer plano.
        if (!_viewModel.OverlayEnabled)
        {
            _viewModel.UpdateFpsTarget(0);
            return;
        }

        bool fullscreen = IsForegroundFullscreen(out IntPtr monitor, out int processId);

        // Los FPS se miden del juego en pantalla completa.
        _viewModel.UpdateFpsTarget(fullscreen ? processId : 0);

        // La barra va al monitor del juego en pantalla completa.
        if (fullscreen && monitor != _targetMonitor && !_viewModel.IsOverlayEditing)
        {
            _targetMonitor = monitor;
            EnsurePosition();
        }

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

            case nameof(MainViewModel.OverlayAnchor):
            case nameof(MainViewModel.OverlayLeft):
            case nameof(MainViewModel.OverlayTop):
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

        UpdateGuides();
    }

    private void ShowOverlay()
    {
        if (_window is null || _window.IsVisible)
            return;

        EnsurePosition();
        _window.Show();
        EnsurePosition();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Con un ancla, la barra crece desde su borde: arriba a la derecha
        // crece hacia la izquierda al sumar métricas.
        if (_viewModel.OverlayAnchor != OverlayAnchors.Custom)
            EnsurePosition();
    }

    private void EnsurePosition()
    {
        if (_window is null)
            return;

        (double width, double height) = BarSize();
        OverlayRect area = MonitorArea();
        double left;
        double top;

        if (_viewModel.OverlayAnchor == OverlayAnchors.Custom
            && _viewModel.OverlayLeft is double savedLeft
            && _viewModel.OverlayTop is double savedTop)
        {
            // Si el monitor cambió o la barra quedó fuera de pantalla, se
            // acerca dejando al menos una franja visible para poder recuperarla.
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            left = Math.Clamp(savedLeft, virtualLeft - width + HalfVisibleMargin, virtualRight - HalfVisibleMargin);
            top = Math.Clamp(savedTop, virtualTop, virtualBottom - Math.Min(height, HalfVisibleMargin));
        }
        else
        {
            (left, top) = OverlayAnchors.Place(_viewModel.OverlayAnchor, area, width, height);
        }

        _window.Left = left;
        _window.Top = top;
    }

    private (double Width, double Height) BarSize()
    {
        if (_window is null)
            return (120, 24);

        if (_window.ActualWidth > 0 && _window.ActualHeight > 0)
            return (_window.ActualWidth, _window.ActualHeight);

        // Oculta todavía no tiene tamaño: se mide el contenido.
        _window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size desired = _window.DesiredSize;
        return (Math.Max(desired.Width, 120), Math.Max(desired.Height, 24));
    }

    /// <summary>
    /// Área completa del monitor de destino (no el área de trabajo: los juegos
    /// en pantalla completa tapan la barra de tareas), en unidades de WPF.
    /// </summary>
    private OverlayRect MonitorArea()
    {
        IntPtr monitor = _targetMonitor;

        if (monitor == IntPtr.Zero && _window is not null)
        {
            IntPtr handle = new WindowInteropHelper(_window).Handle;

            if (handle != IntPtr.Zero)
                monitor = MonitorFromWindow(handle, MonitorDefaultToPrimary);
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return new OverlayRect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        Matrix toDip = Matrix.Identity;

        if (_window is not null && PresentationSource.FromVisual(_window)?.CompositionTarget is { } target)
            toDip = target.TransformFromDevice;

        Point topLeft = toDip.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
        Point bottomRight = toDip.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));

        return new OverlayRect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    private void OnDragCompleted(object? sender, EventArgs e)
    {
        if (_window is null)
            return;

        // El monitor de destino pasa a ser donde se soltó la barra.
        IntPtr handle = new WindowInteropHelper(_window).Handle;

        if (handle != IntPtr.Zero)
            _targetMonitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

        (double width, double height) = BarSize();
        string anchor = OverlayAnchors.Snap(
            MonitorArea(),
            _window.Left,
            _window.Top,
            width,
            height,
            SnapThreshold,
            out double left,
            out double top);

        _viewModel.SetOverlayPosition(left, top, anchor);
        EnsurePosition();
        UpdateGuides();
    }

    /// <summary>La grilla de ayuda solo se ve mientras el overlay está desbloqueado.</summary>
    private void UpdateGuides()
    {
        bool show = _viewModel.OverlayEnabled && _viewModel.IsOverlayEditing && _window?.IsVisible == true;

        if (!show)
        {
            _guides?.Hide();
            return;
        }

        if (_guides is null)
        {
            _guides = new OverlayGuideWindow();
            IntPtr guideHandle = new WindowInteropHelper(_guides).EnsureHandle();
            long style = GetWindowLongPtr(guideHandle, GwlExStyle).ToInt64();
            SetWindowLongPtr(guideHandle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
        }

        OverlayRect area = MonitorArea();
        _guides.Left = area.Left;
        _guides.Top = area.Top;
        _guides.Width = area.Width;
        _guides.Height = area.Height;

        if (!_guides.IsVisible)
            _guides.Show();

        // La barra siempre por encima de la grilla.
        KeepOnTop();
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

    private void OnHotkeysChanged(object? sender, EventArgs e) => RegisterHotkeys();

    /// <summary>Registra (o vuelve a registrar) los tres atajos configurables.</summary>
    private void RegisterHotkeys()
    {
        if (_window is null)
            return;

        IntPtr handle = new WindowInteropHelper(_window).Handle;

        if (handle == IntPtr.Zero)
            return;

        Register(handle, HotKeyId, _viewModel.OverlayHotkeyText);
        Register(handle, ModeHotKeyId, _viewModel.OverlayModeHotkeyText);
        Register(handle, RecordHotKeyId, _viewModel.OverlayRecordHotkeyText);
    }

    private void Register(IntPtr handle, int id, string text)
    {
        UnregisterHotKey(handle, id);

        if (!CtrlMonitor.Core.Overlay.Hotkey.TryParse(text, out CtrlMonitor.Core.Overlay.Hotkey hotkey))
            return;

        uint modifiers = ModNoRepeat;

        if (hotkey.Modifiers.HasFlag(CtrlMonitor.Core.Overlay.HotkeyModifiers.Alt))
            modifiers |= ModAlt;

        if (hotkey.Modifiers.HasFlag(CtrlMonitor.Core.Overlay.HotkeyModifiers.Control))
            modifiers |= ModControl;

        if (hotkey.Modifiers.HasFlag(CtrlMonitor.Core.Overlay.HotkeyModifiers.Shift))
            modifiers |= 0x0004;

        if (hotkey.Modifiers.HasFlag(CtrlMonitor.Core.Overlay.HotkeyModifiers.Win))
            modifiers |= 0x0008;

        if (!RegisterHotKey(handle, id, modifiers, (uint)hotkey.VirtualKey))
        {
            AppLog.Warn($"No se pudo registrar el atajo {text}: otra aplicación lo usa.");
            _viewModel.ReportHotkeyConflict(text);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == ModeHotKeyId)
        {
            handled = true;
            _viewModel.ToggleOverlayMode();
            return IntPtr.Zero;
        }

        if (msg == WmHotKey && wParam.ToInt32() == RecordHotKeyId)
        {
            handled = true;
            _viewModel.ToggleBenchmarkRecording();
            return IntPtr.Zero;
        }

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

        // Alt+F4 sobre el overlay no debe cerrar nada: solo lo vuelve a
        // bloquear y queda click-through.
        e.Cancel = true;
        _viewModel.IsOverlayEditing = false;
        _window?.Hide();
        UpdateGuides();
    }

    /// <summary>
    /// Comprueba si la ventana en primer plano ocupa todo su monitor. Se
    /// excluyen el escritorio, la barra de tareas y las ventanas maximizadas
    /// normales, que nunca deben disparar el overlay.
    /// </summary>
    private static bool IsForegroundFullscreen(out IntPtr monitor, out int processIdOut)
    {
        monitor = IntPtr.Zero;
        processIdOut = 0;
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

        IntPtr candidate = MonitorFromWindow(window, MonitorDefaultToNearest);

        if (candidate == IntPtr.Zero)
            return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (!GetMonitorInfo(candidate, ref info))
            return false;

        const int tolerance = 2;

        bool fullscreen = rect.Left <= info.Monitor.Left + tolerance
                          && rect.Top <= info.Monitor.Top + tolerance
                          && rect.Right >= info.Monitor.Right - tolerance
                          && rect.Bottom >= info.Monitor.Bottom - tolerance;

        if (fullscreen)
        {
            monitor = candidate;
            processIdOut = (int)processId;
        }

        return fullscreen;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _guides?.Close();
        _guides = null;

        if (_window is not null)
        {
            _window.Closing -= OnWindowClosing;
            _window.DragCompleted -= OnDragCompleted;
            _window.SizeChanged -= OnWindowSizeChanged;
            IntPtr handle = new WindowInteropHelper(_window).Handle;

            if (handle != IntPtr.Zero)
                UnregisterHotKey(handle, HotKeyId);
                UnregisterHotKey(handle, ModeHotKeyId);
                UnregisterHotKey(handle, RecordHotKeyId);

            _viewModel.HotkeysChanged -= OnHotkeysChanged;

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
