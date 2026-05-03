using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telemetry.Viewer.Services.Pipeline;
using Telemetry.Viewer.ViewModels;
using Telemetry.Viewer.Views;

namespace Telemetry.Viewer
{
    /// <summary>
    /// Composition root. Builds the DI container, registers services + view models +
    /// views, resolves the root window, and shows it.
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    // ---- Services (singleton — app lifetime) ----
                    services.AddSingleton<IPipelineFactory, PipelineFactory>();

                    // ---- ViewModels (singleton — there's only one main window) ----
                    services.AddSingleton<MainWindowViewModel>();

                    // ---- Views (singleton — DI walks the constructor and supplies the VM) ----
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_host is not null)
            {
                // Dispose VM (which tears down any active pipeline session) before the host.
                _host.Services.GetService<MainWindowViewModel>()?.Dispose();
                _host.Dispose();
            }
            base.OnExit(e);
        }
    }
}
