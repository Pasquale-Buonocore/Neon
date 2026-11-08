using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Neon
{
    internal static class MenuDismissalTests
    {
        private static void Check(bool condition, string text)
        { if (!condition) throw new InvalidOperationException("Menu dismissal: " + text); }
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
        internal static void Run()
        {
            var host = new Window { Width = 100, Height = 100, Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false };
            host.Show();
            var menu = new ContextMenu { StaysOpen = false };
            var parent = new MenuItem { Header = "Themes" };
            var child = new MenuItem { Header = "Matrix" }; parent.Items.Add(child); menu.Items.Add(parent);
            var observer = new MenuDismissal(menu);
            try
            {
                menu.IsOpen = true; Pump();
                Check(observer.Listening, "mouse observer starts only for an open menu");
                var point = parent.PointToScreen(new System.Windows.Point(8, 8));
                observer.CheckPoint(new MenuDismissal.Point { X = (int)point.X, Y = (int)point.Y });
                Check(menu.IsOpen, "menu clicks remain interactive");
                parent.IsSubmenuOpen = true; Pump();
                point = child.PointToScreen(new System.Windows.Point(8, 8));
                observer.CheckPoint(new MenuDismissal.Point { X = (int)point.X, Y = (int)point.Y });
                Check(menu.IsOpen && parent.IsSubmenuOpen, "submenu clicks do not dismiss their parent");
                observer.CheckPoint(new MenuDismissal.Point { X = -100000, Y = -100000 }); Pump();
                Check(!menu.IsOpen && !observer.Listening, "outside click closes the menu and releases its observer");
                menu.IsOpen = true; Pump();
                Check(observer.Listening, "observer restarts after reopening");
                menu.IsOpen = false; Pump();
                Check(!observer.Listening, "normal dismissal also releases the observer");
            }
            finally { menu.IsOpen = false; observer.Dispose(); host.Close(); }
        }
    }
}
