using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Neon
{
    public sealed class DiskSnapshot
    {
        public string Name, Label, Error;
        public double Used, Total;
        public bool IsSystem;
    }

    public sealed class Snapshot
    {
        public DateTime Time = DateTime.Now;
        public double? Cpu;
        public double RamPercent, RamUsed, RamTotal, CommitUsed, CommitTotal;
        public double Download, Upload;
        public bool NetworkAvailable = true;
        public string NetworkNote;
        public List<DiskSnapshot> Disks = new List<DiskSnapshot>();
        public string CpuName = "CPU", GpuName = "GPU";
        public double? Gpu, GpuMemoryUsed, GpuMemoryTotal, CpuTemperature, GpuTemperature, FanRpm;
        internal double? CpuFanRpm;
        public string SensorSource = "No provider", SensorNote = "";
        public TimeSpan Uptime;
        public bool CoreAvailable;
        public string Error;
    }

    public sealed class RateCounter
    {
        private readonly Dictionary<string, long[]> previous = new Dictionary<string, long[]>();
        private double previousTime = -1;
        public double[] Sample(Dictionary<string, long[]> counters, double seconds)
        {
            double incoming = 0, outgoing = 0;
            double elapsed = seconds - previousTime;
            if (previousTime >= 0 && elapsed > 0)
                foreach (var entry in counters)
                {
                    long[] old;
                    if (!previous.TryGetValue(entry.Key, out old)) continue;
                    if (entry.Value[0] < old[0] || entry.Value[1] < old[1]) continue;
                    incoming += Math.Max(0, entry.Value[0] - old[0]) / elapsed;
                    outgoing += Math.Max(0, entry.Value[1] - old[1]) / elapsed;
                }
            previous.Clear();
            foreach (var entry in counters) previous[entry.Key] = entry.Value;
            previousTime = seconds;
            return new[] { incoming, outgoing };
        }
    }

    public sealed class Telemetry
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();
        private readonly RateCounter network = new RateCounter();
        private string networkSelection;
        private Preferences selection = new Preferences();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long lastIdle, lastKernel, lastUser;
        private bool cpuPrimed, identified;
        private string cpuName = "CPU", gpuName = "GPU not detected", nvidiaPath;
        private DateTime sensorTime = DateTime.MinValue;
        private double? cpuTemp, gpuTemp, gpuLoad, gpuUsed, gpuTotal, fan, cpuFan;
        private string source = "No provider", sensorNote = "";

        public static double? CpuUsage(long idleDelta, long kernelDelta, long userDelta)
        {
            long total = kernelDelta + userDelta;
            if (total <= 0 || idleDelta < 0 || kernelDelta < 0 || userDelta < 0) return null;
            return Math.Max(0, Math.Min(100, 100.0 * (total - idleDelta) / total));
        }

        public Snapshot Read()
        { return Read(new Preferences()); }

        public Snapshot Read(Preferences preferences)
        {
            var next = preferences.Copy();
            if (selection.GpuId != next.GpuId) sensorTime = DateTime.MinValue;
            selection = next;
            var s = new Snapshot();
            var errors = new List<string>();
            try
            {
                long idle, kernel, user;
                if (GetSystemTimes(out idle, out kernel, out user))
                {
                    if (cpuPrimed) s.Cpu = CpuUsage(idle - lastIdle, kernel - lastKernel, user - lastUser);
                    lastIdle = idle; lastKernel = kernel; lastUser = user; cpuPrimed = true;
                }
                var memory = new MemoryStatus();
                memory.Length = (uint)Marshal.SizeOf(memory);
                if (!GlobalMemoryStatusEx(ref memory)) throw new InvalidOperationException("Memory unavailable");
                s.RamTotal = memory.TotalPhys; s.RamUsed = memory.TotalPhys - memory.AvailPhys;
                s.RamPercent = 100.0 * s.RamUsed / s.RamTotal;
                s.CommitTotal = memory.TotalPageFile; s.CommitUsed = memory.TotalPageFile - memory.AvailPageFile;
                s.Uptime = TimeSpan.FromMilliseconds(GetTickCount64()); s.CoreAvailable = true;
            }
            catch (Exception ex) { errors.Add("System: " + ex.Message); }
            try
            {
                var counters = new Dictionary<string, long[]>();
                var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string key = selection.NetworkIds == null ? "all" : String.Join("|", selection.NetworkIds.OrderBy(id => id));
                if (key != networkSelection) { network.Sample(new Dictionary<string, long[]>(), clock.Elapsed.TotalSeconds); networkSelection = key; }
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!DeviceCatalog.Includes(selection.NetworkIds, adapter.Id) || adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    try { var stats = adapter.GetIPStatistics(); counters[adapter.Id] = new[] { stats.BytesReceived, stats.BytesSent }; available.Add(adapter.Id); }
                    catch (NetworkInformationException) { }
                }
                var rates = network.Sample(counters, clock.Elapsed.TotalSeconds);
                s.Download = rates[0]; s.Upload = rates[1];
                s.NetworkAvailable = selection.NetworkIds == null ? counters.Count > 0 : selection.NetworkIds.Length > 0 && selection.NetworkIds.All(available.Contains);
                s.NetworkNote = selection.NetworkIds == null ? "All active interfaces (including VPN and virtual adapters)" : selection.NetworkIds.Length == 0 ? "No interfaces selected" : "Selected interfaces: " + String.Join(", ", selection.NetworkIds);
                if (!s.NetworkAvailable && (selection.NetworkIds == null || selection.NetworkIds.Length > 0))
                { s.NetworkNote += " · unavailable or disconnected"; errors.Add(s.NetworkNote); }
            }
            catch (Exception ex)
            {
                network.Sample(new Dictionary<string, long[]>(), clock.Elapsed.TotalSeconds);
                s.NetworkAvailable = false; s.NetworkNote = "Network unavailable: " + ex.Message; errors.Add(s.NetworkNote);
            }
            try
            {
                string system = Path.GetPathRoot(Environment.SystemDirectory);
                var volumes = StorageVolumeCatalog.Read();
                foreach (var drive in DeviceCatalog.SelectVolumes(volumes, selection.VolumeIds))
                {
                    var disk = new DiskSnapshot { Name = drive.Root, IsSystem = String.Equals(drive.Root, system, StringComparison.OrdinalIgnoreCase),
                        Total = drive.TotalBytes, Used = drive.UsedBytes, Label = drive.Label, Error = drive.Error };
                    s.Disks.Add(disk);
                }
            }
            catch (Exception ex) { errors.Add("Disk: " + ex.Message); }
            if (!identified) { Identify(); identified = true; }
            if ((DateTime.UtcNow - sensorTime).TotalSeconds >= 5)
            {
                ReadSensors(); sensorTime = DateTime.UtcNow;
            }
            s.CpuName = cpuName; s.GpuName = gpuName;
            s.CpuTemperature = cpuTemp; s.GpuTemperature = gpuTemp; s.Gpu = gpuLoad;
            s.GpuMemoryUsed = gpuUsed; s.GpuMemoryTotal = gpuTotal; s.FanRpm = fan;
            s.SensorSource = source; s.SensorNote = sensorNote;
            if (!String.IsNullOrEmpty(selection.GpuId) && !gpuLoad.HasValue && !gpuTemp.HasValue && !gpuUsed.HasValue)
                errors.Add("Selected GPU sensors unavailable (missing device/provider or ambiguous sensor identity)");
            foreach (var disk in s.Disks.Where(d => d.Error != null)) errors.Add(disk.Name + ": " + disk.Error);
            s.Error = errors.Count == 0 ? null : String.Join("; ", errors);
            return s;
        }

        private static ManagementObjectSearcher Query(string ns, string query)
        {
            return new ManagementObjectSearcher(ns, query, new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false });
        }

        private void Identify()
        {
            try
            {
                using (var search = Query("root\\cimv2", "SELECT Name FROM Win32_Processor"))
                using (var items = search.Get())
                    foreach (ManagementObject item in items) { using (item) { cpuName = Convert.ToString(item["Name"]).Trim(); } break; }
            }
            catch
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0"))
                        cpuName = Convert.ToString(key.GetValue("ProcessorNameString")).Trim();
                }
                catch { cpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "CPU"; }
            }
            try
            {
                var names = new List<string>();
                using (var search = Query("root\\cimv2", "SELECT Name FROM Win32_VideoController"))
                using (var items = search.Get())
                    foreach (ManagementObject item in items) using (item) { names.Add(Convert.ToString(item["Name"])); }
                gpuName = names.FirstOrDefault(n => n.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? names.FirstOrDefault(n => n.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? names.FirstOrDefault() ?? gpuName;
            }
            catch { }
            var candidates = new List<string> {
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
            };
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                try { if (!String.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim('"'), "nvidia-smi.exe")); } catch { }
            nvidiaPath = candidates.FirstOrDefault(File.Exists);
        }

        private void ReadSensors()
        {
            // Never keep the previous temperature when a provider disappears.
            cpuTemp = gpuTemp = gpuLoad = gpuUsed = gpuTotal = fan = cpuFan = null;
            source = "No provider"; sensorNote = "";
            foreach (var provider in new[] { "LibreHardwareMonitor", "OpenHardwareMonitor" })
            {
                try
                {
                    var hardware = new List<DeviceOption>();
                    using (var search = Query("root\\" + provider, "SELECT Name, Identifier FROM Hardware"))
                    using (var items = search.Get())
                        foreach (ManagementObject item in items) using (item)
                        {
                            string id = Convert.ToString(item["Identifier"]).ToLowerInvariant();
                            if (id.Contains("/gpu-")) hardware.Add(new DeviceOption { Id = id, Name = Convert.ToString(item["Name"]) });
                        }
                    string requestedGpu = null;
                    if (String.IsNullOrEmpty(selection.GpuId)) requestedGpu = hardware.Count == 0 ? null : hardware[0].Id;
                    else if (selection.GpuId.StartsWith("pnp:", StringComparison.OrdinalIgnoreCase))
                    {
                        try { requestedGpu = DeviceCatalog.MatchGpu(selection.GpuId, DeviceCatalog.PhysicalGpus(), hardware); }
                        catch { }
                    }
                    var sensors = new List<SensorValue>();
                    using (var search = Query("root\\" + provider, "SELECT Name, SensorType, Value, Identifier FROM Sensor"))
                    using (var items = search.Get())
                    {
                        foreach (ManagementObject item in items)
                        using (item)
                        {
                            if (item["Value"] == null) continue;
                            double v = Convert.ToDouble(item["Value"], CultureInfo.InvariantCulture);
                            sensors.Add(new SensorValue { Value = v, Id = Convert.ToString(item["Identifier"]),
                                Name = Convert.ToString(item["Name"]), Type = Convert.ToString(item["SensorType"]) });
                        }
                    }
                    // Commit a whole provider sample only after enumeration succeeds.
                    var sample = SensorReadings.Read(hardware, sensors, requestedGpu);
                    if (sample.CpuTemperature.HasValue) cpuTemp = sample.CpuTemperature;
                    gpuTemp = sample.GpuTemperature; gpuLoad = sample.Gpu;
                    gpuUsed = sample.GpuMemoryUsed; gpuTotal = sample.GpuMemoryTotal;
                    if (sample.FanRpm.HasValue) fan = sample.FanRpm;
                    if (sample.CpuFanRpm.HasValue) cpuFan = sample.CpuFanRpm;
                    gpuName = sample.GpuName;
                    source = provider;
                    if (String.IsNullOrEmpty(selection.GpuId) || requestedGpu != null) break;
                }
                catch { }
            }
            // NVIDIA can expose its own sensors without a separate monitoring app.
            // Keep a complete GPU group from one provider rather than mixing devices.
            if ((selection.GpuId ?? "").StartsWith("nvidia:", StringComparison.Ordinal) || nvidiaPath != null && String.IsNullOrEmpty(selection.GpuId) && !gpuLoad.HasValue && !gpuTemp.HasValue) ReadNvidia();
            if (!String.IsNullOrEmpty(selection.GpuId) && !gpuLoad.HasValue && !gpuTemp.HasValue && !gpuUsed.HasValue)
                gpuName = "Selected GPU unavailable · " + selection.GpuId;
            if (!cpuTemp.HasValue)
                sensorNote = "CPU temperature is unavailable. Run Libre Hardware Monitor with WMI enabled; some sensors require administrator privileges.";
        }

        private void ReadNvidia()
        {
            fan = cpuFan;
            try
            {
                var sample = SensorReadings.Nvidia(DeviceCatalog.Nvidia("uuid,name,temperature.gpu,utilization.gpu,memory.used,memory.total"), selection.GpuId);
                gpuName = sample.GpuName; gpuTemp = sample.GpuTemperature; gpuLoad = sample.Gpu;
                gpuUsed = sample.GpuMemoryUsed; gpuTotal = sample.GpuMemoryTotal;
                if (gpuLoad.HasValue || gpuTemp.HasValue || gpuUsed.HasValue)
                    source = source == "No provider" ? "NVIDIA SMI" : source + " + NVIDIA SMI";
            }
            catch { }
        }
    }
}
