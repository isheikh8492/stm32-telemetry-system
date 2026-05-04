using System.Globalization;
using System.Windows;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Dialogs
{
    public partial class SpectralRibbonPropertiesDialog : Window
    {
        private readonly SpectralRibbonSettings _settings;

        public SpectralRibbonPropertiesDialog(SpectralRibbonSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            ChannelsListBox.ItemsSource = SelectionStrategy.AvailableChannels;
            foreach (var ch in SelectionStrategy.ChannelsForIds(settings.ChannelIds))
                ChannelsListBox.SelectedItems.Add(ch);

            ParamComboBox.ItemsSource = SelectionStrategy.AvailableParams;
            ParamComboBox.SelectedItem = settings.Param;

            BinCountComboBox.ItemsSource = Enum.GetValues<BinCount>();
            BinCountComboBox.SelectedItem = settings.BinCount;

            MinRangeBox.Text = settings.MinRange.ToString(CultureInfo.InvariantCulture);
            MaxRangeBox.Text = settings.MaxRange.ToString(CultureInfo.InvariantCulture);

            ScaleComboBox.ItemsSource = Enum.GetValues<AxisScale>();
            ScaleComboBox.SelectedItem = settings.Scale;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var ids = SelectionStrategy.IdsFromSelectedItems(ChannelsListBox.SelectedItems);
            if (ids.Count == 0) { Reject("Select at least one channel."); return; }
            if (ParamComboBox.SelectedItem    is not ParamType param) { Reject("Parameter is required."); return; }
            if (BinCountComboBox.SelectedItem is not BinCount bins)   { Reject("Bin count is required."); return; }
            if (!double.TryParse(MinRangeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var min))
            { Reject("Min range must be a number."); return; }
            if (!double.TryParse(MaxRangeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
            { Reject("Max range must be a number."); return; }
            if (max <= min) { Reject("Max range must be greater than min range."); return; }
            if (ScaleComboBox.SelectedItem is not AxisScale scale) { Reject("Scale is required."); return; }
            if (scale == AxisScale.Logarithmic && min <= 0)
            { Reject("Logarithmic scale requires a positive min range."); return; }

            _settings.ChannelIds = ids;
            _settings.Param      = param;
            _settings.BinCount   = bins;
            _settings.MinRange   = min;
            _settings.MaxRange   = max;
            _settings.Scale      = scale;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Reject(string message)
            => MessageBox.Show(this, message, "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
