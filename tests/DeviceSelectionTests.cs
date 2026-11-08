using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Management;

namespace Neon
{
    internal static class DeviceSelectionTests
    {
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Device selection: " + message); }

        internal static void Run(string directory)
        {
            VerifySensorGroups();
            string path = Path.Combine(directory, "devices.json");
            var preferences = new Preferences { GpuId = "nvidia:GPU-123", VolumeIds = new[] { "volume-a" }, NetworkIds = new[] { "ethernet", "vpn" } };
            Check(PreferencesStore.Save(path, preferences), "save selection");
            var restored = PreferencesStore.Load(path);
            Check(restored.GpuId == preferences.GpuId && restored.VolumeIds.SequenceEqual(preferences.VolumeIds) && restored.NetworkIds.SequenceEqual(preferences.NetworkIds), "persistent selections");
            var copy = preferences.Copy(); copy.NetworkIds[0] = "changed"; copy.VolumeIds[0] = "changed";
            Check(preferences.NetworkIds[0] == "ethernet" && preferences.VolumeIds[0] == "volume-a", "cancel/preview copies do not mutate saved selections");
            Check(DeviceCatalog.Includes(null, "new") && !DeviceCatalog.Includes(new string[0], "new"), "all versus explicitly empty selection");
            Check(DeviceCatalog.Includes(restored.NetworkIds, "ETHERNET") && !DeviceCatalog.Includes(new[] { "ethernet" }, "vpn"), "identity matching and VPN exclusion");
            var physical = new List<DeviceOption> { new DeviceOption { Id = "pnp:a", Name = "GPU A" }, new DeviceOption { Id = "pnp:b", Name = "GPU B" } };
            var sensors = new List<DeviceOption> { new DeviceOption { Id = "/gpu/1", Name = "GPU B" }, new DeviceOption { Id = "/gpu/0", Name = "GPU A" } };
            Check(DeviceCatalog.MatchGpu("pnp:b", physical, sensors) == "/gpu/1", "select second GPU regardless of enumeration order");
            sensors[0].Id = "/gpu/0"; sensors[1].Id = "/gpu/1";
            Check(DeviceCatalog.MatchGpu("pnp:b", physical, sensors) == "/gpu/0", "provider reindex resolves physical identity");
            Check(DeviceCatalog.MatchGpu("pnp:removed", physical, sensors) == null, "removed GPU never falls back");
            physical.Add(new DeviceOption { Id = "pnp:c", Name = "GPU B" });
            Check(DeviceCatalog.MatchGpu("pnp:b", physical, sensors) == null, "identical GPU models cannot be guessed by name");
            physical.RemoveAt(2); sensors.Add(new DeviceOption { Id = "/gpu/2", Name = "GPU B" });
            Check(DeviceCatalog.MatchGpu("pnp:b", physical, sensors) == null, "ambiguous provider hardware is unavailable");
            var counter = new RateCounter();
            Check(counter.Sample(Counters(100, 200), 0)[0] == 0, "initial baseline");
            Check(counter.Sample(Counters(200, 400), 1).SequenceEqual(new[] { 100.0, 200.0 }), "interface rates");
            Check(counter.Sample(Counters(10, 900), 2).SequenceEqual(new[] { 0.0, 0.0 }), "counter reset discards the whole interface delta");
            counter.Sample(new Dictionary<string, long[]>(), 3);
            Check(counter.Sample(Counters(9000, 9000), 4)[0] == 0, "reconnection primes baseline");
            Check(counter.Sample(Counters(9100, 9200), 5)[1] == 200, "reconnected rates recover");
            var aggregate = new RateCounter();
            aggregate.Sample(new Dictionary<string, long[]> { { "ethernet", new long[] { 10, 20 } }, { "vpn", new long[] { 30, 40 } } }, 0);
            Check(aggregate.Sample(new Dictionary<string, long[]> { { "ethernet", new long[] { 110, 220 } }, { "vpn", new long[] { 330, 440 } } }, 1)
                .SequenceEqual(new[] { 400.0, 600.0 }), "explicit multiple-interface aggregation");
            var telemetry = new Telemetry();
            var absent = telemetry.Read(new Preferences { GpuId = "nvidia:missing-device-for-verification", VolumeIds = new[] { "missing-volume-for-verification" }, NetworkIds = new[] { "missing-interface-for-verification" } });
            Check(!absent.Gpu.HasValue && !absent.GpuTemperature.HasValue && !absent.GpuMemoryUsed.HasValue, "missing selected GPU never uses another provider/card");
            Check(!absent.NetworkAvailable && absent.Disks.Count == 1 && absent.Disks[0].Error != null && absent.Disks[0].Total == 0, "missing selected volume/interface remain unavailable");
            var disabled = telemetry.Read(new Preferences { GpuId = "nvidia:missing-device-for-verification", VolumeIds = new string[0], NetworkIds = new string[0] });
            Check(disabled.Disks.Count == 0 && !disabled.NetworkAvailable, "empty selections exclude all local volumes and interfaces");
            Preferences applied = null, saved = null;
            var window = new SettingsWindow(new Preferences(), value => { applied = value; return true; }, value => { saved = value; return true; }, null, true);
            window.VerifyDeviceControls();
            Check(applied.GpuId == "nvidia:second" && applied.NetworkIds.SequenceEqual(new[] { "ethernet" }), "controls preview the selected GPU and interface");
            Check(saved.VolumeIds.SequenceEqual(new[] { "volume-a" }) && saved.NetworkIds.SequenceEqual(new[] { "ethernet" }), "controls save explicit volume/interface selections");
            window.Close();
            var monitor = new MonitorWindow(true);
            try { monitor.VerifyStorageDeviceSelection(); monitor.VerifyDevicesCompanion(); }
            finally { monitor.Close(); }
        }
        private static Dictionary<string, long[]> Counters(long received, long sent)
        { return new Dictionary<string, long[]> { { "ethernet", new[] { received, sent } } }; }

