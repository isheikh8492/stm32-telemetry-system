using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Telemetry.Viewer.Views.Worksheet;

// 4 corner Thumbs that resize the PlotItemHost.
//
// Snap policy: the dragged corner of the DATA RECT (not the host) snaps to
// grid intersections — same rule as drag-to-move and placement. Chrome
// offsets (data-rect-to-host gaps) are cached at drag start so we can
// convert between data-rect edges and host edges without disturbing the
// plot's chrome reflow during resize.
//
// Mutates Presenter.X/Y/Width/Height; ItemsControl + Canvas.Left/Top
// bindings translate that into the visual move.
internal sealed class ThumbManager
{
    private const double MinSize = 50;
    private const double Half = 3;

    private readonly PlotItemHost _host;
    private readonly Worksheet _worksheet;

    private readonly Thumb _tl, _tr, _bl, _br;

    // Drag-start state.
    private double _initialL, _initialT, _initialR, _initialB;
    private double _leftChrome, _topChrome, _rightChrome, _bottomChrome;
    private Point _dragStartCursor;

    private enum Corner { TL, TR, BL, BR }

    private ThumbManager(PlotItemHost host, Worksheet worksheet)
    {
        _host = host;
        _worksheet = worksheet;

        _tl = MakeThumb(Cursors.SizeNWSE);
        _tr = MakeThumb(Cursors.SizeNESW);
        _bl = MakeThumb(Cursors.SizeNESW);
        _br = MakeThumb(Cursors.SizeNWSE);

        WireThumb(_tl, Corner.TL);
        WireThumb(_tr, Corner.TR);
        WireThumb(_bl, Corner.BL);
        WireThumb(_br, Corner.BR);

        // Hosted in the same Grid as the plot/data/drag layers, on top of
        // everything via Z=100. The Grid layout keeps them inside the host's
        // bounds; we then position via Margin so they sit at data-rect corners.
        var grid = (Grid)host.Content;
        foreach (var t in new[] { _tl, _tr, _bl, _br })
        {
            grid.Children.Add(t);
            Panel.SetZIndex(t, 100);
            t.HorizontalAlignment = HorizontalAlignment.Left;
            t.VerticalAlignment   = VerticalAlignment.Top;
            t.Visibility = Visibility.Collapsed;
        }
    }

    public static ThumbManager Wire(PlotItemHost host, Worksheet worksheet)
    {
        var manager = new ThumbManager(host, worksheet);
        if (host.PlotItem is not null)
            host.PlotItem.DataAreaChanged += manager.OnDataAreaChanged;
        return manager;
    }

    public void SetVisible(bool visible)
    {
        var v = visible ? Visibility.Visible : Visibility.Collapsed;
        _tl.Visibility = _tr.Visibility = _bl.Visibility = _br.Visibility = v;
    }

    private static Thumb MakeThumb(Cursor cursor)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.DarkOrange);
        var template = new ControlTemplate(typeof(Thumb)) { VisualTree = border };
        return new Thumb { Width = 6, Height = 6, Cursor = cursor, Template = template };
    }

    private void WireThumb(Thumb thumb, Corner corner)
    {
        thumb.DragStarted += (_, _) =>
        {
            if (_host.Presenter is null) return;
            _initialL = _host.Presenter.X;
            _initialT = _host.Presenter.Y;
            _initialR = _initialL + _host.Presenter.Width;
            _initialB = _initialT + _host.Presenter.Height;

            var area = _host.LastDataArea;
            _leftChrome   = area.X;
            _topChrome    = area.Y;
            _rightChrome  = _host.Presenter.Width  - area.Right;
            _bottomChrome = _host.Presenter.Height - area.Bottom;

            if (FindCanvasAncestor(_host) is IInputElement canvas)
                _dragStartCursor = Mouse.GetPosition(canvas);
        };

        thumb.DragDelta += (_, _) => OnDragDelta(corner);
    }

    private void OnDragDelta(Corner corner)
    {
        if (_host.Presenter is null) return;
        if (FindCanvasAncestor(_host) is not IInputElement canvas) return;

        var cursor = Mouse.GetPosition(canvas);
        var dx = cursor.X - _dragStartCursor.X;
        var dy = cursor.Y - _dragStartCursor.Y;

        var snap = _worksheet.SnapSize;

        var l = _initialL;
        var t = _initialT;
        var r = _initialR;
        var b = _initialB;

        bool movesLeft = corner == Corner.TL || corner == Corner.BL;
        bool movesTop  = corner == Corner.TL || corner == Corner.TR;

        // Snap the DATA RECT edge, then convert back to host-edge coords by
        // adding/subtracting the cached chrome offset.
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

        _host.Presenter.X = l;
        _host.Presenter.Y = t;
        _host.Presenter.Width = newW;
        _host.Presenter.Height = newH;
    }

    private void OnDataAreaChanged(Rect dataRect)
    {
        // Park thumbs at data-rect corners. Margin offset is relative to the
        // host's TL (0,0).
        Place(_tl, dataRect.X,                  dataRect.Y);
        Place(_tr, dataRect.X + dataRect.Width, dataRect.Y);
        Place(_bl, dataRect.X,                  dataRect.Y + dataRect.Height);
        Place(_br, dataRect.X + dataRect.Width, dataRect.Y + dataRect.Height);
    }

    private static void Place(Thumb t, double x, double y)
        => t.Margin = new Thickness(x - Half, y - Half, 0, 0);

    private static IInputElement? FindCanvasAncestor(DependencyObject from)
    {
        var obj = VisualTreeHelper.GetParent(from);
        while (obj is not null)
        {
            if (obj is Canvas c) return c;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private static double SnapTo(double v, double s) => s > 0 ? Math.Round(v / s) * s : v;
}
