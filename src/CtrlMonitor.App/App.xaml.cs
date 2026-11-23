using System.Windows;
using System.Windows.Threading;
using CtrlMonitor.App.Services;
using CtrlMonitor.App.ViewModels;
using CtrlMonitor.Core.Configuration;
using CtrlMonitor.Core.Diagnostics;
using CtrlMonitor.Core.Hardware;
using CtrlMonitor.Core.Localization;

namespace CtrlMonitor.App;

public partial class App : Application
{
    private const string MutexName = @"Global\CtrlMonitor.SingleInstance";
    private const string ShowEventName = @"Global\CtrlMonitor.ShowWindow";
    private const string ExitEventName = @"Global\CtrlMonitor.Exit";

    /// <summary>
    /// Cierra la instancia que ya está corriendo y arranca en su lugar. Sirve
    /// para probar una compilación nueva sin cerrar la app a mano: como corre
    /// elevada, un proceso sin permisos de administrador no puede terminarla.
    /// </summary>
    public const string ReplaceArgument = "--replace";

    private static readonly TimeSpan ReplaceTimeout = TimeSpan.FromSeconds(15);

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitWait;

    private ConfigStore? _store;
    private AppConfig? _config;
    private HardwareService? _hardware;
    private MainViewModel? _viewModel;
    private TrayService? _tray;
    private MainWindow? _window;
    private OverlayWindowManager? _overlay;

    private bool _exiting;
    private bool _cleaned;
    private bool _crashReported;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // El idioma se aplica antes que nada para que los avisos de elevación
        // y de error también salgan traducidos. Leer la configuración no
        // necesita permisos de administrador.
        ApplyConfiguredLanguage();

        if (!EnsureElevated(e.Args))
        {
            Shutdown();
            return;
        }

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

        if (!createdNew && HasArgument(e.Args, ReplaceArgument))
            createdNew = ReplaceExistingInstance(_mutex);

        _ownsMutex = createdNew;