        internal static string RunLive()
        {
            var catalog = DeviceCatalog.Read();
            var report = new StringBuilder();
            string operatingSystem = "Runtime OS version " + Environment.OSVersion.Version;
            try
            {
                using (var search = new ManagementObjectSearcher("root\\cimv2", "SELECT Caption, Version FROM Win32_OperatingSystem",
                    new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false }))
                using (var rows = search.Get())
                    foreach (ManagementObject row in rows) using (row)
                        operatingSystem = Convert.ToString(row["Caption"]) + " " + Convert.ToString(row["Version"]);
            }
            catch { }
            report.AppendLine("Device selection live check · " + operatingSystem);
            report.AppendLine("Inventory: " + catalog.Gpus.Count + " GPU entries, " + catalog.Volumes.Count + " volumes, " + catalog.Networks.Count + " interfaces");
            if (!String.IsNullOrEmpty(catalog.Error)) report.AppendLine("Inventory limitations: " + catalog.Error);
            foreach (var option in catalog.Gpus)
            {
                var sample = new Telemetry().Read(new Preferences { GpuId = option.Id, VolumeIds = new string[0], NetworkIds = new string[0] });
                bool reading = sample.Gpu.HasValue || sample.GpuTemperature.HasValue || sample.GpuMemoryUsed.HasValue;
                if (reading) Check(option.Name.StartsWith(sample.GpuName + " · ", StringComparison.OrdinalIgnoreCase), "live GPU readings identify the selected card");
                report.AppendLine("GPU " + option.Name + ": " + (reading ? "selected provider readings available" : "N/A (no verified sensor association/provider)"));
            }
            foreach (var option in catalog.Volumes)
            {
                var sample = new Telemetry().Read(new Preferences { GpuId = "verification:no-gpu", VolumeIds = new[] { option.Id }, NetworkIds = new string[0] });
                Check(sample.Disks.Count == 1, "live selection returns exactly one volume");
                var volume = StorageVolumeCatalog.Read().FirstOrDefault(v => String.Equals(v.Id, option.Id, StringComparison.OrdinalIgnoreCase));
                if (volume != null) Check(sample.Disks[0].Name == volume.Root, "live volume selection uses the intended mount");
                report.AppendLine("Volume " + option.Name + ": " + (sample.Disks[0].Error ?? "selected capacity available"));
            }
            foreach (var option in catalog.Networks)
            {
                var sample = new Telemetry().Read(new Preferences { GpuId = "verification:no-gpu", VolumeIds = new string[0], NetworkIds = new[] { option.Id } });
                Check(sample.NetworkNote.Contains(option.Id), "live network sample identifies its selected interface");
                report.AppendLine("Interface " + option.Name + ": " + (sample.NetworkAvailable ? "selected counters available" : "N/A (disconnected/unavailable)"));
            }
            report.AppendLine("No saved settings, device configuration or network connectivity changed. Physical hot-plug and a second Windows version require separate acceptance.");
            return report.ToString();
        }

