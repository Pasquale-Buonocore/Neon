using System;
using System.ComponentModel;
using System.Management;
using System.Security.Principal;
using Microsoft.Win32;

namespace Neon
{
    // Query the Windows registry provider, rather than relying on the registry view
    // inherited by the launching process. Always address this user's hive explicitly.
    internal static class WindowsStartupRegistry
    {
        private static ManagementBaseObject Call(string method, string path, string name, object value)
        {
            using (var registry = new ManagementClass(@"\\.\root\default:StdRegProv"))
            using (var input = registry.GetMethodParameters(method))
            using (var identity = WindowsIdentity.GetCurrent())
            {
                input["hDefKey"] = (uint)0x80000003; // HKEY_USERS
                input["sSubKeyName"] = identity.User.Value + "\\" + path;
                if (name != null) input["sValueName"] = name;
                if (value != null) input[value is string ? "sValue" : "uValue"] = value;
                return registry.InvokeMethod(method, input, null);
            }
        }
        private static uint Result(ManagementBaseObject result) { return Convert.ToUInt32(result["ReturnValue"]); }
        private static void Check(ManagementBaseObject result, string operation)
        {
            uint code = Result(result);
            if (code != 0) throw new InvalidOperationException(operation + " failed in Windows (" + code + "): " + new Win32Exception((int)code).Message);
        }
        internal static object Read(string path, string name, out RegistryValueKind kind)
        {
            kind = RegistryValueKind.Unknown;
            using (var values = Call("EnumValues", path, null, null))
            {
                uint code = Result(values);
                if (code == 2 || code == 3) return null; // Missing key, not a permissions failure.
                Check(values, "Reading startup registration");
                var names = values["sNames"] as string[];
                var types = values["Types"] as int[];
                if (names == null) return null;
                int index = Array.FindIndex(names, item => String.Equals(item, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0) return null;
                kind = (RegistryValueKind)types[index];
            }
            string method;
            if (kind == RegistryValueKind.String) method = "GetStringValue";
            else if (kind == RegistryValueKind.Binary) method = "GetBinaryValue";
            else throw new InvalidOperationException("The startup registry value has an unsupported type: " + kind + ".");
            using (var result = Call(method, path, name, null))
            {
                Check(result, "Reading startup registration");
                return result[kind == RegistryValueKind.String ? "sValue" : "uValue"];
            }
        }
        internal static void Write(string path, string name, object value, RegistryValueKind kind)
        {
            string method = kind == RegistryValueKind.String ? "SetStringValue" : kind == RegistryValueKind.Binary ? "SetBinaryValue" : null;
            if (method == null) throw new InvalidOperationException("Unsupported startup registry value type: " + kind + ".");
            using (var created = Call("CreateKey", path, null, null)) Check(created, "Creating startup registration");
            using (var written = Call(method, path, name, value)) Check(written, "Saving startup registration");
            RegistryValueKind restoredKind;
            object restored = Read(path, name, out restoredKind);
            bool matches = kind == restoredKind && (kind == RegistryValueKind.String ? String.Equals(value as string, restored as string, StringComparison.Ordinal) :
                restored != null && Convert.ToBase64String((byte[])value) == Convert.ToBase64String((byte[])restored));
            if (!matches) throw new InvalidOperationException("Windows did not retain the startup registration. Reopen Personalize to check its state.");
        }
        internal static void DeleteValue(string path, string name)
        {
            using (var result = Call("DeleteValue", path, name, null))
            {
                uint code = Result(result);
                if (code != 2 && code != 3) Check(result, "Removing startup registration");
            }
            RegistryValueKind kind;
            if (Read(path, name, out kind) != null) throw new InvalidOperationException("Windows did not remove the startup registration.");
        }
        // Used only to clean up the unique registry keys created by verification.
        internal static void DeleteTestKey(string path)
        {
            if (!path.StartsWith(@"Software\NEON\Tests\", StringComparison.Ordinal) || path.Contains(".."))
                throw new InvalidOperationException("Only isolated startup test keys can be removed.");
            using (var result = Call("DeleteKey", path, null, null))
            {
                uint code = Result(result);
                if (code != 2 && code != 3) Check(result, "Removing startup test key");
            }
        }
    }
}
