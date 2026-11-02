using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Neon
{
    // Moving the containing window must not change the value under a captured thumb.
    internal sealed class DesktopSlider : Slider
    {
        private double startX, startValue, pixels;
        protected override void OnThumbDragStarted(DragStartedEventArgs e)
        {
            base.OnThumbDragStarted(e);
            var track = GetTemplateChild("PART_Track") as Track;
            var source = PresentationSource.FromVisual(this);
            double dpi = source == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            BeginScreenDrag(System.Windows.Forms.Control.MousePosition.X,
                track == null ? 0 : (track.ActualWidth - track.Thumb.ActualWidth) * dpi);
        }
        internal void BeginScreenDrag(double screenX, double trackPixels)
        { startX = screenX; startValue = Value; pixels = trackPixels; }
        internal void ContinueScreenDrag(double screenX)
        {
            if (pixels <= 0) return;
            double value = startValue + (screenX - startX) * (Maximum - Minimum) / pixels;
            if (IsSnapToTickEnabled && TickFrequency > 0)
                value = Minimum + Math.Round((value - Minimum) / TickFrequency) * TickFrequency;
            Value = Math.Max(Minimum, Math.Min(Maximum, value));
        }
        protected override void OnThumbDragDelta(DragDeltaEventArgs e)
        { ContinueScreenDrag(System.Windows.Forms.Control.MousePosition.X); e.Handled = true; }
    }
}
