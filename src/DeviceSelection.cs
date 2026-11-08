using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;

namespace Neon
{
    internal sealed class DeviceOption
    {
        public string Id, Name;
        public override string ToString() { return Name; }
    }

    internal sealed class DeviceCatalog
    {
        public readonly List<DeviceOption> Gpus = new List<DeviceOption>();
        public readonly List<DeviceOption> Volumes = new List<DeviceOption>();
        public readonly List<DeviceOption> Networks = new List<DeviceOption>();
        public string Error;

        internal static DeviceCatalog Read()
        {
            var result = new DeviceCatalog();
            var errors = new List<string>();
            try
            {
                foreach (string row in Nvidia("uuid,name"))
                {
                    string[] fields = row.Split(',');
                    if (fields.Length == 2) result.Gpus.Add(new DeviceOption { Id = "nvidia:" + fields[0].Trim(), Name = fields[1].Trim() + " · NVIDIA SMI · " + fields[0].Trim() });
                }
            }
            catch { }
            try
            {
                foreach (var gpu in PhysicalGpus()) result.Gpus.Add(new DeviceOption { Id = gpu.Id, Name = gpu.Name + " · Windows" });
            }
            catch (Exception ex) { errors.Add("GPUs: " + ex.Message); }
            try
            {
                foreach (var volume in StorageVolumeCatalog.Read())
                    if (volume.Id.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
                        result.Volumes.Add(new DeviceOption { Id = volume.Id, Name = volume.Display });
                    else errors.Add(volume.Root + ": stable volume identity unavailable; automatic monitoring only");
            }
            catch (Exception ex) { errors.Add("Volumes: " + ex.Message); }
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().OrderBy(a => a.Name))
                    if (adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        result.Networks.Add(new DeviceOption { Id = adapter.Id, Name = adapter.Name + " · " + adapter.Description + " · " + adapter.OperationalStatus });
            }
            catch (Exception ex) { errors.Add("Network: " + ex.Message); }
            result.Error = String.Join("; ", errors);
            return result;
        }

        internal static List<DeviceOption> PhysicalGpus()
        {
            var result = new List<DeviceOption>();
            using (var search = new ManagementObjectSearcher("root\\cimv2", "SELECT Name, PNPDeviceID FROM Win32_VideoController",
                new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false }))
            using (var rows = search.Get())
                foreach (ManagementObject row in rows)
                using (row)
                {
                    string id = Convert.ToString(row["PNPDeviceID"]);
                    if (!String.IsNullOrEmpty(id)) result.Add(new DeviceOption { Id = "pnp:" + id, Name = Convert.ToString(row["Name"]) });
                }
            return result;
        }

        // A sensor's numeric provider index is not a persistent physical identity.
        // Associate by name only when both inventories prove it is unambiguous.
        internal static string MatchGpu(string selected, List<DeviceOption> physical, List<DeviceOption> hardware)
        {
            var gpu = physical.FirstOrDefault(p => String.Equals(p.Id, selected, StringComparison.OrdinalIgnoreCase));
            if (gpu == null || physical.Count(p => String.Equals(p.Name, gpu.Name, StringComparison.OrdinalIgnoreCase)) != 1) return null;
            var matches = hardware.Where(p => String.Equals(p.Name, gpu.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? matches[0].Id : null;
        }

        internal static string[] Nvidia(string fields)
        {
            var candidates = new List<string> {
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe") };
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                try { if (!String.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim('"'), "nvidia-smi.exe")); } catch { }
            string path = candidates.FirstOrDefault(File.Exists);
            if (path == null) return new string[0];
            using (var process = Process.Start(new ProcessStartInfo(path, "--query-gpu=" + fields + " --format=csv,noheader,nounits") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(1800)) { process.Kill(); return new string[0]; }
                return process.ExitCode == 0 ? output.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
            }
        }

        internal static bool Includes(string[] selection, string id)
        { return selection == null || selection.Contains(id, StringComparer.OrdinalIgnoreCase); }

        internal static List<StorageVolume> SelectVolumes(List<StorageVolume> available, string[] selected)
        {
            var result = available.Where(v => Includes(selected, v.Id)).ToList();
            if (selected != null)
                foreach (string id in selected.Where(id => !available.Any(v => String.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase))))
                    result.Add(new StorageVolume { Id = id, Root = id, Mounts = id, MediaType = "Unavailable", Error = "Selected volume unavailable" });
            return result;
        }
    }
}
