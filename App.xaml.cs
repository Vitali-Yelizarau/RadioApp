using RadioApp.Data;
using Serilog;
using System;
using System.Data.Entity;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RadioApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ConfigureLogging();

            AppDomain.CurrentDomain.SetData(
                "DataDirectory",
                AppDomain.CurrentDomain.BaseDirectory
            );

            Database.SetInitializer<RadioDbContext>(null);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Log.Information("Application starting...");
            Log.Information("Application base directory: {BaseDirectory}", AppDomain.CurrentDomain.BaseDirectory);
            Log.Information("Application data directory: {DataDirectory}", AppDomain.CurrentDomain.GetData("DataDirectory"));

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application exiting with code {ExitCode}", e.ApplicationExitCode);
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        private void ConfigureLogging()
        {
            string logsDirectory = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Logs"
            );

            Directory.CreateDirectory(logsDirectory);

            string logFilePath = Path.Combine(
                logsDirectory,
                "radioapp-.txt"
            );

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Fatal(e.Exception, "Unhandled UI exception");

            MessageBox.Show(
                "Произошла непредвиденная ошибка. Подробности записаны в лог.",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;

            if (exception != null)
            {
                Log.Fatal(exception, "Unhandled application exception");
            }
            else
            {
                Log.Fatal("Unhandled application exception: {ExceptionObject}", e.ExceptionObject);
            }
        }
    }
}