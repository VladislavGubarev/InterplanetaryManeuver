using System.Globalization;
using System.Windows;
using System.Windows.Media;
using InterplanetaryManeuver.App.Models;
using PhysicsSim.Core;

namespace InterplanetaryManeuver.App.Controls;

public sealed class OrbitSceneControl : FrameworkElement
{
    private static readonly Vector[] LabelOffsets =
    [
        new Vector(20, -28),
        new Vector(20, 18),
        new Vector(-20, -28),
        new Vector(-20, 18),
        new Vector(0, -36),
        new Vector(0, 24),
    ];

    public OrbitSceneControl()
    {
        ClipToBounds = true;
    }

    public static readonly DependencyProperty SceneDataProperty =
        DependencyProperty.Register(
            nameof(SceneData),
            typeof(AnimationSceneData),
            typeof(OrbitSceneControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FrameIndexProperty =
        DependencyProperty.Register(
            nameof(FrameIndex),
            typeof(int),
            typeof(OrbitSceneControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(OrbitSceneControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public AnimationSceneData? SceneData
    {
        get => (AnimationSceneData?)GetValue(SceneDataProperty);
        set => SetValue(SceneDataProperty, value);
    }

    public int FrameIndex
    {
        get => (int)GetValue(FrameIndexProperty);
        set => SetValue(FrameIndexProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        Rect bounds = new(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.Transparent, null, bounds);
        dc.PushClip(new RectangleGeometry(bounds, 16, 16));

        Brush bg = (Brush?)TryFindResource("Brush.PanelBackground") ?? new SolidColorBrush(Color.FromArgb(0x22, 0x17, 0x1C, 0x26));
        Brush borderBrush = (Brush?)TryFindResource("Brush.Line") ?? new SolidColorBrush(Color.FromRgb(0x27, 0x32, 0x46));
        dc.DrawRoundedRectangle(bg, new Pen(borderBrush, 1), bounds, 16, 16);

        DrawHeader(dc);

        AnimationSceneData? data = SceneData;
        if (data is null || data.Positions.Length == 0 || data.BodyNames.Length == 0)
        {
            DrawEmpty(dc);
            dc.Pop();
            return;
        }

        int frame = Math.Clamp(FrameIndex, 0, data.Positions.Length - 1);
        Rect sceneRect = new(18, 44, Math.Max(0, ActualWidth - 36), Math.Max(0, ActualHeight - 62));
        if (sceneRect.Width < 20 || sceneRect.Height < 20)
        {
            dc.Pop();
            return;
        }

        (double minX, double maxX, double minY, double maxY) = ComputeBounds(data);
        if (!double.IsFinite(minX))
        {
            DrawEmpty(dc);
            dc.Pop();
            return;
        }

        double spanX = Math.Max(1e-9, maxX - minX);
        double spanY = Math.Max(1e-9, maxY - minY);
        double marginX = spanX * 0.08;
        double marginY = spanY * 0.08;
        minX -= marginX;
        maxX += marginX;
        minY -= marginY;
        maxY += marginY;

        double sx = sceneRect.Width / Math.Max(1e-12, maxX - minX);
        double sy = sceneRect.Height / Math.Max(1e-12, maxY - minY);
        double scale = Math.Min(sx, sy);
        double usedW = (maxX - minX) * scale;
        double usedH = (maxY - minY) * scale;
        double ox = sceneRect.Left + (sceneRect.Width - usedW) / 2.0;
        double oy = sceneRect.Top + (sceneRect.Height - usedH) / 2.0;

        Point Map(Vector3d p)
        {
            Vector3d center = data.Positions[frame][data.CenterBodyIndex];
            Vector3d rel = p - center;
            double xAu = rel.X / AstronomyConstants.AstronomicalUnit;
            double yAu = rel.Y / AstronomyConstants.AstronomicalUnit;
            return new Point(
                ox + (xAu - minX) * scale,
                oy + (maxY - yAu) * scale);
        }

        DrawGrid(dc, sceneRect);

        int trailStart = 0;
        for (int body = 0; body < data.BodyNames.Length; body++)
        {
            if (body >= data.Positions[frame].Length)
                break;

            DrawTrail(dc, data, body, trailStart, frame, Map);
        }

        for (int body = 0; body < data.BodyNames.Length; body++)
        {
            if (body >= data.Positions[frame].Length)
                break;

            Point current = Map(data.Positions[frame][body]);
            DrawBodyMarker(dc, data, body, current, sceneRect);
        }

        DrawLegend(dc, data);
        dc.Pop();
    }

    private static (double minX, double maxX, double minY, double maxY) ComputeBounds(AnimationSceneData data)
    {
        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;

        for (int i = 0; i < data.Positions.Length; i++)
        {
            Vector3d center = data.Positions[i][data.CenterBodyIndex];
            for (int body = 0; body < data.BodyNames.Length; body++)
            {
                Vector3d rel = data.Positions[i][body] - center;
                double xAu = rel.X / AstronomyConstants.AstronomicalUnit;
                double yAu = rel.Y / AstronomyConstants.AstronomicalUnit;
                minX = Math.Min(minX, xAu);
                maxX = Math.Max(maxX, xAu);
                minY = Math.Min(minY, yAu);
                maxY = Math.Max(maxY, yAu);
            }
        }

        if (!double.IsFinite(minX))
            return (double.NaN, double.NaN, double.NaN, double.NaN);

        if (Math.Abs(maxX - minX) < 1e-12)
        {
            minX -= 1;
            maxX += 1;
        }

        if (Math.Abs(maxY - minY) < 1e-12)
        {
            minY -= 1;
            maxY += 1;
        }

        return (minX, maxX, minY, maxY);
    }

    private void DrawHeader(DrawingContext dc)
    {
        if (string.IsNullOrWhiteSpace(Title))
            return;

        var ft = CreateText(
            Title,
            16,
            (Brush?)TryFindResource("Brush.Text0") ?? Brushes.White,
            FontWeights.SemiBold,
            "Segoe UI Variable Display");
        dc.DrawText(ft, new Point(18, 14));
    }

    private void DrawEmpty(DrawingContext dc)
    {
        var ft = CreateText(
            "Нет данных для анимации.",
            14,
            (Brush?)TryFindResource("Brush.Text2") ?? Brushes.Gray,
            FontWeights.Normal,
            "Segoe UI Variable Display");
        dc.DrawText(ft, new Point(18, 50));
    }

    private static void DrawGrid(DrawingContext dc, Rect rect)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x25, 0x27, 0x32, 0x46)), 1);
        pen.Freeze();
        for (int i = 0; i <= 5; i++)
        {
            double x = rect.Left + rect.Width * i / 5.0;
            double y = rect.Top + rect.Height * i / 5.0;
            dc.DrawLine(pen, new Point(x, rect.Top), new Point(x, rect.Bottom));
            dc.DrawLine(pen, new Point(rect.Left, y), new Point(rect.Right, y));
        }
    }

    private static void DrawTrail(DrawingContext dc, AnimationSceneData data, int body, int startFrame, int endFrame, Func<Vector3d, Point> map)
    {
        if (endFrame - startFrame < 1)
            return;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(map(data.Positions[startFrame][body]), false, false);
            for (int i = startFrame + 1; i <= endFrame; i++)
                ctx.LineTo(map(data.Positions[i][body]), true, false);
        }
        geo.Freeze();

        Brush brush = data.BodyBrushes[Math.Min(body, data.BodyBrushes.Length - 1)].Clone();
        brush.Opacity = 0.45;
        brush.Freeze();
        var pen = new Pen(brush, body == data.CenterBodyIndex ? 1.8 : 1.2)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private void DrawBodyMarker(DrawingContext dc, AnimationSceneData data, int bodyIndex, Point current, Rect sceneRect)
    {
        string bodyName = data.BodyNames[bodyIndex];
        Brush baseBrush = data.BodyBrushes[Math.Min(bodyIndex, data.BodyBrushes.Length - 1)];
        bool isCenter = bodyIndex == data.CenterBodyIndex;
        bool isSpacecraft = IsSpacecraft(bodyName);
        double radius = isCenter ? 16 : isSpacecraft ? 13 : 12;

        Brush glow = baseBrush.Clone();
        glow.Opacity = isCenter ? 0.25 : 0.18;
        dc.DrawEllipse(glow, null, current, radius + 7, radius + 7);

        var outlinePen = new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xF4, 0xF8, 0xFF)), isCenter ? 1.6 : 1.0);
        dc.DrawEllipse(baseBrush, outlinePen, current, radius, radius);
        if (isCenter)
            dc.DrawEllipse(null, new Pen(baseBrush, 1.2), current, radius + 4.5, radius + 4.5);

