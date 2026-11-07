using System.Windows;
using System.Windows.Controls;

namespace Neon
{
    // Only Monitor determines the natural size; the other views use its body area.
    internal sealed class TabViewport : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            if (Children.Count == 0) return new Size();
            Children[0].Measure(available);
            var size = Children[0].DesiredSize;
            for (int i = 1; i < Children.Count; i++) Children[i].Measure(size);
            return size;
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (UIElement child in Children) child.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
