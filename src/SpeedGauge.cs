using System;
using System.Windows;
using System.Windows.Media;

namespace Neon
{
    internal sealed class SpeedGauge : FrameworkElement
    {
        private double value;
        internal double Value { get { return value; } set { this.value = Math.Max(0, value); InvalidateVisual(); } }
        internal bool Upload { get; set; }
        internal Color DownloadColor = Color.FromRgb(112, 255, 167);
        internal Color UploadColor = Color.FromRgb(115, 221, 235);
        internal Color TrackColor = Color.FromRgb(52, 81, 67);
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth, height = ActualHeight;
            var center = new Point(width / 2, height * 0.88);
            double radius = Math.Min(width * 0.39, height * 0.73);
            double fraction = Math.Min(1, Math.Log10(value + 1) / Math.Log10(10001));
            var track = new Pen(new SolidColorBrush(TrackColor), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            var accent = new Pen(new SolidColorBrush(Upload ? UploadColor : DownloadColor), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            for (int i = 0; i < 120; i++)
            {
                double a = 150 + i * 2, b = a + 2;
                dc.DrawLine(i / 120.0 < fraction ? accent : track, Point(center, radius, a), Point(center, radius, b));
            }
            var needle = new Pen(accent.Brush, 2.5) { EndLineCap = PenLineCap.Round };
            dc.DrawLine(needle, center, Point(center, radius * 0.79, 150 + fraction * 240));
            dc.DrawEllipse(accent.Brush, null, center, 5, 5);
        }
        private static Point Point(Point center, double radius, double degrees)
        { double angle = degrees * Math.PI / 180; return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius); }
    }
}
