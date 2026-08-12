using System.Windows;
using System.Windows.Threading;
using CrowLink.Services;
using CrowLink.ViewModels;
using CrowLink.Views;

namespace CrowLink;

public partial class App : Application
{
    private AppHost? _host;

    public App()
    {
        if (!AppContext.TryGetSwitch("CrowLink.DisableGlobalExceptionDialogs", out var disableDialogs) || !disableDialogs)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = await AppHost.CreateAsync().ConfigureAwait(true);
            _host.Theme.Apply(_host.Settings.Current.Theme);
            var viewModel = new MainViewModel(_host);
            var window = new MainWindow(viewModel);
            MainWindow = window;
            window.Show();
            await viewModel.StartAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"CrowLink를 시작하지 못했습니다.\n\n{exception.Message}",
                "CrowLink",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(exception);
            }
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLogFatal("Unhandled UI exception", e.Exception);
        e.Handled = true;

        try
        {
            MessageBox.Show(
                $"처리하지 못한 오류가 발생하여 CrowLink를 종료합니다.\n\n{e.Exception.Message}\n\n로그: %LOCALAPPDATA%\\CrowLink\\logs\\crowlink.log",
                "CrowLink 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Shutdown(1);
        }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            TryLogFatal("Unhandled background exception", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryLogFatal("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private void TryLogFatal(string message, Exception exception)
    {
        try
        {
            _host?.Log.ErrorAsync(message, exception).GetAwaiter().GetResult();
        }
        catch (Exception logException)
        {
            System.Diagnostics.Debug.WriteLine(logException);
        }
    }
}
