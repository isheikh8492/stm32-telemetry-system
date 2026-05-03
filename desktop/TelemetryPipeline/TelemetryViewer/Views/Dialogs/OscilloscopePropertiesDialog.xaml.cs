using System.Windows;

namespace TelemetryViewer.Views.Dialogs
{
    public partial class OscilloscopePropertiesDialog : Window
    {
        private const int MaxChannelCount = 60;

        public OscilloscopeSettings UpdatedSettings { get; private set; }

        public OscilloscopePropertiesDialog(OscilloscopeSettings current)
        {
            InitializeComponent();
            UpdatedSettings = current;

            ChannelIdComboBox.ItemsSource = Enumerable.Range(0, MaxChannelCount).ToArray();
            ChannelIdComboBox.SelectedItem = current.ChannelId;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (ChannelIdComboBox.SelectedItem is int channelId)
            {
                UpdatedSettings = UpdatedSettings with { ChannelId = channelId };
                DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
