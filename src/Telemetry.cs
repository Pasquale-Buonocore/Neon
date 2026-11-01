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
        public List<DiskSnapshot> Disks = new List<DiskSnapshot>();
        public string CpuName = "CPU", GpuName = "GPU";
        public double? Gpu, GpuMemoryUsed, GpuMemoryTotal, CpuTemperature, GpuTemperature, FanRpm;
        public string SensorSource = "Nessun provider", SensorNote = "";
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
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long lastIdle, lastKernel, lastUser;
        private bool cpuPrimed, identified;
        private string cpuName = "CPU", gpuName = "GPU non rilevata", nvidiaPath;
        private DateTime sensorTime = DateTime.MinValue;
        private double? cpuTemp, gpuTemp, gpuLoad, gpuUsed, gpuTotal, fan;
        private string source = "Nessun provider", sensorNote = "";

        public static double? CpuUsage(long idleDelta, long kernelDelta, long userDelta)
        {
            long total = kernelDelta + userDelta;
            if (total <= 0 || idleDelta < 0 || kernelDelta < 0 || userDelta < 0) return null;
            return Math.Max(0, Math.Min(100, 100.0 * (total - idleDelta) / total));
        }

        public Snapshot Read()
        {
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
                if (!GlobalMemoryStatusEx(ref memory)) throw new InvalidOperationException("Memoria non disponibile");
                s.RamTotal = memory.TotalPhys; s.RamUsed = memory.TotalPhys - memory.AvailPhys;
                s.RamPercent = 100.0 * s.RamUsed / s.RamTotal;
                s.CommitTotal = memory.TotalPageFile; s.CommitUsed = memory.TotalPageFile - memory.AvailPageFile;
                s.Uptime = TimeSpan.FromMilliseconds(GetTickCount64()); s.CoreAvailable = true;
            }
            catch (Exception ex) { errors.Add("Sistema: " + ex.Message); }
            try
            {
                var counters = new Dictionary<string, long[]>();
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    try { var stats = adapter.GetIPStatistics(); counters[adapter.Id] = new[] { stats.BytesReceived, stats.BytesSent }; }
                    catch (NetworkInformationException) { }
                }
                var rates = network.Sample(counters, clock.Elapsed.TotalSeconds);
                s.Download = rates[0]; s.Upload = rates[1];
            }
            catch (Exception ex) { errors.Add("Rete: " + ex.Message); }
            try
            {
                string system = Path.GetPathRoot(Environment.SystemDirectory);
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).OrderBy(d => d.Name))
                {
                    var disk = new DiskSnapshot { Name = drive.Name, IsSystem = String.Equals(drive.Name, system, StringComparison.OrdinalIgnoreCase) };
                    try
                    {
                        if (!drive.IsReady) disk.Error = "Unità non disponibile";
                        else { disk.Total = drive.TotalSize; disk.Used = drive.TotalSize - drive.TotalFreeSpace; disk.Label = drive.VolumeLabel; }
                    }
                    catch (Exception ex) { disk.Error = "Unità non accessibile"; errors.Add(drive.Name + ": " + ex.Message); }
                    s.Disks.Add(disk);
                }
            }
            catch (Exception ex) { errors.Add("Disco: " + ex.Message); }
            if (!identified) { Identify(); identified = true; }
            if ((DateTime.UtcNow - sensorTime).TotalSeconds >= 5)
            {
                ReadSensors(); sensorTime = DateTime.UtcNow;
            }
            s.CpuName = cpuName; s.GpuName = gpuName;
            s.CpuTemperature = cpuTemp; s.GpuTemperature = gpuTemp; s.Gpu = gpuLoad;
            s.GpuMemoryUsed = gpuUsed; s.GpuMemoryTotal = gpuTotal; s.FanRpm = fan;
            s.SensorSource = source; s.SensorNote = sensorNote;
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
            cpuTemp = gpuTemp = gpuLoad = gpuUsed = gpuTotal = fan = null;
            source = "Nessun provider"; sensorNote = "";
            foreach (var provider in new[] { "LibreHardwareMonitor", "OpenHardwareMonitor" })
            {
                try
                {
                    var cpuTemps = new List<double>(); var gpuTemps = new List<double>();
                    var gpuLoads = new List<double>(); var fans = new List<double>();
                    string selectedGpu = null;
                    using (var search = Query("root\\" + provider, "SELECT Name, SensorType, Value, Identifier FROM Sensor"))
                    using (var items = search.Get())
                    {
                        foreach (ManagementObject item in items)
                        using (item)
                        {
                            if (item["Value"] == null) continue;
                            double v = Convert.ToDouble(item["Value"], CultureInfo.InvariantCulture);
                            if (Double.IsNaN(v) || Double.IsInfinity(v)) continue;
                            string id = Convert.ToString(item["Identifier"]).ToLowerInvariant();
                            string name = Convert.ToString(item["Name"]);
                            string type = Convert.ToString(item["SensorType"]);
                            bool cpu = id.Contains("/intelcpu/") || id.Contains("/amdcpu/") || id.Contains("/cpu/");
                            bool gpu = id.Contains("/gpu-");
                            // Keep every GPU metric on the same physical device.
                            if (gpu)
                            {
                                string device = String.Join("/", id.Split('/').Take(3));
                                if (selectedGpu == null) selectedGpu = device;
                                gpu = selectedGpu == device;
                            }
                            if (type == "Temperature" && v > 0 && v < 150)
                            { if (cpu) cpuTemps.Add(v); if (gpu) gpuTemps.Add(v); }
                            if (gpu && type == "Load" && name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0) gpuLoads.Add(v);
                            if (type == "Fan" && v >= 0) fans.Add(v);
                        }
                    }
                    if (cpuTemps.Count > 0) cpuTemp = cpuTemps.Max();
                    if (gpuTemps.Count > 0) gpuTemp = gpuTemps.Max();
                    if (gpuLoads.Count > 0) gpuLoad = gpuLoads.Max();
                    if (fans.Count > 0) fan = fans.Max();
                    source = provider;
                    // Resolve the selected GPU's display name from the provider too.
                    if (selectedGpu != null)
                        using (var search = Query("root\\" + provider, "SELECT Name, Identifier FROM Hardware"))
                        using (var items = search.Get())
                            foreach (ManagementObject item in items) using (item)
                                if (Convert.ToString(item["Identifier"]).ToLowerInvariant() == selectedGpu) gpuName = Convert.ToString(item["Name"]);
                    break;
                }
                catch { }
            }
            // NVIDIA can expose its own sensors without a separate monitoring app.
            // Keep a complete GPU group from one provider rather than mixing devices.
            if (nvidiaPath != null && !gpuLoad.HasValue && !gpuTemp.HasValue) ReadNvidia();
            if (!cpuTemp.HasValue)
                sensorNote = "Temperatura CPU non esposta. Avvia Libre Hardware Monitor con WMI attivo; alcuni sensori richiedono diritti amministrativi.";
        }

        private void ReadNvidia()
        {
            try
            {
                var info = new ProcessStartInfo(nvidiaPath, "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total --format=csv,noheader,nounits") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var process = Process.Start(info))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(1800)) { process.Kill(); return; }
                    if (process.ExitCode != 0) return;
                    string row = stdout.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (row == null) return;
                    string[] fields = row.Split(',').Select(f => f.Trim()).ToArray();
                    if (fields.Length != 5) return;
                    gpuName = fields[0]; gpuTemp = Number(fields[1]); gpuLoad = Number(fields[2]);
                    var used = Number(fields[3]); var total = Number(fields[4]);
                    gpuUsed = used.HasValue ? used * 1048576 : null;
                    gpuTotal = total.HasValue ? total * 1048576 : null;
                    source = source == "Nessun provider" ? "NVIDIA SMI" : source + " + NVIDIA";
                }
            }
            catch { }
        }

        private static double? Number(string value)
        {
            double parsed;
            return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && !Double.IsNaN(parsed) && !Double.IsInfinity(parsed) && parsed >= 0 ? (double?)parsed : null;
        }
    }
}
