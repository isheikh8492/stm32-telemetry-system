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

            // Display path is set in XAML (CheckBox.Content="{Binding Name}")
            // because DisplayMemberPath is ignored when the ItemContainer
            // template is overridden.
            ChannelsListBox.ItemsSource = SelectionStrategy.AvailableChannels;

            // Pre-select the channels currently in settings.
            foreach (var ch in SelectionStrategy.ChannelsForIds(settings.ChannelIds))
                ChannelsListBox.SelectedItems.Add(ch);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var ids = SelectionStrategy.IdsFromSelectedItems(ChannelsListBox.SelectedItems);
            if (ids.Count == 0)
            {
                MessageBox.Show(this, "Select at least one channel.", "Invalid input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settings.ChannelIds = ids;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