        if (!createdNew)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        // Otra instancia lanzada con --replace pide que esta se cierre.
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            _exitEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                AppLog.Info("Otra instancia pidió reemplazar a esta; cerrando.");
                RequestExit();
            })),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            Initialize(e.Args);
        }
        catch (Exception ex)
        {
            AppLog.Error($"No se pudo iniciar Ctrl Monitor :: {ex}", ex);
            MessageBox.Show(
                Loc.F("app.startFailed", ex.Message),
                "Ctrl Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            RequestExit();
        }
    }

    private bool EnsureElevated(string[] args)
    {
        if (ElevationService.IsElevated())
            return true;

        if (args.Any(arg => arg.Equals(ElevationService.RetryArgument, StringComparison.OrdinalIgnoreCase)))
        {
            AppLog.Warn("La app sigue sin permisos de administrador después de reintentar la elevación.");
            ShowElevationWarning(Loc.T("app.elevation.denied"));
            return false;
        }

        // Se comprueba antes de crear el mutex: si la app corre elevada y se
        // intenta abrir otra instancia sin privilegios, primero se pide UAC y
        // la instancia elevada existente recibe el aviso de mostrarse.
        ElevationResult result = ElevationService.RelaunchElevated(args);

        if (result == ElevationResult.Started)
            return false;

        string detail = result switch
        {
            ElevationResult.Cancelled => Loc.T("app.elevation.cancelled"),
            ElevationResult.Unavailable => Loc.T("app.elevation.unavailable"),
            _ => Loc.T("app.elevation.failed")
        };

        AppLog.Warn($"Elevación no completada: {detail}");
        ShowElevationWarning(detail);
        return false;
    }

    private static void ShowElevationWarning(string detail)
    {
        MessageBox.Show(
            Loc.F("app.needsAdmin", detail),
            "Ctrl Monitor",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void ApplyConfiguredLanguage()
    {
        try
        {
            Loc.Instance.SetLanguage(new ConfigStore().Load().Language);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"No se pudo aplicar el idioma configurado: {ex.Message}");
            Loc.Instance.SetLanguage(AppLanguage.Auto);
        }
    }

    private void Initialize(string[] args)
    {
        AppLog.Info("Ctrl Monitor iniciando…");

        _store = new ConfigStore();

        if (_store.MigrateFrom(AppPaths.LegacyConfigFile))
            AppLog.Info($"Configuración migrada desde {AppPaths.LegacyConfigFile}");

        _config = _store.Load();

        if (_store.LastError is not null)
            AppLog.Warn($"Configuración con problemas: {_store.LastError}");

        // Informes de errores: solo si el usuario los activó en Ajustes.
        ErrorReporting.Configure(_config.SendErrorReports, _config.InstallId);

        // La tarea vieja apunta a FanCtrl.exe; si el usuario tenía el inicio
        // automático activo, se recrea con el nombre y el ejecutable nuevos.
        if (StartupService.RemoveLegacyTask() && _config.RunAtStartup
            && !StartupService.SetEnabled(true, out string? startupError))
        {
            AppLog.Warn($"No se pudo recrear la tarea de inicio: {startupError}");
        }

        // Si la app se instaló en otra carpeta o se movió, la tarea de inicio
        // puede seguir apuntando al exe anterior.
        if (_config.RunAtStartup)
            StartupService.EnsureTaskPointsHere();

        _hardware = new HardwareService();
        _hardware.Open();
        ErrorReporting.SetHardware(_hardware.CpuName, _hardware.GpuName, _hardware.MotherboardName);

        if (HasArgument(args, "--test-error-report"))
            ErrorReporting.SendTest();

        AppLog.Info(
            $"Hardware: {_hardware.MotherboardName} / {_hardware.SuperIoName} / driver {_hardware.DriverVersion}");

        if (!_hardware.IsAdministrator)
            AppLog.Warn("La app no corre como administrador; algunos sensores pueden faltar.");

        _viewModel = new MainViewModel(_store, _config, _hardware);
        _tray = new TrayService(_viewModel, ShowMainWindow, RequestExit);

        // Aviso de versión nueva: solo si el usuario lo activó en Ajustes.
        _ = _viewModel.CheckForUpdatesAsync();

        _window = new MainWindow(_viewModel);
        _window.Closed += OnMainWindowClosed;

        try
        {
            _overlay = new OverlayWindowManager(_viewModel);
            _overlay.Start();
        }
        catch (Exception ex)
        {
            // El overlay es opcional: si falla, la monitorización sigue igual.
            AppLog.Error("No se pudo iniciar el overlay", ex);
            _overlay = null;
        }

        bool startHidden = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                           || _config.StartMinimized;

        // Mide solo mientras la ventana o el overlay están a la vista; si
        // arranca en la bandeja, queda en reposo hasta que se abra.
        _viewModel.SetWindowShown(!startHidden);

        if (!startHidden)
        {
            _window.Show();
        }
        else
        {
            AppLog.Info("Ctrl Monitor iniciado minimizado en la bandeja.");
        }

        _store.Save(_config);
    }

    public void ShowMainWindow()
    {
        if (_window is null || _exiting)
            return;

        if (!_window.IsVisible)
            _window.Show();

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    public void RequestExit()
    {
        if (_exiting)
            return;

        _exiting = true;
        _window?.AllowClose();
        Cleanup();
        Shutdown();
    }

    private void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (!_exiting)
            RequestExit();
    }

    private void Cleanup()
    {
        if (_cleaned)
            return;

        _cleaned = true;

        try
        {
            _tray?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("Error al cerrar el icono de bandeja", ex);
        }

        try
        {
            _overlay?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("Error al cerrar el overlay", ex);
        }

        try
        {
            _viewModel?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("Error al cerrar el view model", ex);
        }

        try
        {
            _hardware?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Error("Error al cerrar el hardware", ex);
        }

        try
        {
            if (_store is not null && _config is not null)
                _store.Save(_config);
        }
        catch (Exception ex)
        {
            AppLog.Error("Error al guardar la configuración final", ex);
        }

        try
        {
            _showWait?.Unregister(null);
            _showEvent?.Dispose();
            _exitWait?.Unregister(null);
            _exitEvent?.Dispose();
        }
        catch
        {
            // best effort
        }

        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // El mutex puede haberse liberado al terminar el proceso.
            }
        }

        _mutex?.Dispose();
        AppLog.Info("Ctrl Monitor cerrado.");

        // Envía lo pendiente y cierra la conexión con Sentry.
        ErrorReporting.Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Cleanup();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error($"Excepción no controlada en la UI :: {e.Exception}", e.Exception);
        e.Handled = true;

        // Mientras el aviso está abierto, la UI sigue procesando eventos y el
        // mismo error puede repetirse: se avisa una sola vez y la app se cierra.
        if (_crashReported)
            return;

        _crashReported = true;

        // Antes del aviso, por si el usuario cierra la app sin aceptarlo.
        ErrorReporting.Flush();

        MessageBox.Show(
            Loc.F("app.crash", e.Exception.Message),
            "Ctrl Monitor",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        RequestExit();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error("Excepción no controlada", e.ExceptionObject as Exception);
        ErrorReporting.Flush();
    }

    private static bool HasArgument(string[] args, string argument) =>
        args.Any(arg => arg.Equals(argument, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pide a la instancia existente que se cierre y espera a que suelte el
    /// mutex. Devuelve <c>true</c> si esta instancia quedó como dueña.
    /// </summary>
    private static bool ReplaceExistingInstance(Mutex mutex)
    {
        try
        {
            using EventWaitHandle exit = EventWaitHandle.OpenExisting(ExitEventName);
            exit.Set();
        }
        catch (Exception ex)
        {
            // Una versión vieja de la app no tiene este evento: hay que cerrarla a mano.
            AppLog.Warn($"No se pudo pedir el cierre de la instancia existente: {ex.Message}");
            return false;
        }

        try
        {
            if (mutex.WaitOne(ReplaceTimeout))
            {
                AppLog.Info("Instancia anterior reemplazada.");
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            // La anterior terminó sin liberar el mutex; igual queda tomado por esta.
            AppLog.Info("Instancia anterior reemplazada (mutex abandonado).");
            return true;
        }

        AppLog.Warn("La instancia anterior no se cerró a tiempo.");
        return false;
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(ShowEventName);
            handle.Set();
        }
        catch
        {
            // La primera instancia todavía no creó el evento.
        }
    }
}
