using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Neon
{
    internal static class SettingsTests
    {
        private static void Assert(bool value, string description)
        { if (!value) throw new InvalidOperationException("Settings: " + description); }
        private static TextBlock FindBrand(DependencyObject root)
        {
            var text = root as TextBlock;
            if (text != null && text.Text == "●  NEON / SYS") return text;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var child = FindBrand(VisualTreeHelper.GetChild(root, i)); if (child != null) return child; }
            return null;
        }
        private static T FindControl<T>(DependencyObject root) where T : DependencyObject
        {
            var match = root as T; if (match != null) return match;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var child = FindControl<T>(VisualTreeHelper.GetChild(root, i)); if (child != null) return child; }
            return null;
        }
        private static System.Collections.Generic.IEnumerable<Button> FindButtons(DependencyObject root)
        {
            var button = root as Button; if (button != null) yield return button;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in FindButtons(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        public static string Run(string reportPath)
        {
            string parent = Path.GetFullPath(Path.GetDirectoryName(reportPath));
            string temporary = Path.GetFullPath(Path.Combine(parent, "settings-test-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(temporary);
            try
            {
                string path = Path.Combine(temporary, "settings.json");
                var missing = PreferencesStore.Load(path);
                Assert(missing.WidgetScale == 1 && missing.FontScale == 1 && missing.RefreshSeconds == 1, "missing file defaults");
                File.WriteAllText(path, "{\"Left\":120,\"Top\":30,\"Background\":0,\"Theme\":\"ice\",\"Compact\":true,\"DockToCorner\":false,\"Pinned\":true}");
                var legacy = PreferencesStore.Load(path);
                Assert(legacy.Left == 120 && legacy.Top == 30 && legacy.Background == 0 && legacy.Theme == "ice" && !legacy.DockToCorner, "legacy settings preserved without compact mode");
                Assert(legacy.WidgetScale == 1 && legacy.FontScale == 1 && legacy.RefreshSeconds == 1, "new fields migrate to current defaults");
                File.WriteAllText(path, "invalid json"); Assert(PreferencesStore.Load(path).Theme == "matrix", "malformed settings fallback");
                File.WriteAllText(path, "null"); Assert(PreferencesStore.Load(path).Background == 0.6, "null settings fallback");
                var invalid = new Preferences { Background = Double.NaN, FontScale = Double.PositiveInfinity, WidgetScale = -3, RefreshSeconds = 999, Theme = "unknown" }; invalid.Normalize();
                Assert(invalid.Background == 0.6 && invalid.FontScale == 1 && invalid.WidgetScale == 0.6 && invalid.RefreshSeconds == 10 && invalid.Theme == "matrix", "invalid values normalized");
                var wanted = new Preferences { WidgetScale = 1.2, FontScale = 1.35, RefreshSeconds = 2.5, Theme = "ghost", Background = 0.35, Left = -120, Top = 20, DockToCorner = false };
                Assert(PreferencesStore.Save(path, wanted), "save over a corrupt file");
                var restored = PreferencesStore.Load(path);
                Assert(restored.WidgetScale == 1.2 && restored.FontScale == 1.35 && restored.RefreshSeconds == 2.5 && restored.Background == 0.35 && restored.Theme == "ghost" && restored.Left == -120 && !restored.DockToCorner, "all preferences survive reload");
                Assert(!File.ReadAllText(path).Contains("\"Compact\""), "obsolete compact setting is not saved");
                wanted.RefreshSeconds = 5; Assert(PreferencesStore.Save(path, wanted) && PreferencesStore.Load(path).RefreshSeconds == 5, "atomic replacement");
                Assert(!PreferencesStore.Save(temporary, wanted), "unwritable destination reported without crashing");
                Assert(Directory.GetFiles(temporary, "*.tmp").Length == 0, "temporary writes cleaned up");
                VerifyLiveApplication();
                StartupTests.Run();
                return "Settings OK: defaults, legacy migration, corrupt/null settings, validation, atomic save/reload, save failure, live application, independent typography, interval, docking, single display mode, shared controls, personalization menu, immediate placement under mouse capture, stable screen-coordinate slider dragging, and Windows startup registration, approval, Save/Cancel/Reset and rollback.";
            }
            finally
            {
                if (temporary.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(temporary, true);
            }
        }
        private static void VerifyLiveApplication()
        {
            var monitor = new MonitorWindow(true); SettingsWindow settings = null;
            try
            {
                monitor.Left = monitor.Top = -32000; monitor.Show(); monitor.ApplyUserSettings(new Preferences()); monitor.UpdateLayout();
                double initialWidth = monitor.ActualWidth, initialFont = FindBrand(monitor.Visual).FontSize;
                var changed = monitor.CurrentPreferences; changed.FontScale = 1.2; changed.RefreshSeconds = 2.5; changed.Theme = "ice"; changed.Background = 0.35;
                Assert(monitor.ApplyUserSettings(changed), "apply settings callback"); monitor.UpdateLayout();
                Assert(FindBrand(monitor.Visual).FontSize > initialFont && Math.Abs(monitor.ActualWidth - initialWidth) <= 1, "font size independent of widget width");
                Assert(monitor.RefreshInterval == 2.5 && monitor.CurrentPreferences.Theme == "ice" && monitor.CurrentPreferences.Background == 0.35, "live interval, theme and opacity");
                changed.FontScale = 1; changed.WidgetScale = 1.2; monitor.ApplyUserSettings(changed); monitor.UpdateLayout();
                Assert(monitor.ActualWidth > initialWidth && monitor.ActualHeight <= SystemParameters.WorkArea.Height + 1, "live widget resize fits screen");
                Assert(Math.Abs(monitor.Left + monitor.ActualWidth - SystemParameters.WorkArea.Right) <= 1 && Math.Abs(monitor.Top - SystemParameters.WorkArea.Top) <= 1, "right corner anchor preserved");
                Assert(!monitor.Topmost && !monitor.ShowActivated, "settings do not elevate the widget");
                settings = new SettingsWindow(monitor.CurrentPreferences, monitor.ApplyUserSettings);
                settings.VerifyControls(); var applied = monitor.CurrentPreferences;
                Assert(applied.WidgetScale == 0.85 && applied.FontScale == 1.2 && applied.Background == 0.35 && applied.RefreshSeconds == 2 && applied.Theme == "ice", "all controls apply their values");
                settings.Close(); settings = new SettingsWindow(monitor.CurrentPreferences, delegate { return false; }); settings.VerifySaveFailure();
                var entry = ((Border)((Viewbox)monitor.Visual).Child).ContextMenu.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Tag) == "settings");
                Assert(Application.Current.Windows.OfType<SettingsWindow>().Count(w => w.IsVisible) == 0, "settings do not open at startup");
                entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var firstSettings = Application.Current.Windows.OfType<SettingsWindow>().Single(w => w.IsVisible);
                entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert(Application.Current.Windows.OfType<SettingsWindow>().Count(w => w.IsVisible) == 1 && firstSettings.IsVisible, "settings menu opens one reusable window");
                Assert(!monitor.Topmost && !monitor.ShowActivated && !firstSettings.Topmost && !firstSettings.ShowActivated && !firstSettings.ShowInTaskbar, "personalization remains on the desktop without a taskbar entry");
                Assert(Convert.ToString(entry.Header) == "Personalize…", "personalization menu label");
                firstSettings.UpdateLayout();
                var sizeSlider = FindControl<DesktopSlider>(firstSettings.Visual);
                Assert(sizeSlider != null && Mouse.Capture(sizeSlider), "capture size slider during live resize");
                try
                {
                    sizeSlider.Value = 150;
                    Assert(Mouse.Captured == sizeSlider, "capture retained while resizing");
                    Assert(Math.Abs(firstSettings.Left + firstSettings.Width + 8 - monitor.Left) <= 1, "personalization follows resize before mouse release");
                    sizeSlider.BeginScreenDrag(500, 180);
                    sizeSlider.ContinueScreenDrag(470);
                    double firstValue = sizeSlider.Value;
                    Assert(firstValue == 135, "slider uses physical cursor displacement");
                    sizeSlider.ContinueScreenDrag(470);
                    Assert(sizeSlider.Value == firstValue, "moving the panel without moving the cursor cannot change the slider");
                    Assert(Math.Abs(firstSettings.Left + firstSettings.Width + 8 - monitor.Left) <= 1, "panel stays adjacent throughout the drag");
                }
                finally { Mouse.Capture(null); }
                var widgetClose = FindButtons(monitor.Visual).First(b => Convert.ToString(b.Content) == "×");
                var panelClose = FindButtons(firstSettings.Visual).First(b => Convert.ToString(b.Content) == "×");
                Assert(widgetClose.Width == panelClose.Width && widgetClose.Height == panelClose.Height && widgetClose.FontSize == panelClose.FontSize && widgetClose.Padding == panelClose.Padding, "shared icon dimensions and typography");
                Assert(!FindButtons(monitor.Visual).Any(b => Convert.ToString(b.Content) == "···" || Convert.ToString(b.Content) == "="), "redundant header buttons removed");
                firstSettings.Close();
                var beforePreview = monitor.CurrentPreferences; int writes = 0;
                settings.Close();
                settings = new SettingsWindow(beforePreview, delegate(Preferences p) { return monitor.ApplyUserSettings(p, false); }, delegate(Preferences p) { writes++; return monitor.ApplyUserSettings(p); });
                settings.VerifyLivePreview();
                Assert(writes == 0 && monitor.CurrentPreferences.Theme == "cyberpunk" && monitor.CurrentPreferences.WidgetScale == 0.9 && monitor.RefreshInterval == 3 && monitor.CurrentPreferences.Background == 0.45, "controls preview immediately without writing preferences");
                settings.VerifySharedOpacity();
                Assert(((SolidColorBrush)((Border)((Viewbox)monitor.Visual).Child).Background).Color.A == (byte)(255 * 0.95), "widget and settings share live opacity");
                settings.Close();
                Assert(writes == 0 && monitor.CurrentPreferences.Theme == beforePreview.Theme && monitor.CurrentPreferences.WidgetScale == beforePreview.WidgetScale && monitor.RefreshInterval == beforePreview.RefreshSeconds, "closing cancels unsaved live preview");
                var leftPosition = MonitorWindow.SettingsPosition(new Rect(0, 0, 1536, 816), new Rect(1230, 0, 306, 408), new Size(320, 390));
                Assert(leftPosition.X == 902 && leftPosition.Y == 0, "settings placed to the left of widget");
                var fallback = MonitorWindow.SettingsPosition(new Rect(0, 0, 800, 600), new Rect(0, 550, 300, 50), new Size(320, 390));
                Assert(fallback.X == 308 && fallback.Y == 210, "settings fallback fits screen");
                var secondary = MonitorWindow.SettingsPosition(new Rect(-1920, -100, 1920, 1080), new Rect(-300, -100, 300, 408), new Size(320, 390));
                Assert(secondary.X == -628 && secondary.Y == -100, "settings placement supports negative monitor coordinates");
                foreach (double scale in new[] { 0.6, 1.5 })
                    foreach (double font in new[] { 0.8, 1.4 })
                    {
                        changed = monitor.CurrentPreferences; changed.WidgetScale = scale; changed.FontScale = font;
                        monitor.ApplyUserSettings(changed); monitor.UpdateLayout();
                        Assert(monitor.Left >= SystemParameters.WorkArea.Left - 1 && monitor.Top >= SystemParameters.WorkArea.Top - 1 && monitor.Left + monitor.ActualWidth <= SystemParameters.WorkArea.Right + 1 && monitor.Top + monitor.ActualHeight <= SystemParameters.WorkArea.Bottom + 1, "size and font extremes fit screen");
                        double width = monitor.ActualWidth;
                        Assert(!((Border)((Viewbox)monitor.Visual).Child).ContextMenu.Items.OfType<MenuItem>().Any(item => Convert.ToString(item.Tag) == "compact"), "compact menu removed");
                        foreach (int tab in new[] { 1, 2, 0 })
                        {
                            monitor.SelectTab(tab); monitor.UpdateLayout();
                            Assert(Math.Abs(width - monitor.ActualWidth) <= 1, "custom settings preserve width across tabs: size=" + scale + ", font=" + font + ", tab=" + tab);
                        }
                    }
            }
            finally { if (settings != null) settings.Close(); monitor.Close(); }
        }
    }
}
