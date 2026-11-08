using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace Neon
{
    internal sealed class SensorValue
    {
        public string Id, Name, Type;
        public double Value;
    }

    internal static class SensorReadings
    {
        internal static Snapshot Read(List<DeviceOption> hardware, IEnumerable<SensorValue> sensors, string gpuId)
        {
            var result = new Snapshot();
            var gpu = hardware.FirstOrDefault(h => String.Equals(h.Id, gpuId, StringComparison.OrdinalIgnoreCase));
            result.GpuName = gpu == null ? "GPU sensors unavailable" : gpu.Name;
            foreach (var sensor in sensors)
            {
                double value = sensor.Value;
                if (Double.IsNaN(value) || Double.IsInfinity(value)) continue;
                string id = sensor.Id.ToLowerInvariant();
                bool cpu = id.Contains("/intelcpu/") || id.Contains("/amdcpu/") || id.Contains("/cpu/");
                string device = String.Join("/", id.Split('/').Take(3));
                bool selected = gpu != null && String.Equals(device, gpu.Id, StringComparison.OrdinalIgnoreCase);
                if (sensor.Type == "Temperature" && value > 0 && value < 150)
                {
                    if (cpu) result.CpuTemperature = Maximum(result.CpuTemperature, value);
                    if (selected) result.GpuTemperature = Maximum(result.GpuTemperature, value);
                }
                if (selected && sensor.Type == "Load" && sensor.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 && value >= 0 && value <= 100)
                    result.Gpu = Maximum(result.Gpu, value);
                if (selected && sensor.Type == "SmallData" && value >= 0)
                {
                    if (sensor.Name.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase)) result.GpuMemoryUsed = value * 1048576;
                    if (sensor.Name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase)) result.GpuMemoryTotal = value * 1048576;
                }
                if (sensor.Type == "Fan" && value >= 0)
                {
                    if (cpu) result.CpuFanRpm = Maximum(result.CpuFanRpm, value);
                    if (cpu || selected) result.FanRpm = Maximum(result.FanRpm, value);
                }
            }
            return result;
        }

        private static double Maximum(double? previous, double value)
        { return previous.HasValue ? Math.Max(previous.Value, value) : value; }

        internal static Snapshot Nvidia(IEnumerable<string> rows, string selected)
        {
            foreach (string row in rows)
            {
                string[] fields = row.Split(',').Select(f => f.Trim()).ToArray();
                if (fields.Length != 6 || !String.IsNullOrEmpty(selected) && !String.Equals(selected, "nvidia:" + fields[0], StringComparison.OrdinalIgnoreCase)) continue;
                return new Snapshot { GpuName = fields[1], GpuTemperature = Number(fields[2]), Gpu = Number(fields[3]),
                    GpuMemoryUsed = Number(fields[4]) * 1048576, GpuMemoryTotal = Number(fields[5]) * 1048576 };
            }
            return new Snapshot { GpuName = "Selected GPU unavailable" };
        }

        private static double? Number(string value)
        {
            double parsed;
            return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
                !Double.IsNaN(parsed) && !Double.IsInfinity(parsed) && parsed >= 0 ? (double?)parsed : null;
        }
    }
}
