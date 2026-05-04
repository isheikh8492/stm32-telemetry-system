using System.Windows;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Dialogs
{
    public partial class OscilloscopePropertiesDialog : Window
    {
        private readonly OscilloscopeSettings _settings;

        public OscilloscopePropertiesDialog(OscilloscopeSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            ChannelIdComboBox.ItemsSource       = SelectionStrategy.AvailableChannels;
            ChannelIdComboBox.DisplayMemberPath = SelectionStrategy.ChannelDisplayPath;
            ChannelIdComboBox.SelectedValuePath = SelectionStrategy.ChannelValuePath;
            ChannelIdComboBox.SelectedValue     = settings.ChannelId;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelIdComboBox.SelectedValue is not int channelId)
            {
                MessageBox.Show(this, "Channel is required.", "Invalid input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Mutates the live settings instance — bumps Version, fires
            // PropertyChanged, and ProcessingEngine reprocesses next tick.
            _settings.ChannelId = channelId;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
