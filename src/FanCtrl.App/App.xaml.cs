using System.Windows;
using System.Windows.Threading;
using FanCtrl.App.Services;
using FanCtrl.App.ViewModels;
using FanCtrl.Core.Configuration;
using FanCtrl.Core.Diagnostics;
using FanCtrl.Core.Hardware;

namespace FanCtrl.App;

public partial class App : Application
{
    private const string MutexName = @"Global\FanCtrl.SingleInstance";
    private const string ShowEventName = @"Global\FanCtrl.ShowWindow";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    private ConfigStore? _store;
    private AppConfig? _config;
    private HardwareService? _hardware;
    private MainViewModel? _viewModel;
    private TrayService? _tray;
    private MainWindow? _window;

    private bool _exiting;
    private bool _cleaned;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!EnsureElevated(e.Args))
        {
            Shutdown();
            return;
        }

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
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

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            Initialize(e.Args);
        }
        catch (Exception ex)
        {
            AppLog.Error($"No se pudo iniciar FanCtrl :: {ex}", ex);
            MessageBox.Show(
                $"No se pudo iniciar FanCtrl.\n\n{ex.Message}\n\n" +
                "Verificá que PawnIO esté instalado y que la app se ejecute como administrador.",
                "FanCtrl",
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
            ShowElevationWarning("Windows no otorgó los permisos solicitados.");
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
            ElevationResult.Cancelled => "Se canceló la solicitud de permisos.",
            ElevationResult.Unavailable => "No se encontró FanCtrl.exe para relanzar la app.",
            _ => "Windows rechazó la solicitud de elevación."
        };

        AppLog.Warn($"Elevación no completada: {detail}");
        ShowElevationWarning(detail);
        return false;
    }

    private static void ShowElevationWarning(string detail)
    {
        MessageBox.Show(
            "FanCtrl necesita ejecutarse como administrador para leer la temperatura del CPU, " +
            $"los ventiladores y el Super I/O de la placa.\n\n{detail}",
            "FanCtrl",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void Initialize(string[] args)
    {
        AppLog.Info("FanCtrl Monitor iniciando…");

        _store = new ConfigStore();
        _config = _store.Load();

        if (_store.LastError is not null)
            AppLog.Warn($"Configuración con problemas: {_store.LastError}");

        _hardware = new HardwareService();
        _hardware.Open();

        AppLog.Info(
            $"Hardware: {_hardware.MotherboardName} / {_hardware.SuperIoName} / driver {_hardware.DriverVersion}");

        if (!_hardware.IsAdministrator)
            AppLog.Warn("La app no corre como administrador; algunos sensores pueden faltar.");

        _viewModel = new MainViewModel(_store, _config, _hardware);
        _tray = new TrayService(_viewModel, ShowMainWindow, RequestExit);

        _window = new MainWindow(_viewModel);
        _window.Closed += OnMainWindowClosed;

        _hardware.StartMonitoring(TimeSpan.FromMilliseconds(_config.RefreshIntervalMs));

        bool startHidden = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                           || _config.StartMinimized;

        if (!startHidden)
        {
            _window.Show();
        }
        else
        {
            AppLog.Info("FanCtrl iniciado minimizado en la bandeja.");
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
        AppLog.Info("FanCtrl cerrado.");
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

        MessageBox.Show(
            $"FanCtrl encontró un error y se cerrará.\n\n{e.Exception.Message}",
            "FanCtrl",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        RequestExit();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error("Excepción no controlada", e.ExceptionObject as Exception);
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
