using System.Globalization;
using System.Windows;
using Telemetry.Viewer.Models;
using Telemetry.Viewer.Models.Plots;

namespace Telemetry.Viewer.Views.Dialogs
{
    public partial class PseudocolorPropertiesDialog : Window
    {
        private readonly PseudocolorSettings _settings;

        public PseudocolorPropertiesDialog(PseudocolorSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            // X axis
            HookChannelCombo(XChannelComboBox, settings.XChannelId);
            HookParamCombo(XParamComboBox, settings.XParam);
            XMinRangeBox.Text = settings.XMinRange.ToString(CultureInfo.InvariantCulture);
            XMaxRangeBox.Text = settings.XMaxRange.ToString(CultureInfo.InvariantCulture);
            HookScaleCombo(XScaleComboBox, settings.XScale);

            // Y axis
            HookChannelCombo(YChannelComboBox, settings.YChannelId);
            HookParamCombo(YParamComboBox, settings.YParam);
            YMinRangeBox.Text = settings.YMinRange.ToString(CultureInfo.InvariantCulture);
            YMaxRangeBox.Text = settings.YMaxRange.ToString(CultureInfo.InvariantCulture);
            HookScaleCombo(YScaleComboBox, settings.YScale);

            // Single bin count — applied to both axes.
            HookBinCountCombo(BinCountComboBox, settings.BinCount);
        }

        private static void HookChannelCombo(System.Windows.Controls.ComboBox cb, int currentId)
        {
            cb.ItemsSource       = SelectionStrategy.AvailableChannels;
            cb.DisplayMemberPath = SelectionStrategy.ChannelDisplayPath;
            cb.SelectedValuePath = SelectionStrategy.ChannelValuePath;
            cb.SelectedValue     = currentId;
        }

        private static void HookParamCombo(System.Windows.Controls.ComboBox cb, ParamType current)
        {
            cb.ItemsSource = SelectionStrategy.AvailableParams;
            cb.SelectedItem = current;
        }

        private static void HookBinCountCombo(System.Windows.Controls.ComboBox cb, BinCount current)
        {
            cb.ItemsSource = Enum.GetValues<BinCount>();
            cb.SelectedItem = current;
        }

        private static void HookScaleCombo(System.Windows.Controls.ComboBox cb, AxisScale current)
        {
            cb.ItemsSource = Enum.GetValues<AxisScale>();
            cb.SelectedItem = current;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (XChannelComboBox.SelectedValue is not int xChannel) { Reject("X channel is required."); return; }
            if (XParamComboBox.SelectedItem    is not ParamType xParam) { Reject("X parameter is required."); return; }
            if (!TryParseDouble(XMinRangeBox.Text, out var xMin)) { Reject("X min range must be a number."); return; }
            if (!TryParseDouble(XMaxRangeBox.Text, out var xMax)) { Reject("X max range must be a number."); return; }
            if (xMax <= xMin) { Reject("X max range must be greater than X min range."); return; }
            if (XScaleComboBox.SelectedItem is not AxisScale xScale) { Reject("X scale is required."); return; }
            if (xScale == AxisScale.Logarithmic && xMin <= 0) { Reject("X logarithmic scale requires a positive min."); return; }

            if (YChannelComboBox.SelectedValue is not int yChannel) { Reject("Y channel is required."); return; }
            if (YParamComboBox.SelectedItem    is not ParamType yParam) { Reject("Y parameter is required."); return; }
            if (!TryParseDouble(YMinRangeBox.Text, out var yMin)) { Reject("Y min range must be a number."); return; }
            if (!TryParseDouble(YMaxRangeBox.Text, out var yMax)) { Reject("Y max range must be a number."); return; }
            if (yMax <= yMin) { Reject("Y max range must be greater than Y min range."); return; }
            if (YScaleComboBox.SelectedItem is not AxisScale yScale) { Reject("Y scale is required."); return; }
            if (yScale == AxisScale.Logarithmic && yMin <= 0) { Reject("Y logarithmic scale requires a positive min."); return; }

            if (BinCountComboBox.SelectedItem is not BinCount bins) { Reject("Bin count is required."); return; }

            // Mutates the live settings instance — bumps Version, ProcessingEngine
            // sees the new fingerprint next tick and reprocesses.
            _settings.XChannelId = xChannel; _settings.XParam = xParam;
            _settings.XMinRange = xMin; _settings.XMaxRange = xMax; _settings.XScale = xScale;
            _settings.YChannelId = yChannel; _settings.YParam = yParam;
            _settings.YMinRange = yMin; _settings.YMaxRange = yMax; _settings.YScale = yScale;
            _settings.BinCount  = bins;

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Reject(string message)
            => MessageBox.Show(this, message, "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);

        private static bool TryParseDouble(string s, out double value)
            => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
