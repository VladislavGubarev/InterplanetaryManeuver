using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using InterplanetaryManeuver.App.Models;

namespace InterplanetaryManeuver.App.Controls;

public sealed class Plot2DControl : FrameworkElement
{
    public Plot2DControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(
            nameof(Series),
            typeof(IEnumerable<LineSeries>),
            typeof(Plot2DControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSeriesChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(Plot2DControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty XLabelProperty =
        DependencyProperty.Register(
            nameof(XLabel),
            typeof(string),
            typeof(Plot2DControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty YLabelProperty =
        DependencyProperty.Register(
            nameof(YLabel),
            typeof(string),
            typeof(Plot2DControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EqualAspectProperty =
        DependencyProperty.Register(
            nameof(EqualAspect),
            typeof(bool),
            typeof(Plot2DControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<LineSeries>? Series
    {
        get => (IEnumerable<LineSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string XLabel
    {
        get => (string)GetValue(XLabelProperty);
        set => SetValue(XLabelProperty, value);
    }

    public string YLabel
    {
        get => (string)GetValue(YLabelProperty);
        set => SetValue(YLabelProperty, value);
    }

    public bool EqualAspect
    {
        get => (bool)GetValue(EqualAspectProperty);
        set => SetValue(EqualAspectProperty, value);
    }

    private bool _viewInitialized;
    private ViewRect _view;

    private static void OnSeriesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var plot = (Plot2DControl)d;
        plot.ResetView();
    }

    private void ResetView()
    {
        _viewInitialized = false;
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            ResetView();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var all = Series?.ToArray() ?? Array.Empty<LineSeries>();
        if (all.Length == 0) return;

        Rect plotRect = GetPlotRect();
        Point mouse = e.GetPosition(this);
        if (!plotRect.Contains(mouse)) return;

        if (!TryEnsureViewInitialized(all))
            return;

        // Прокрутка вверх приближает график, уменьшая видимое окно данных.
        double factor = e.Delta > 0 ? (1.0 / 1.18) : 1.18;

        var cursor = ScreenToData(mouse, plotRect, _view);
        _view = ZoomAbout(_view, cursor.X, cursor.Y, factor);

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        Rect bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.Transparent, null, bounds);

        // Жёстко обрезаем всё по границам панели, чтобы линии графика
        // никогда не выходили наружу.
        dc.PushClip(new RectangleGeometry(bounds, 16, 16));

        var bg = (Brush?)TryFindResource("Brush.PanelBackground") ?? new SolidColorBrush(Color.FromArgb(0x22, 0x17, 0x1C, 0x26));
        var borderBrush = (Brush?)TryFindResource("Brush.Line") ?? new SolidColorBrush(Color.FromRgb(0x27, 0x32, 0x46));
        dc.DrawRoundedRectangle(bg, new Pen(borderBrush, 1), bounds, 16, 16);

        Rect plotRect = GetPlotRect();

        DrawHeader(dc, bounds);

        var all = Series?.ToArray() ?? Array.Empty<LineSeries>();
        if (all.Length == 0 || all.All(s => s.Points.Count == 0) || plotRect.Width < 10 || plotRect.Height < 10)
        {
            DrawEmpty(dc, bounds, plotRect);
            return;
        }

        if (!TryEnsureViewInitialized(all))
        {
            DrawEmpty(dc, bounds, plotRect);
            return;
        }

        double sx = plotRect.Width / _view.Width;
        double sy = plotRect.Height / _view.Height;
        if (EqualAspect)
        {
            double s = Math.Min(sx, sy);
            sx = s;
            sy = s;
        }

        // При равном масштабе по осям центрируем область, если остаётся лишнее место.
        double usedW = _view.Width * sx;
        double usedH = _view.Height * sy;
        double ox = plotRect.Left + (plotRect.Width - usedW) / 2.0;
        double oy = plotRect.Top + (plotRect.Height - usedH) / 2.0;

        Point Map(Point p)
        {
            double x = ox + (p.X - _view.MinX) * sx;
            double y = oy + (_view.MaxY - p.Y) * sy;
            return new Point(x, y);
        }

        DrawGrid(dc, plotRect);

        // Подписи осей.
        DrawAxisLabels(dc, bounds, plotRect);

        foreach (var s in all)
        {
            if (s.Points.Count < 2) continue;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(Map(s.Points[0]), false, false);
                for (int i = 1; i < s.Points.Count; i++)
                    ctx.LineTo(Map(s.Points[i]), true, false);
            }
            geo.Freeze();

            var pen = new Pen(s.Stroke, s.Thickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        DrawLegend(dc, bounds, all);
    }

    private void DrawHeader(DrawingContext dc, Rect bounds)
    {
        if (string.IsNullOrWhiteSpace(Title)) return;
        var textBrush = (Brush?)TryFindResource("Brush.Text0") ?? Brushes.White;
        var ft = new FormattedText(
            Title,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            16,
            textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(18, 14));
    }

    private void DrawEmpty(DrawingContext dc, Rect bounds, Rect plotRect)
    {
        var textBrush = (Brush?)TryFindResource("Brush.Text2") ?? Brushes.Gray;
        var ft = new FormattedText(
            "Нет данных. Нажмите «Запуск RK-45».",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            14,
            textBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(ft, new Point(plotRect.Left + 12, plotRect.Top + 12));
    }

    private static (double minX, double maxX, double minY, double maxY) ComputeBounds(LineSeries[] series)
    {
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

        foreach (var s in series)
        {
            foreach (var p in s.Points)
            {
                if (double.IsNaN(p.X) || double.IsNaN(p.Y)) continue;
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }
        }

        if (!double.IsFinite(minX)) return (double.NaN, double.NaN, double.NaN, double.NaN);
        return (minX, maxX, minY, maxY);
    }

    private void DrawGrid(DrawingContext dc, Rect plotRect)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(0x35, 0x27, 0x32, 0x46));
        gridBrush.Freeze();
        var pen = new Pen(gridBrush, 1);
        pen.Freeze();

        int vLines = 6;
        int hLines = 5;
        for (int i = 0; i <= vLines; i++)
        {
            double x = plotRect.Left + plotRect.Width * i / vLines;
            dc.DrawLine(pen, new Point(x, plotRect.Top), new Point(x, plotRect.Bottom));
        }
        for (int i = 0; i <= hLines; i++)
        {
            double y = plotRect.Top + plotRect.Height * i / hLines;
            dc.DrawLine(pen, new Point(plotRect.Left, y), new Point(plotRect.Right, y));
        }
    }

    private void DrawAxisLabels(DrawingContext dc, Rect bounds, Rect plotRect)
    {
        var textBrush = (Brush?)TryFindResource("Brush.Text2") ?? Brushes.Gray;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (!string.IsNullOrWhiteSpace(XLabel))
        {
            var ft = new FormattedText(XLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                12, textBrush, dip);
            dc.DrawText(ft, new Point(plotRect.Left + plotRect.Width - ft.Width, bounds.Height - 24));
        }
        if (!string.IsNullOrWhiteSpace(YLabel))
        {
            var ft = new FormattedText(YLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                12, textBrush, dip);
            dc.DrawText(ft, new Point(18, plotRect.Top + 2));
        }
    }

    private void DrawLegend(DrawingContext dc, Rect bounds, LineSeries[] all)
    {
        double x = 18;
        double y = bounds.Height - 32;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var s in all)
        {
            if (string.IsNullOrWhiteSpace(s.Name)) continue;
            dc.DrawRoundedRectangle(s.Stroke, null, new Rect(x, y + 5, 10, 10), 3, 3);
            var ft = new FormattedText(
                s.Name,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                12,
                (Brush?)TryFindResource("Brush.Text1") ?? Brushes.LightGray,
                dip);
            dc.DrawText(ft, new Point(x + 14, y + 2));
            x += 14 + ft.Width + 16;
            if (x > bounds.Width - 160) break;
        }

        dc.Pop();
    }

    private Rect GetPlotRect()
    {
        double padLeft = 56;
        double padRight = 22;
        double padTop = 40;
        double padBottom = 46;
        return new Rect(
            padLeft,
            padTop,
            Math.Max(0, ActualWidth - padLeft - padRight),
            Math.Max(0, ActualHeight - padTop - padBottom));
    }

    private bool TryEnsureViewInitialized(LineSeries[] all)
    {
        if (_viewInitialized) return true;

        (double minX, double maxX, double minY, double maxY) = ComputeBounds(all);
        if (!double.IsFinite(minX) || !double.IsFinite(maxX) || !double.IsFinite(minY) || !double.IsFinite(maxY))
            return false;

        if (minX == maxX) { minX -= 1; maxX += 1; }
        if (minY == maxY) { minY -= 1; maxY += 1; }

        // Добавляем немного свободного пространства по краям.
        double mx = (maxX - minX) * 0.05;
        double my = (maxY - minY) * 0.05;
        if (mx <= 0) mx = 1;
        if (my <= 0) my = 1;

        _view = new ViewRect(minX - mx, maxX + mx, minY - my, maxY + my);
        _viewInitialized = true;
        return true;
    }

    private static Point ScreenToData(Point screen, Rect plotRect, ViewRect view)
    {
        double u = (screen.X - plotRect.Left) / Math.Max(1e-12, plotRect.Width);
        double v = (screen.Y - plotRect.Top) / Math.Max(1e-12, plotRect.Height);

        double x = view.MinX + u * view.Width;
        double y = view.MaxY - v * view.Height;
        return new Point(x, y);
    }

    private static ViewRect ZoomAbout(ViewRect view, double cx, double cy, double factor)
    {
        // Ограничиваем коэффициент масштаба, чтобы избежать численной ерунды.
        factor = Math.Clamp(factor, 0.15, 8.0);

        double newMinX = cx - (cx - view.MinX) * factor;
        double newMaxX = cx + (view.MaxX - cx) * factor;
        double newMinY = cy - (cy - view.MinY) * factor;
        double newMaxY = cy + (view.MaxY - cy) * factor;

        const double minSpan = 1e-12;
        if (newMaxX - newMinX < minSpan)
        {
            double mid = (newMinX + newMaxX) / 2.0;
            newMinX = mid - minSpan / 2.0;
            newMaxX = mid + minSpan / 2.0;
        }
        if (newMaxY - newMinY < minSpan)
        {
            double mid = (newMinY + newMaxY) / 2.0;
            newMinY = mid - minSpan / 2.0;
            newMaxY = mid + minSpan / 2.0;
        }

        return new ViewRect(newMinX, newMaxX, newMinY, newMaxY);
    }

    private readonly record struct ViewRect(double MinX, double MaxX, double MinY, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }
}
