using System.Globalization;
using System.Windows;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Dialogs
{
    public partial class HistogramPropertiesDialog : Window
    {
        private const int MaxChannelCount = 60;

        private readonly HistogramSettings _settings;

        public HistogramPropertiesDialog(HistogramSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            ChannelIdComboBox.ItemsSource = Enumerable.Range(0, MaxChannelCount).ToArray();
            ChannelIdComboBox.SelectedItem = settings.ChannelId;

            ParamComboBox.ItemsSource = Enum.GetValues<ParamType>();
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
            if (ChannelIdComboBox.SelectedItem is not int channelId) return;
            if (ParamComboBox.SelectedItem is not ParamType param) return;
            if (BinCountComboBox.SelectedItem is not BinCount binCount) return;
            if (!double.TryParse(MinRangeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minRange)) return;
            if (!double.TryParse(MaxRangeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxRange) || maxRange <= minRange) return;
            if (ScaleComboBox.SelectedItem is not AxisScale scale) return;
            if (scale == AxisScale.Logarithmic && minRange <= 0) return;  // log requires positive min

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

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
