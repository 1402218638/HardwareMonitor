using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace HardwareMonitor.Controls;

/// <summary>
/// 轻量迷你趋势图（无第三方依赖）：Canvas 上绘制折线 + 半透明面积。
/// </summary>
public sealed class Sparkline : Canvas
{
    private IReadOnlyList<double> _values = Array.Empty<double>();

    /// <summary>线条画笔，可在 XAML 中以 "#RRGGBB" 设置。</summary>
    public Brush? SparkStroke { get; set; }

    /// <summary>为 true 时纵轴最小值固定为 0（适合功率等非负数据）。</summary>
    public bool ScaleFromZero { get; set; }

    public Sparkline()
    {
        SizeChanged += (_, _) => Render();
    }

    public void SetData(IReadOnlyList<double> values)
    {
        _values = values ?? Array.Empty<double>();
        Render();
    }

    private void Render()
    {
        Children.Clear();

        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 4 || height <= 4 || _values.Count < 2)
            return;

        double min = double.MaxValue, max = double.MinValue;
        foreach (var raw in _values)
        {
            if (double.IsNaN(raw))
                continue;
            min = Math.Min(min, raw);
            max = Math.Max(max, raw);
        }

        if (min > max)
            return;
        if (ScaleFromZero)
            min = Math.Min(0, min);
        if (max - min < 1e-6)
            max = min + 1;

        const double pad = 3;
        var stroke = SparkStroke ?? new SolidColorBrush(Microsoft.UI.Colors.White);
        var count = _values.Count;
        var points = new List<Point>();

        for (var i = 0; i < count; i++)
        {
            var value = _values[i];
            if (double.IsNaN(value))
                continue;

            var x = width * i / (count - 1);
            var y = height - pad - (value - min) / (max - min) * (height - 2 * pad);
            points.Add(new Point(x, y));
        }

        if (points.Count == 0)
            return;

        // 半透明面积
        var areaPoints = new PointCollection();
        foreach (var p in points)
            areaPoints.Add(p);
        areaPoints.Add(new Point(points[^1].X, height));
        areaPoints.Add(new Point(points[0].X, height));

        Children.Add(new Polygon
        {
            Fill = stroke,
            Opacity = 0.16,
            StrokeThickness = 0,
            Points = areaPoints,
        });

        // 折线
        var linePoints = new PointCollection();
        foreach (var p in points)
            linePoints.Add(p);

        Children.Add(new Polyline
        {
            Stroke = stroke,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Points = linePoints,
        });
    }
}
