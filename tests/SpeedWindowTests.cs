using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Neon
{
    internal static class SpeedWindowTests
    {
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Speed window: " + message); }
        private static void Open(MonitorWindow monitor, string method)
        { typeof(MonitorWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(monitor, null); }

        internal static void Run()
        {
            var monitor = new MonitorWindow(true) { Left = -32000, Top = -32000, ShowActivated = false };
            monitor.Show();
            try
            {
                monitor.SetTheme("ice");
                Open(monitor, "OpenSpeedTest");
                var speed = Application.Current.Windows.OfType<SpeedTestWindow>().Single(w => w.IsVisible);
                Check(Math.Abs(speed.Width - monitor.ActualWidth) < 1 && Math.Abs(speed.Height - monitor.ActualHeight) < 1 && speed.WindowStyle == WindowStyle.None &&
                    speed.AllowsTransparency && !speed.ShowInTaskbar && !speed.ShowActivated,
                    "companion size, frame, and taskbar state: " + speed.Width + "x" + speed.Height +
                    ", style=" + speed.WindowStyle + ", transparency=" + speed.AllowsTransparency +
                    ", taskbar=" + speed.ShowInTaskbar + ", activated=" + speed.ShowActivated);
                var frame = (Border)speed.Visual;
                Check(((SolidColorBrush)frame.BorderBrush).Color == (Color)ColorConverter.ConvertFromString(ThemePalette.Find("ice").Line) &&
                    ((SolidColorBrush)frame.Background).Color.A == (byte)(255 * monitor.CurrentPreferences.Background),
                    "companion inherits theme and opacity");
                var smaller = monitor.CurrentPreferences; smaller.WidgetScale = 0.8;
                monitor.ApplyUserSettings(smaller, false); monitor.UpdateLayout();
                Check(Math.Abs(speed.Width - monitor.ActualWidth) < 1 && Math.Abs(speed.Height - monitor.ActualHeight) < 1,
                    "companion follows widget resize");
                Open(monitor, "OpenSpeedTest");
                Check(Application.Current.Windows.OfType<SpeedTestWindow>().Count(w => w.IsVisible) == 1, "only one speed window opens");
                Open(monitor, "OpenSettings");
                Check(!speed.IsVisible && Application.Current.Windows.OfType<SettingsWindow>().Count(w => w.IsVisible) == 1,
                    "Personalize replaces Speed Test");
                Open(monitor, "OpenSpeedTest");
                Check(Application.Current.Windows.OfType<SettingsWindow>().All(w => !w.IsVisible) &&
                    Application.Current.Windows.OfType<SpeedTestWindow>().Count(w => w.IsVisible) == 1,
                    "Speed Test replaces Personalize");
            }
            finally
            {
                foreach (var companion in Application.Current.Windows.OfType<SpeedTestWindow>().Where(w => w.IsVisible).ToArray()) companion.Close();
                monitor.Close();
            }

            bool canceled = false;
            var held = new SpeedTestWindow(new Preferences(), (progress, token) =>
            {
                var pending = new TaskCompletionSource<SpeedTestResult>();
                token.Register(() => { canceled = true; pending.TrySetCanceled(); });
                return pending.Task;
            }) { Left = -32000, Top = -32000 };
            held.Show(); held.RunTest();
            Check(held.IsTesting, "a started test is active");
            held.Close();
            Check(canceled, "closing the companion cancels its active test");
            Check(SpeedTestWindow.FailureMessage(new System.Net.WebException("DNS", System.Net.WebExceptionStatus.NameResolutionFailure), "finding", false)
                .Contains("No measurement was obtained"), "discovery DNS failure reports no measurement");
            Check(SpeedTestWindow.FailureMessage(new System.Net.WebException("offline", System.Net.WebExceptionStatus.ConnectFailure), "finding", false)
                .Contains("No measurement was obtained"), "offline discovery reports no measurement");
            Check(SpeedTestWindow.FailureMessage(new TimeoutException(), "finding", false)
                .Contains("No measurement was obtained"), "discovery timeout reports no measurement");
        }
    }
}
