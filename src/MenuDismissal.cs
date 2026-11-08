using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows;

namespace Neon
{
    // Desktop-band windows do not activate, so WPF's normal focus dismissal is insufficient.
    // Observe button-down only while a menu is open; never consume or replay the click.
    internal sealed class MenuDismissal : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRect { public int Left, Top, Right, Bottom; }
        private delegate IntPtr MouseCallback(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hook, MouseCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rectangle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        private readonly ContextMenu menu;
        private readonly MouseCallback callback;
        private readonly DependencyPropertyDescriptor openProperty;
        private readonly EventHandler openChanged;
        private bool observingOpen;
        private IntPtr hook;
        internal bool Listening { get { return hook != IntPtr.Zero; } }

        internal MenuDismissal(ContextMenu target)
        {
            menu = target; callback = MouseMessage;
            openProperty = DependencyPropertyDescriptor.FromProperty(ContextMenu.IsOpenProperty, typeof(ContextMenu));
            openChanged = delegate { if (!menu.IsOpen) Dispose(); };
            menu.Opened += delegate
            {
                Dispose(); openProperty.AddValueChanged(menu, openChanged); observingOpen = true;
                hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
            };
            menu.Closed += delegate { Dispose(); };
        }
        private IntPtr MouseMessage(int code, IntPtr message, IntPtr data)
        {
            int kind = message.ToInt32();
            if (code >= 0 && (kind == 0x201 || kind == 0x204 || kind == 0x207 || kind == 0x20B))
            {
                var point = (Point)Marshal.PtrToStructure(data, typeof(Point));
                bool popup = IsApplicationPopup(point);
                menu.Dispatcher.BeginInvoke(new Action(delegate { DismissIfOutside(popup); }));
            }
            return CallNextHookEx(hook, code, message, data);
        }
        private bool IsApplicationPopup(Point point)
        {
            foreach (PresentationSource presentation in PresentationSource.CurrentSources)
            {
                var source = presentation as HwndSource;
                if (source == null || source.RootVisual == null || source.RootVisual.GetType().Name != "PopupRoot") continue;
                WindowRect rectangle;
                if (GetWindowRect(source.Handle, out rectangle) && point.X >= rectangle.Left && point.X < rectangle.Right &&
                    point.Y >= rectangle.Top && point.Y < rectangle.Bottom) return true;
            }
            return false;
        }
        internal void DismissIfOutside(bool insidePopup)
        { if (!insidePopup && menu.IsOpen) menu.IsOpen = false; }
        internal void CheckPoint(Point point) { DismissIfOutside(IsApplicationPopup(point)); }
        public void Dispose()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            if (observingOpen) { openProperty.RemoveValueChanged(menu, openChanged); observingOpen = false; }
        }
    }
}
