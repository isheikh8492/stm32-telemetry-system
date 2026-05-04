using System.Globalization;
using System.Windows;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Dialogs
{
    public partial class HistogramPropertiesDialog : Window
    {
        private readonly HistogramSettings _settings;

        public HistogramPropertiesDialog(HistogramSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            ChannelIdComboBox.ItemsSource       = SelectionStrategy.AvailableChannels;
            ChannelIdComboBox.DisplayMemberPath = SelectionStrategy.ChannelDisplayPath;
            ChannelIdComboBox.SelectedValuePath = SelectionStrategy.ChannelValuePath;
            ChannelIdComboBox.SelectedValue     = settings.ChannelId;

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
            if (ChannelIdComboBox.SelectedValue is not int channelId) { Reject("Channel is required."); return; }
            if (ParamComboBox.SelectedItem is not ParamType param)  { Reject("Parameter is required."); return; }
            if (BinCountComboBox.SelectedItem is not BinCount binCount) { Reject("Bin count is required."); return; }
            if (!double.TryParse(MinRangeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minRange))
            { Reject("Min range must be a number."); return; }
            if (!double.TryParse(MaxRangeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxRange))
            { Reject("Max range must be a number."); return; }
            if (maxRange <= minRange) { Reject("Max range must be greater than min range."); return; }
            if (ScaleComboBox.SelectedItem is not AxisScale scale) { Reject("Scale is required."); return; }
            if (scale == AxisScale.Logarithmic && minRange <= 0)
            { Reject("Logarithmic scale requires a positive min range."); return; }

            // Mutates the live settings instance — bumps Version, ProcessingEngine
            // sees the new fingerprint next tick and reprocesses.
            _settings.ChannelId = channelId;
            _settings.Param     = param;
            _settings.BinCount  = binCount;
            _settings.MinRange  = minRange;
            _settings.MaxRange  = maxRange;
            _settings.Scale     = scale;

            DialogResult = true;
        }

        private void Reject(string message)
            => MessageBox.Show(this, message, "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
