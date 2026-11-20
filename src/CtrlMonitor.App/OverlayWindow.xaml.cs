using System.Windows;
using System.Windows.Input;
using CtrlMonitor.App.ViewModels;

namespace CtrlMonitor.App;

/// <summary>
/// Ventana transparente y sin bordes que dibuja la barra de métricas. Nunca
/// toca el proceso del juego: es una ventana normal por encima del resto.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly MainViewModel _viewModel;

    public OverlayWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// Se dispara al soltar la barra. El administrador del overlay aplica el
    /// imán y guarda la posición.
    /// </summary>
    public event EventHandler? DragCompleted;

    private void OnRootMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.IsOverlayEditing)
            return;

        try
        {
            // DragMove bloquea hasta soltar el botón.
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // El botón se soltó antes de empezar el arrastre: no hay nada que guardar.
            return;
        }

        DragCompleted?.Invoke(this, EventArgs.Empty);
    }
}
