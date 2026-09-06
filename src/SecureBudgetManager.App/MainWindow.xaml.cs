using System.Windows;
using System.Windows.Media;
using SecureBudgetManager.App.Services;
using SecureBudgetManager.App.ViewModels;
using SecureBudgetManager.App.Views;
using SecureBudgetManager.App.Windows;

namespace SecureBudgetManager.App;

public partial class MainWindow : Window
{
    private readonly CaptureVisualLocator _locator;
    private readonly ShellViewModel _shell;

    public MainWindow(ShellViewModel viewModel, CaptureVisualLocator locator)
    {
        InitializeComponent();
        _shell = viewModel;
        _locator = locator;
        DataContext = viewModel;
        SourceInitialized += OnWindowSourceInitialized;
        Loaded += OnWindowLoaded;
        _shell.PropertyChanged += OnShellChanged;
        _shell.Workspace.PropertyChanged += OnWorkspaceChanged;
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        MainWindowPlacement.FitToCurrentMonitor(this);
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        MainWindowPlacement.FitToCurrentMonitor(this);
        Dispatcher.BeginInvoke(RefreshCaptureTarget, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(ShellViewModel.CurrentScreen))
        {
            Dispatcher.BeginInvoke(RefreshCaptureTarget, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            _shell.CaptureFullPageCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnWorkspaceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(MainViewModel.CurrentViewModel))
        {
            Dispatcher.BeginInvoke(RefreshCaptureTarget, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private bool _refreshingCapture;

    private void RefreshCaptureTarget()
    {
        if (_refreshingCapture)
        {
            return;
        }

        _refreshingCapture = true;
        try
        {
            var workspaceVisible = _shell.CurrentScreen is MainViewModel;
            FrameworkElement? page = null;

            if (workspaceVisible)
            {
                var workspace = FindVisual<WorkspaceView>(this);
                page = workspace?.ActivePageVisual;
            }

            _locator.Update(workspaceVisible, page);
        }
        finally
        {
            _refreshingCapture = false;
        }
    }

    private static T? FindVisual<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindVisual<T>(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
