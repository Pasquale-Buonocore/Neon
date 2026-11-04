using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Neon
{
    internal static class StartupTests
    {
        private static void Assert(bool value, string description)
        { if (!value) throw new InvalidOperationException("Startup: " + description); }

        private sealed class FakeStartup : IStartupRegistration
        {
            public bool Enabled, Blocked, ReadError, WriteError, RestoreError;
            public int Writes, Restores;
            public StartupSnapshot Read()
            { return new StartupSnapshot { Registered = Enabled, MatchesExecutable = Enabled, Approval = Blocked ? StartupApproval.Blocked : StartupApproval.Allowed, Error = ReadError ? "Simulated read failure." : null }; }
            public bool SetEnabled(bool enabled)
            {
                if (WriteError) throw new UnauthorizedAccessException("Simulated write failure.");
                if (enabled == Enabled) return false;
                Enabled = enabled; Writes++; return true;
            }
            public void Restore(StartupSnapshot snapshot)
            {
                Restores++;
                if (RestoreError) throw new UnauthorizedAccessException("Simulated rollback failure.");
                Enabled = snapshot.Registered;
            }
        }
        private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
        {
            var match = root as T; if (match != null) yield return match;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        private static void Click(SettingsWindow window, string caption)
        { Find<Button>(window.Visual).Single(b => Convert.ToString(b.Content) == caption).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        private static CheckBox Option(SettingsWindow window) { return Find<CheckBox>(window.Visual).Single(); }
        private static string Text(SettingsWindow window) { return String.Join(" ", Find<TextBlock>(window.Visual).Select(t => t.Text).ToArray()); }
        private static SettingsWindow Open(FakeStartup startup, Func<Preferences, bool> save)
        {
            var window = new SettingsWindow(new Preferences(), delegate { return true; }, save, startup);
            window.Left = window.Top = -32000; window.Show(); window.UpdateLayout(); return window;
        }
        public static void Run()
        {
            VerifyPanel(); VerifyRegistry();
        }
        private static void VerifyPanel()
        {
            foreach (string action in new[] { "Cancel", "×", "Escape" })
            {
                var startup = new FakeStartup(); int saves = 0;
                var window = Open(startup, delegate { saves++; return true; });
                try
                {
                    Assert(Option(window).IsChecked == false, "disabled by default");
                    Option(window).IsChecked = true;
                    window.VerifyLivePreview();
                    Assert(startup.Writes == 0 && saves == 0, "selection and live preview do not register startup");
                    window.RefreshStartupState(); Assert(Option(window).IsChecked == true, "reopening preserves a pending selection");
                    if (action == "Escape") window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    else Click(window, action);
                    Assert(!window.IsVisible && startup.Writes == 0 && saves == 0, action + " discards startup changes");
                }
                finally { window.Close(); }
            }
            var enabled = new FakeStartup(); int saved = 0;
            var success = Open(enabled, delegate { saved++; return true; });
            try
            {
                Option(success).IsChecked = true; Click(success, "Save");
                Assert(enabled.Enabled && enabled.Writes == 1 && saved == 1 && !success.IsVisible, "Save registers startup and closes");
            }
            finally { success.Close(); }
            var reset = Open(enabled, delegate { return true; });
            try
            {
                Click(reset, "Reset"); Assert(Option(reset).IsChecked == false && enabled.Enabled && enabled.Writes == 1, "Reset is provisional");
                Click(reset, "Save"); Assert(!enabled.Enabled, "Save after Reset removes startup");
            }
            finally { reset.Close(); }
            var blocked = new FakeStartup { Enabled = true, Blocked = true };
            var blockedWindow = Open(blocked, delegate { return true; });
            try
            {
                Assert(Option(blockedWindow).IsChecked == true && Text(blockedWindow).Contains("Disabled by Windows"), "registration and Windows block are distinct");
                Assert(Find<Button>(blockedWindow.Visual).Single(b => Convert.ToString(b.Content) == "Open Windows startup settings").IsVisible, "Windows recovery link visible");
                Click(blockedWindow, "Save"); Assert(blocked.Blocked && blocked.Writes == 0, "Save respects the Windows block");
            }
            finally { blockedWindow.Close(); }
            var external = new FakeStartup(); var reopened = Open(external, delegate { return true; });
            try { external.Enabled = true; reopened.RefreshStartupState(); Assert(Option(reopened).IsChecked == true, "reopening reads external registration changes"); }
            finally { reopened.Close(); }
            foreach (bool rollbackFails in new[] { false, true })
            {
                var failing = new FakeStartup { RestoreError = rollbackFails };
                var window = Open(failing, delegate { return false; });
                try
                {
                    Option(window).IsChecked = true; Click(window, "Save");
                    Assert(window.IsVisible && failing.Restores == 1 && failing.Enabled == rollbackFails, "preference failure rolls back registration or reports failure");
                    Assert(Text(window).Contains(rollbackFails ? "could not be restored" : "this session"), "specific save/rollback failure shown");
                    Assert(Text(window).Contains(rollbackFails ? "Registered for Windows login" : "Not registered"), "actual state reread after rollback");
                }
                finally { window.Close(); }
            }
            var denied = new FakeStartup { WriteError = true }; int deniedSaves = 0;
            var deniedWindow = Open(denied, delegate { deniedSaves++; return true; });
            try
            {
                Option(deniedWindow).IsChecked = true; Click(deniedWindow, "Save");
                Assert(deniedWindow.IsVisible && deniedSaves == 0 && Text(deniedWindow).Contains("Simulated write failure"), "registration failure prevents preference save");
            }
            finally { deniedWindow.Close(); }
            var unreadable = Open(new FakeStartup { ReadError = true }, delegate { return true; });
            try
            {
                Assert(!Option(unreadable).IsEnabled && Option(unreadable).IsChecked == null, "read failures show an unknown disabled control");
                Click(unreadable, "Save"); Assert(unreadable.IsVisible && Text(unreadable).Contains("Simulated read failure"), "unknown registration cannot be overwritten");
            }
            finally { unreadable.Close(); }
        }
        private static byte[] Approval(uint state) { var result = new byte[12]; Array.Copy(BitConverter.GetBytes(state), result, 4); return result; }
        private static void VerifyRegistry()
        {
            string testPath = @"Software\NEON\Tests\" + Guid.NewGuid().ToString("N");
            string runPath = testPath + @"\Run", approvalPath = testPath + @"\Approval";
            string executable = Path.Combine(Path.GetTempPath(), "NEON path with spaces", "NeonMonitor.exe");
            var service = new WindowsStartupRegistration(executable, runPath, approvalPath);
            try
            {
                Assert(!service.Read().Registered && !service.SetEnabled(false), "missing registration is disabled without creating a key");
                WindowsStartupRegistry.Write(runPath, "OtherApp", "keep", RegistryValueKind.String);
                Assert(service.SetEnabled(true), "enable registration");
                var registered = service.Read();
                Assert(registered.MatchesExecutable && registered.ConfiguredCommand == "\"" + executable + "\"", "absolute executable path is quoted");
                Assert(!service.SetEnabled(true), "repeated enable is idempotent");
                foreach (uint state in new uint[] { 2, 3, 6, 7, 99 })
                {
                    WindowsStartupRegistry.Write(approvalPath, "NEON", Approval(state), RegistryValueKind.Binary);
                    var expected = state == 2 || state == 6 ? StartupApproval.Allowed : state == 3 || state == 7 ? StartupApproval.Blocked : StartupApproval.Unknown;
                    Assert(service.Read().Approval == expected, "Windows approval state " + state);
                    service.SetEnabled(true);
                    RegistryValueKind approvalKind;
                    Assert(((byte[])WindowsStartupRegistry.Read(approvalPath, "NEON", out approvalKind))[0] == state, "approval is never changed");
                }
                Assert(WindowsStartupRegistration.DecodeApproval(new byte[1]) == StartupApproval.Unknown && WindowsStartupRegistration.DecodeApproval("invalid") == StartupApproval.Unknown, "malformed approval is unknown");
                WindowsStartupRegistry.Write(runPath, "NEON", "\"C:\\Old NEON\\NeonMonitor.exe\"", RegistryValueKind.String);
                var old = service.Read(); Assert(!old.MatchesExecutable && old.Description.Contains("different path"), "moved executable reported");
                service.SetEnabled(true); service.Restore(old);
                Assert(service.Read().ConfiguredCommand == old.ConfiguredCommand, "rollback restores the exact old command");
                Assert(service.SetEnabled(false) && !service.SetEnabled(false), "disable is idempotent");
                RegistryValueKind otherKind;
                Assert((string)WindowsStartupRegistry.Read(runPath, "OtherApp", out otherKind) == "keep", "other applications unchanged");
                service.Restore(registered); Assert(service.Read().MatchesExecutable, "restore existing registration");
                var atLimit = new WindowsStartupRegistration("C:\\" + new string('x', 230) + "\\" + new string('y', 24), runPath, approvalPath);
                Assert(atLimit.SetEnabled(true), "260-character quoted command is accepted");
                service.SetEnabled(true);
                var tooLong = new WindowsStartupRegistration("C:\\" + new string('x', 230) + "\\" + new string('y', 25), runPath, approvalPath);
                bool rejected = false; try { tooLong.SetEnabled(true); } catch (InvalidOperationException) { rejected = true; }
                Assert(rejected && service.Read().MatchesExecutable, "overlong command rejected without mutation");
            }
            finally
            {
                WindowsStartupRegistry.DeleteTestKey(runPath); WindowsStartupRegistry.DeleteTestKey(approvalPath); WindowsStartupRegistry.DeleteTestKey(testPath);
            }
        }
    }
}