        string glyph = GetMarkerGlyph(bodyName);
        double glyphSize = glyph.Length > 1 ? radius : radius + 6;
        Brush glyphBrush = IsLightBrush(baseBrush)
            ? new SolidColorBrush(Color.FromArgb(0xE0, 0x0D, 0x14, 0x1C))
            : Brushes.White;
        var glyphText = CreateText(glyph, glyphSize, glyphBrush, FontWeights.SemiBold, glyph.Contains("🚀") || glyph.Contains("🪐") ? "Segoe UI Emoji" : "Segoe UI Symbol");
        dc.DrawText(glyphText, new Point(current.X - glyphText.Width / 2.0, current.Y - glyphText.Height / 2.0 - 1));

        string label = GetDisplayLabel(bodyName);
        var labelText = CreateText(label, 11.5, (Brush?)TryFindResource("Brush.Text0") ?? Brushes.White, FontWeights.Medium, "Segoe UI Variable Display");
        Vector offset = LabelOffsets[bodyIndex % LabelOffsets.Length];
        double labelX = current.X + offset.X;
        double labelY = current.Y + offset.Y;

        if (offset.X < 0)
            labelX -= labelText.Width + 18;
        else if (Math.Abs(offset.X) < 0.1)
            labelX -= (labelText.Width + 18) / 2.0;