        private static void VerifySensorGroups()
        {
            var hardware = new List<DeviceOption> {
                new DeviceOption { Id = "/gpu-nvidia/0", Name = "Card A" },
                new DeviceOption { Id = "/gpu-nvidia/1", Name = "Card B" } };
            var sensors = new[] {
                Sensor("/gpu-nvidia/0/load/0", "GPU Core", "Load", 99),
                Sensor("/gpu-nvidia/0/temperature/0", "GPU Core", "Temperature", 95),
                Sensor("/gpu-nvidia/0/smalldata/0", "GPU Memory Used", "SmallData", 8000),
                Sensor("/gpu-nvidia/0/smalldata/1", "GPU Memory Total", "SmallData", 12000),
                Sensor("/gpu-nvidia/1/load/0", "GPU Core", "Load", 12),
                Sensor("/gpu-nvidia/1/temperature/0", "GPU Core", "Temperature", 40),
                Sensor("/gpu-nvidia/1/smalldata/0", "GPU Memory Used", "SmallData", 500),
                Sensor("/gpu-nvidia/1/smalldata/1", "GPU Memory Total", "SmallData", 2000),
                Sensor("/gpu-nvidia/0/fan/0", "GPU Fan", "Fan", 6000),
                Sensor("/gpu-nvidia/1/fan/0", "GPU Fan", "Fan", 1200),
                Sensor("/intelcpu/0/fan/0", "CPU Fan", "Fan", 900),
                Sensor("/intelcpu/0/temperature/0", "CPU Package", "Temperature", 60) };
            var selected = SensorReadings.Read(hardware, sensors, "/gpu-nvidia/1");
            Check(selected.GpuName == "Card B" && selected.Gpu == 12 && selected.GpuTemperature == 40 &&
                selected.GpuMemoryUsed == 500 * 1048576.0 && selected.GpuMemoryTotal == 2000 * 1048576.0,
                "every selected GPU metric comes from card B despite hotter/busier card A");
            Check(selected.CpuTemperature == 60, "GPU selection preserves CPU readings");
            Check(selected.FanRpm == 1200 && selected.CpuFanRpm == 900, "fan information excludes other GPUs and retains the CPU baseline for fallback");
            var missing = SensorReadings.Read(hardware, sensors, "/gpu-nvidia/missing");
            Check(!missing.Gpu.HasValue && !missing.GpuTemperature.HasValue && !missing.GpuMemoryUsed.HasValue && missing.CpuTemperature == 60,
                "missing GPU sensors cannot use another card's data");
            var stale = SensorReadings.Read(hardware, new SensorValue[0], "/gpu-nvidia/1");
            Check(!stale.Gpu.HasValue && !stale.GpuTemperature.HasValue && !stale.GpuMemoryUsed.HasValue, "provider loss clears all GPU readings");
            string[] rows = { "GPU-a,Card A,95,99,8000,12000", "GPU-b,Card B,40,12,500,2000" };
            var nvidia = SensorReadings.Nvidia(rows, "nvidia:GPU-b");
            Check(nvidia.GpuName == "Card B" && nvidia.Gpu == 12 && nvidia.GpuTemperature == 40 && nvidia.GpuMemoryUsed == 500 * 1048576.0,
                "NVIDIA UUID selection keeps the entire row together");
            Check(SensorReadings.Nvidia(rows.Reverse(), "nvidia:GPU-b").Gpu == 12, "UUID binding survives row reorder");
            Check(!SensorReadings.Nvidia(rows, "nvidia:removed").Gpu.HasValue, "missing UUID never takes first row");
            var partial = SensorReadings.Nvidia(new[] { "GPU-b,Card B,N/A,12,N/A,2000" }, "nvidia:GPU-b");
            Check(partial.Gpu == 12 && !partial.GpuTemperature.HasValue && !partial.GpuMemoryUsed.HasValue, "unsupported NVIDIA metrics stay unavailable");
        }

        private static SensorValue Sensor(string id, string name, string type, double value)
        { return new SensorValue { Id = id, Name = name, Type = type, Value = value }; }
    }
}
