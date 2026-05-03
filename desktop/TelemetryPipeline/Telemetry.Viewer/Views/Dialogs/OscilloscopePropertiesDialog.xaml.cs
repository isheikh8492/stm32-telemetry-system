using System.Windows;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Dialogs
{
    public partial class OscilloscopePropertiesDialog : Window
    {
        private const int MaxChannelCount = 60;

        private readonly OscilloscopeSettings _settings;

        public OscilloscopePropertiesDialog(OscilloscopeSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            ChannelIdComboBox.ItemsSource = Enumerable.Range(0, MaxChannelCount).ToArray();
            ChannelIdComboBox.SelectedItem = settings.ChannelId;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelIdComboBox.SelectedItem is int channelId)
            {
                // Mutates the live settings instance — bumps Version, fires
                // PropertyChanged, and ProcessingEngine reprocesses next tick.
                _settings.ChannelId = channelId;
                DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
