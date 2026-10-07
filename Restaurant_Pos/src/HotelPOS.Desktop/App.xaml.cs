using System.Windows;
using System.Windows.Threading;
using HotelPOS.Desktop.Services.Ui;
using HotelPOS.Desktop.Shell;
using HotelPOS.Desktop.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace HotelPOS.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = AppHost.Build(e.Args);
        RegisterGlobalExceptionHandlers();

        try
        {
            await _host.StartAsync();
            var services = _host.Services;
            services.GetRequiredService<ThemeService>().ApplySaved();

            var window = new MainWindow { DataContext = services.GetRequiredService<MainViewModel>() };
            MainWindow = window;
            window.Show();

            await services.GetRequiredService<StartupFlow>().RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "HotelPOS failed to start");
            MessageBox.Show($"HotelPOS could not start.\n\n{ex.Message}\n\nDetails are in the log folder.", "HotelPOS",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Stop off the UI thread so hub shutdown cannot deadlock on the dispatcher.
            var host = _host;
            Task.Run(async () =>
            {
                await host.StopAsync(TimeSpan.FromSeconds(3));
                await ((IAsyncDisposable)host).DisposeAsync();
            }).Wait(TimeSpan.FromSeconds(5));
        }

        Log.Information("HotelPOS desktop closed");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", args.IsTerminating);
    }

    // A bug in one screen must not close the terminal in the middle of service.
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        e.Handled = true;
        try
        {
            _host?.Services.GetService<INotificationService>()?.Error("Something went wrong. The problem was logged; please try again.");
        }
        catch (Exception ex)
        {
            _host?.Services.GetService<ILogger<App>>()?.LogError(ex, "Could not show the error notification");
        }
    }
}
