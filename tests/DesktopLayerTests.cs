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
                Assert((styles & 0x08000000) != 0 && (styles & 8) == 0, "stili nativi di non attivazione e non topmost");
                Assert(Above(firstHandle, hwnd) && Above(secondHandle, hwnd), "posizione dietro a entrambe le altre finestre");
                IntPtr desktop = GetShellWindow();
                if (desktop != IntPtr.Zero && IsWindowVisible(desktop)) Assert(Above(hwnd, desktop), "widget visibile sopra il desktop di Explorer");
                Assert(SendMessage(hwnd, 0x0021, IntPtr.Zero, IntPtr.Zero) == new IntPtr(3), "click senza attivazione, con consegna degli eventi");
                Assert(SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 1 | 2 | 0x10), "simulazione richiesta di primo piano");
                Pump(monitor);
                Assert((GetWindowLongPtr(hwnd, -20).ToInt64() & 8) == 0 && Above(firstHandle, hwnd), "richiesta topmost bloccata");
                second.WindowState = WindowState.Minimized; Pump(monitor);
                Assert(Above(firstHandle, hwnd) && GetForegroundWindow() != hwnd, "minimizzazione di un'altra finestra senza foreground NEON");
                monitor.Hide(); monitor.Reveal(); Pump(monitor);
                Assert(monitor.IsVisible && Above(firstHandle, hwnd) && GetForegroundWindow() != hwnd, "richiamo dall'icona senza primo piano");
                monitor.Left += 20; Pump(monitor);
                Assert(Above(firstHandle, hwnd), "spostamento senza alzare il widget");
                monitor.WindowState = WindowState.Minimized; Pump(monitor);
                Assert(monitor.WindowState == WindowState.Normal && Above(firstHandle, hwnd), "ripristino al livello desktop dopo minimizzazione");
                return "Desktop OK: stili Win32, ordine dietro a due finestre, click senza attivazione, blocco topmost, minimizzazione altra app, richiamo, spostamento e ripristino.";
            }
            finally { monitor.Close(); second.Close(); first.Close(); }
        }
    }
}
