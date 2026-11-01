using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Neon
{
    // Keep a top-level WPF window in the desktop band without reparenting it to Explorer.
    internal sealed class DesktopLayer : IDisposable
    {
        private const int ExStyleIndex = -20;
        private const long NoActivate = 0x08000000, ToolWindow = 0x80, AppWindow = 0x40000, Topmost = 8;
        private const uint NoSize = 1, NoMove = 2, NoZOrder = 4, NoActivation = 0x10, FrameChanged = 0x20, NoOwnerZOrder = 0x200;
        private static readonly IntPtr Bottom = new IntPtr(1);
        private readonly Window window;
        private readonly HwndSource source;
        private readonly IntPtr handle;
        private readonly uint taskbarCreated;
        private IntPtr desktop;
        private bool disposed, useShellBand = true;

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPosition
        {
            public IntPtr Window, InsertAfter;
            public int X, Y, Width, Height;
            public uint Flags;
        }
        private delegate bool EnumWindowCallback(IntPtr hwnd, IntPtr parameter);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);

        public DesktopLayer(Window owner)
        {
            window = owner; handle = new WindowInteropHelper(owner).Handle;
            source = HwndSource.FromHwnd(handle);
            if (source == null) throw new InvalidOperationException("Finestra desktop non inizializzata.");
            taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            desktop = FindDesktop();
            source.AddHook(WindowMessage);
            long styles = GetWindowLongPtr(handle, ExStyleIndex).ToInt64();
            SetWindowLongPtr(handle, ExStyleIndex, new IntPtr((styles | NoActivate | ToolWindow) & ~(AppWindow | Topmost)));
            window.StateChanged += StateChanged;
            Position(FrameChanged);
        }

        public void KeepBehind()
        {
            if (disposed || !window.IsVisible) return;
            Position(0);
        }
        private void Position(uint extraFlags)
        {
            uint flags = NoSize | NoMove | NoActivation | NoOwnerZOrder | extraFlags;
            if (SetWindowPos(handle, DesktopInsertAfter(), 0, 0, 0, 0, flags)) return;
            // A restricted desktop or a different shell may reject Explorer's band.
            useShellBand = false;
            if (!SetWindowPos(handle, Bottom, 0, 0, 0, 0, flags))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "Impossibile mantenere NEON sul desktop (" + error + ").");
            }
        }
        private IntPtr DesktopInsertAfter()
        {
            if (!useShellBand) return Bottom;
            if (!IsWindow(desktop)) desktop = FindDesktop();
            if (desktop == IntPtr.Zero) return Bottom;
            // Immediately above the desktop's icon host, below application windows.
            // If Explorer raises the desktop for Win+D, follow that same desktop band.
            IntPtr previous = GetWindow(desktop, 3); // GW_HWNDPREV
            if (previous == handle) previous = GetWindow(handle, 3);
            return previous; // zero means HWND_TOP when the desktop itself is first
        }
        private static IntPtr FindDesktop()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr candidate, IntPtr ignored) {
                var name = new StringBuilder(64); GetClassName(candidate, name, name.Capacity);
                if ((name.ToString() == "Progman" || name.ToString() == "WorkerW") && FindWindowEx(candidate, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                { found = candidate; return false; }
                return true;
            }, IntPtr.Zero);
            return found != IntPtr.Zero ? found : GetShellWindow();
        }
        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0021) // WM_MOUSEACTIVATE: deliver the click without activation
            { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            if (message == 0x0046 && lParam != IntPtr.Zero) // WM_WINDOWPOSCHANGING
            {
                var position = (WindowPosition)Marshal.PtrToStructure(lParam, typeof(WindowPosition));
                position.InsertAfter = DesktopInsertAfter();
                position.Flags = (position.Flags | NoActivation | NoOwnerZOrder) & ~NoZOrder;
                Marshal.StructureToPtr(position, lParam, false);
            }
            if (taskbarCreated != 0 && (uint)message == taskbarCreated)
            {
                desktop = IntPtr.Zero; useShellBand = true;
                window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(KeepBehind));
            }
            return IntPtr.Zero;
        }
        private void StateChanged(object sender, EventArgs args)
        {
            if (disposed || !window.IsVisible || window.WindowState != WindowState.Minimized) return;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate {
                if (disposed || !window.IsVisible || window.WindowState != WindowState.Minimized) return;
                window.ShowActivated = false; window.WindowState = WindowState.Normal; KeepBehind();
            }));
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; window.StateChanged -= StateChanged;
            if (!source.IsDisposed) source.RemoveHook(WindowMessage);
        }
    }
}
