using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Neon
{
    internal static class DesktopLayerTests
    {
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        private static void Assert(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Desktop: " + description);
        }
        private static bool Above(IntPtr upper, IntPtr lower)
        {
            IntPtr current = lower;
            for (int i = 0; i < 10000 && current != IntPtr.Zero; i++)
            {
                current = GetWindow(current, 3);
                if (current == upper) return true;
            }
            return false;
        }
        private static Window OtherWindow()
        {
            return new Window { Title = "NEON test fixture", ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Width = 80, Height = 60, Left = -32000, Top = -32000 };
        }
        private static void Pump(Window window)
        {
            window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { }));
            window.UpdateLayout();
        }
        public static string Run()
        {
            var first = OtherWindow(); var second = OtherWindow(); var monitor = new MonitorWindow(true);
            try
            {
                first.Show(); second.Show();
                monitor.Left = -32000; monitor.Top = -32000; monitor.Show(); Pump(monitor);
                IntPtr hwnd = new WindowInteropHelper(monitor).Handle;
                IntPtr firstHandle = new WindowInteropHelper(first).Handle, secondHandle = new WindowInteropHelper(second).Handle;
                long styles = GetWindowLongPtr(hwnd, -20).ToInt64();
                Assert((styles & 0x08000000) != 0 && (styles & 8) == 0, "native no-activation and non-topmost styles");
                Assert(Above(firstHandle, hwnd) && Above(secondHandle, hwnd), "placement behind both other windows");
                IntPtr desktop = GetShellWindow();
                if (desktop != IntPtr.Zero && IsWindowVisible(desktop)) Assert(Above(hwnd, desktop), "widget visible above the Explorer desktop");
                Assert(SendMessage(hwnd, 0x0021, IntPtr.Zero, IntPtr.Zero) == new IntPtr(3), "clicks delivered without activation");
                Assert(SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 1 | 2 | 0x10), "simulated foreground request");
                Pump(monitor);
                Assert((GetWindowLongPtr(hwnd, -20).ToInt64() & 8) == 0 && Above(firstHandle, hwnd), "topmost request blocked");
                second.WindowState = WindowState.Minimized; Pump(monitor);
                Assert(Above(firstHandle, hwnd) && GetForegroundWindow() != hwnd, "minimizing another window without foregrounding NEON");
                monitor.Hide(); monitor.Reveal(); Pump(monitor);
                Assert(monitor.IsVisible && Above(firstHandle, hwnd) && GetForegroundWindow() != hwnd, "showing from the tray without foreground focus");
                monitor.Left += 20; Pump(monitor);
                Assert(Above(firstHandle, hwnd), "moving without raising the widget");
                monitor.WindowState = WindowState.Minimized; Pump(monitor);
                Assert(monitor.WindowState == WindowState.Normal && Above(firstHandle, hwnd), "restoring to the desktop layer after minimization");
                return "Desktop OK: Win32 styles, placement behind two windows, clicks without activation, blocked topmost requests, minimizing another app, showing, moving and restoring the widget.";
            }
            finally { monitor.Close(); second.Close(); first.Close(); }
        }
    }
}
