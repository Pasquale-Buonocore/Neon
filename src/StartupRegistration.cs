using System;
using System.IO;
using Microsoft.Win32;

namespace Neon
{
    internal enum StartupApproval { Allowed, Blocked, Unknown }

    internal sealed class StartupSnapshot
    {
        public bool Registered, MatchesExecutable;
        public string ConfiguredCommand, Error, ApprovalError;
        public StartupApproval Approval;
        internal object RawValue;
        internal RegistryValueKind ValueKind;
        public string Description
        {
            get
            {
                if (Error != null) return "Startup state could not be verified. " + Error;
                if (!Registered) return "Not registered for Windows login.";
                string result = MatchesExecutable ? "Registered for Windows login." : "Registered to a different path. Save to use this copy of NEON.";
                if (Approval == StartupApproval.Blocked) result += " Disabled by Windows. Re-enable NEON in Windows startup settings.";
                else if (Approval == StartupApproval.Unknown) result += " Windows approval could not be verified. " + ApprovalError;
                return result;
            }
        }
    }

    internal interface IStartupRegistration
    {
        StartupSnapshot Read();
        bool SetEnabled(bool enabled);
        void Restore(StartupSnapshot snapshot);
    }

    internal sealed class WindowsStartupRegistration : IStartupRegistration
    {
        internal const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ApprovalPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "NEON";
        private readonly string executable, runPath, approvalPath;

        public WindowsStartupRegistration() : this(System.Windows.Forms.Application.ExecutablePath, RunPath, ApprovalPath) { }
        internal WindowsStartupRegistration(string executablePath, string registrationPath, string approvalRegistryPath)
        { executable = Path.GetFullPath(executablePath); runPath = registrationPath; approvalPath = approvalRegistryPath; }

        public StartupSnapshot Read()
        {
            var snapshot = new StartupSnapshot { Approval = StartupApproval.Allowed };
            try
            {
                snapshot.RawValue = WindowsStartupRegistry.Read(runPath, ValueName, out snapshot.ValueKind);
                snapshot.Registered = snapshot.RawValue != null;
                if (snapshot.Registered)
                {
                    snapshot.ConfiguredCommand = snapshot.RawValue as string;
                    snapshot.MatchesExecutable = snapshot.ValueKind == RegistryValueKind.String &&
                        String.Equals(snapshot.ConfiguredCommand, Command(), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) { snapshot.Error = ex.Message; return snapshot; }
            try
            {
                RegistryValueKind approvalKind;
                object value = WindowsStartupRegistry.Read(approvalPath, ValueName, out approvalKind);
                snapshot.Approval = DecodeApproval(value);
                if (snapshot.Approval == StartupApproval.Unknown) snapshot.ApprovalError = "Unrecognized Windows approval data.";
            }
            catch (Exception ex) { snapshot.Approval = StartupApproval.Unknown; snapshot.ApprovalError = ex.Message; }
            return snapshot;
        }

        // StartupApproved is a Windows implementation detail. Recognize known records only;
        // never write them or treat future/invalid formats as permission to launch.
        internal static StartupApproval DecodeApproval(object value)
        {
            if (value == null) return StartupApproval.Allowed;
            var bytes = value as byte[];
            if (bytes == null || bytes.Length != 12) return StartupApproval.Unknown;
            uint state = BitConverter.ToUInt32(bytes, 0);
            if (state == 2 || state == 6) return StartupApproval.Allowed;
            if (state == 3 || state == 7) return StartupApproval.Blocked;
            return StartupApproval.Unknown;
        }

        private string Command() { return "\"" + executable + "\""; }
        public bool SetEnabled(bool enabled)
        {
            string command = Command();
            if (enabled && command.Length > 260) throw new InvalidOperationException("The startup command exceeds Windows' 260-character limit. Move NEON to a shorter path.");
            var before = Read();
            if (before.Error != null) throw new InvalidOperationException(before.Error);
            if (enabled && before.MatchesExecutable || !enabled && !before.Registered) return false;
            try
            {
                if (enabled) WindowsStartupRegistry.Write(runPath, ValueName, command, RegistryValueKind.String);
                else WindowsStartupRegistry.DeleteValue(runPath, ValueName);
            }
            catch (Exception ex)
            {
                // A write can succeed before its read-back fails. Restore even in that case.
                try { Restore(before); }
                catch (Exception restoreError)
                { throw new InvalidOperationException(ex.Message + " The previous startup registration could not be restored. " + restoreError.Message, ex); }
                throw;
            }
            return true;
        }
        public void Restore(StartupSnapshot snapshot)
        {
            if (snapshot.Error != null) throw new InvalidOperationException("The previous startup registration is unknown.");
            if (snapshot.Registered)
            {
                WindowsStartupRegistry.Write(runPath, ValueName, snapshot.RawValue, snapshot.ValueKind);
            }
            else
            {
                WindowsStartupRegistry.DeleteValue(runPath, ValueName);
            }
        }
    }

    // Settings previews and UI tests never access the real Windows startup keys.
    internal sealed class PreviewStartupRegistration : IStartupRegistration
    {
        private bool enabled;
        public StartupSnapshot Read() { return new StartupSnapshot { Registered = enabled, MatchesExecutable = enabled, Approval = StartupApproval.Allowed }; }
        public bool SetEnabled(bool value) { bool changed = enabled != value; enabled = value; return changed; }
        public void Restore(StartupSnapshot snapshot) { enabled = snapshot.Registered; }
    }
}
