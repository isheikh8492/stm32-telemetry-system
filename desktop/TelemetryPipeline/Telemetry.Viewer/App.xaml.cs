using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Telemetry.IO;
using Telemetry.Viewer.Services.Channels;
using Telemetry.Viewer.Services.Dialogs;
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

            // Channel catalog — names + colors keyed by channel id, read by
            // plot views (axis labels), processors (trace colors), and
            // future pseudocolor / spectral-ribbon plots. Loads channels.json
            // from the exe directory; if missing, seeds 60 ADC defaults and
            // writes a starter file the user can edit.
            var channelsPath = Path.Combine(AppContext.BaseDirectory, "channels.json");
            ChannelCatalog.LoadFrom(channelsPath, fallbackCount: 60);

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    // ---- Services (singleton — app lifetime) ----
                    services.AddSingleton<IPortDiscovery, SerialPortDiscovery>();
                    services.AddSingleton<IDialogService, WpfDialogService>();
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
