using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Telemetry.Viewer.Views.Worksheet;

// 4 corner Thumbs that resize the PlotContainer.
//
// Snap policy: the dragged corner of the DATA RECT (not the outer
// container) snaps to grid intersections — same rule as drag-to-move
// and placement. Chrome offsets (data-rect-to-outer gaps) are cached at
// drag start so we can convert between data-rect edges and outer edges
// without disturbing the plot's chrome reflow during resize.
//
// We read the cursor directly via Mouse.GetPosition relative to the
// worksheet canvas — sidestepping WPF Thumb's e.HorizontalChange (which
// is cumulative AND auto-compensated whenever the thumb moves; fragile
// against snap rounding).
internal sealed class ThumbManager
{
    private const double MinSize = 50;
    private const double Half = 4;

    private readonly PlotContainer _container;
    private readonly Func<double> _getSnapSize;

    private readonly Thumb _tl, _tr, _bl, _br;

    private Rect _lastDataArea;

    // Drag-start state.
    private double _initialL, _initialT, _initialR, _initialB;
    private double _leftChrome, _topChrome, _rightChrome, _bottomChrome;
    private Point _dragStartCursor;

    private enum Corner { TL, TR, BL, BR }

    private ThumbManager(PlotContainer container, Func<double> getSnapSize)
    {
        _container = container;
        _getSnapSize = getSnapSize;

        _tl = MakeThumb(Cursors.SizeNWSE);
        _tr = MakeThumb(Cursors.SizeNESW);
        _bl = MakeThumb(Cursors.SizeNESW);
        _br = MakeThumb(Cursors.SizeNWSE);

        WireThumb(_tl, Corner.TL);
        WireThumb(_tr, Corner.TR);
        WireThumb(_bl, Corner.BL);
        WireThumb(_br, Corner.BR);

        foreach (var t in new[] { _tl, _tr, _bl, _br })
        {
            container.Outer.Children.Add(t);
            Panel.SetZIndex(t, 100);
        }

        ParkAtOuterCorners();
        SetVisible(false);
    }

    public static ThumbManager Wire(PlotContainer container, PlotItem view, Func<double> getSnapSize)
    {
        var manager = new ThumbManager(container, getSnapSize);
        view.DataAreaChanged += manager.OnDataAreaChanged;
        return manager;
    }

    public void Show() => SetVisible(true);
    public void Hide() => SetVisible(false);

    private void SetVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        _tl.Visibility = _tr.Visibility = _bl.Visibility = _br.Visibility = v;
    }

    private static Thumb MakeThumb(Cursor cursor)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.DarkOrange);
        var template = new ControlTemplate(typeof(Thumb)) { VisualTree = border };
        return new Thumb { Width = 8, Height = 8, Cursor = cursor, Template = template };
    }

    private void WireThumb(Thumb thumb, Corner corner)
    {
        thumb.DragStarted += (_, _) =>
        {
            _initialL = Canvas.GetLeft(_container.Outer);
            _initialT = Canvas.GetTop(_container.Outer);
            _initialR = _initialL + _container.Outer.Width;
            _initialB = _initialT + _container.Outer.Height;

            _leftChrome   = _lastDataArea.X;
            _topChrome    = _lastDataArea.Y;
            _rightChrome  = _container.Outer.Width  - _lastDataArea.Right;
            _bottomChrome = _container.Outer.Height - _lastDataArea.Bottom;

            if (_container.Outer.Parent is IInputElement worksheet)
                _dragStartCursor = Mouse.GetPosition(worksheet);
        };

        thumb.DragDelta += (_, _) => OnDragDelta(corner);
    }

    private void OnDragDelta(Corner corner)
    {
        if (_container.Outer.Parent is not IInputElement worksheet) return;

        var cursor = Mouse.GetPosition(worksheet);
        var dx = cursor.X - _dragStartCursor.X;
        var dy = cursor.Y - _dragStartCursor.Y;

        var snap = _getSnapSize();

        var l = _initialL;
        var t = _initialT;
        var r = _initialR;
        var b = _initialB;

        bool movesLeft = corner == Corner.TL || corner == Corner.BL;
        bool movesTop  = corner == Corner.TL || corner == Corner.TR;

        // Snap the DATA RECT edge, then convert back to outer-edge coords
        // by adding/subtracting the cached chrome offset.
        if (movesLeft)
        {
            var initialDataL = _initialL + _leftChrome;
            var snappedDataL = SnapTo(initialDataL + dx, snap);
            l = snappedDataL - _leftChrome;
        }
        else
        {
            var initialDataR = _initialR - _rightChrome;
            var snappedDataR = SnapTo(initialDataR + dx, snap);
            r = snappedDataR + _rightChrome;
        }

        if (movesTop)
        {
            var initialDataT = _initialT + _topChrome;
            var snappedDataT = SnapTo(initialDataT + dy, snap);
            t = snappedDataT - _topChrome;
        }
        else
        {
            var initialDataB = _initialB - _bottomChrome;
            var snappedDataB = SnapTo(initialDataB + dy, snap);
            b = snappedDataB + _bottomChrome;
        }

        var newW = r - l;
        var newH = b - t;
        if (newW < MinSize || newH < MinSize) return;

        Canvas.SetLeft(_container.Outer, l);
        Canvas.SetTop(_container.Outer,  t);
        SetSize(newW, newH);
    }

    private void OnDataAreaChanged(Rect dataRect)
    {
        _lastDataArea = dataRect;
        ParkAtDataArea(dataRect);
    }

    private void ParkAtOuterCorners()
    {
        var w = _container.Outer.Width;
        var h = _container.Outer.Height;
        Place(_tl, 0, 0);
        Place(_tr, w, 0);
        Place(_bl, 0, h);
        Place(_br, w, h);
    }

    private void ParkAtDataArea(Rect r)
    {
        Place(_tl, r.X,            r.Y);
        Place(_tr, r.X + r.Width,  r.Y);
        Place(_bl, r.X,            r.Y + r.Height);
        Place(_br, r.X + r.Width,  r.Y + r.Height);
    }

    private static void Place(Thumb t, double x, double y)
    {
        Canvas.SetLeft(t, x - Half);
        Canvas.SetTop(t,  y - Half);
    }

    private void SetSize(double w, double h)
    {
        _container.Outer.Width = w;
        _container.Outer.Height = h;
        _container.Host.Width = w;
        _container.Host.Height = h;

        if (_container.Host.Children.Count > 0 && _container.Host.Children[0] is FrameworkElement plot)
        {
            plot.Width = w;
            plot.Height = h;
        }
    }

    private static double SnapTo(double v, double s) => s > 0 ? Math.Round(v / s) * s : v;
}