        labelX = Math.Clamp(labelX, sceneRect.Left + 6, sceneRect.Right - labelText.Width - 18);
        labelY = Math.Clamp(labelY, sceneRect.Top + 6, sceneRect.Bottom - labelText.Height - 12);

        Rect labelRect = new(labelX, labelY, labelText.Width + 18, labelText.Height + 10);
        var labelBg = new SolidColorBrush(Color.FromArgb(0xBE, 0x0F, 0x15, 0x1E));
        var labelPen = new Pen(baseBrush, 1.0);
        dc.DrawRoundedRectangle(labelBg, labelPen, labelRect, 9, 9);

        Point anchor = new(labelRect.Left + labelRect.Width / 2.0, labelRect.Top + labelRect.Height / 2.0);
        dc.DrawLine(new Pen(baseBrush, 0.9), current, anchor);
        dc.DrawText(labelText, new Point(labelRect.Left + 9, labelRect.Top + 5));
    }

    private void DrawLegend(DrawingContext dc, AnimationSceneData data)
    {
        double x = 18;
        double y = ActualHeight - 30;
        for (int i = 0; i < data.BodyNames.Length; i++)
        {
            Brush brush = data.BodyBrushes[Math.Min(i, data.BodyBrushes.Length - 1)];
            string glyph = GetMarkerGlyph(data.BodyNames[i]);
            dc.DrawEllipse(brush, null, new Point(x + 8, y + 8), 7, 7);
            var glyphText = CreateText(glyph, 9, Brushes.Black, FontWeights.Bold, glyph.Contains("🚀") || glyph.Contains("🪐") ? "Segoe UI Emoji" : "Segoe UI Symbol");
            dc.DrawText(glyphText, new Point(x + 8 - glyphText.Width / 2.0, y + 8 - glyphText.Height / 2.0));

            var ft = CreateText(
                GetDisplayLabel(data.BodyNames[i]),
                12,
                (Brush?)TryFindResource("Brush.Text1") ?? Brushes.LightGray,
                FontWeights.Normal,
                "Segoe UI Variable Display");
            dc.DrawText(ft, new Point(x + 18, y + 1));
            x += 22 + ft.Width + 14;
            if (x > ActualWidth - 140)
                break;
        }
    }

    private FormattedText CreateText(string text, double size, Brush brush, FontWeight weight, string fontFamily)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(fontFamily), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static string GetDisplayLabel(string bodyName)
    {
        string normalized = bodyName.Trim().ToLowerInvariant();
        if (normalized.Contains("sun") || normalized.Contains("солн"))
            return "Солнце";
        if (normalized.Contains("jupiter") || normalized.Contains("юпит"))
            return "Юпитер";
        if (normalized.Contains("saturn") || normalized.Contains("сатур"))
            return "Сатурн";
        if (IsSpacecraft(bodyName))
            return "КА";

        return bodyName.Length > 14 ? bodyName[..14] : bodyName;
    }

    private static string GetMarkerGlyph(string bodyName)
    {
        string normalized = bodyName.Trim().ToLowerInvariant();
        if (normalized.Contains("sun") || normalized.Contains("солн"))
            return "☀";
        if (normalized.Contains("jupiter") || normalized.Contains("юпит"))
            return "♃";
        if (normalized.Contains("saturn") || normalized.Contains("сатур"))
            return "♄";
        if (IsSpacecraft(bodyName))
            return "🚀";

        return "🪐";
    }

    private static bool IsSpacecraft(string bodyName)
    {
        string normalized = bodyName.Trim().ToLowerInvariant();
        return normalized == "ка"
            || normalized.Contains("spacecraft")
            || normalized.Contains("аппарат")
            || normalized.Contains("probe")
            || normalized.Contains("craft");
    }

    private static bool IsLightBrush(Brush brush)
    {
        if (brush is not SolidColorBrush solid)
            return false;

        Color c = solid.Color;
        double luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        return luminance > 0.65;
    }
}
