using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Neon
{
    internal sealed class ProcessObservation
    {
        public int Pid;
        public string Name;
        public DateTime? StartedUtc;
        public TimeSpan? CpuTime;
        public long? WorkingSetBytes;
    }

    internal sealed class ProcessEntry
    {
        public int Pid;
        public string Name;
        public double? CpuPercent;
        public long? WorkingSetBytes;
    }

    internal sealed class ProcessSnapshot
    {
        public readonly List<ProcessEntry> Entries = new List<ProcessEntry>();
        public bool MeasuringCpu;
        public string Error;
        public bool Illustrative;
    }

    internal interface IProcessSource
    {
        IList<ProcessObservation> Read();
        int LogicalProcessorCount { get; }
    }

    internal sealed class WindowsProcessSource : IProcessSource
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetActiveProcessorCount(ushort groupNumber);

        public int LogicalProcessorCount
        {
            get
            {
                try { uint count = GetActiveProcessorCount(0xffff); if (count > 0) return (int)count; }
                catch (EntryPointNotFoundException) { }
                return Math.Max(1, Environment.ProcessorCount);
            }
        }

        public IList<ProcessObservation> Read()
        {
            var result = new List<ProcessObservation>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    int pid;
                    try { pid = process.Id; }
                    catch (InvalidOperationException) { continue; }
                    catch (Win32Exception) { continue; }
                    var item = new ProcessObservation { Pid = pid, Name = "Unknown process" };
                    try { item.Name = process.ProcessName; }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    try { item.StartedUtc = process.StartTime.ToUniversalTime(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    catch (NotSupportedException) { }
                    try { item.CpuTime = process.TotalProcessorTime; }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    catch (NotSupportedException) { }
                    try { item.WorkingSetBytes = process.WorkingSet64; }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    catch (NotSupportedException) { }
                    result.Add(item);
                }
            }
            return result;
        }
    }

    internal sealed class ProcessSampler
    {
        private readonly IProcessSource source;
        private readonly Func<double> seconds;
        private Dictionary<Tuple<int, long>, TimeSpan> previous = new Dictionary<Tuple<int, long>, TimeSpan>();
        private double previousAt = Double.NaN;
        private int previousSession = -1;

        public ProcessSampler(IProcessSource processSource, Func<double> monotonicSeconds)
        { source = processSource; seconds = monotonicSeconds; }

        public ProcessSnapshot Read(int session)
        {
            IList<ProcessObservation> observations;
            try { observations = source.Read(); }
            catch { previous.Clear(); previousAt = Double.NaN; throw; }
            double at = seconds();
            if (session != previousSession)
            {
                previous.Clear(); previousAt = Double.NaN; previousSession = session;
            }
            double elapsed = at - previousAt;
            bool primed = !Double.IsNaN(previousAt) && elapsed > 0;
            int processorCount = Math.Max(1, source.LogicalProcessorCount);
            var next = new Dictionary<Tuple<int, long>, TimeSpan>();
            var snapshot = new ProcessSnapshot { MeasuringCpu = !primed };
            foreach (var observation in observations)
            {
                if (observation == null) continue;
                var entry = new ProcessEntry {
                    Pid = observation.Pid, Name = String.IsNullOrWhiteSpace(observation.Name) ? "Unknown process" : observation.Name,
                    WorkingSetBytes = observation.WorkingSetBytes >= 0 ? observation.WorkingSetBytes : null
                };
                if (observation.StartedUtc.HasValue && observation.CpuTime.HasValue)
                {
                    var key = Tuple.Create(observation.Pid, observation.StartedUtc.Value.Ticks);
                    TimeSpan old;
                    if (primed && previous.TryGetValue(key, out old))
                    {
                        double used = (observation.CpuTime.Value - old).TotalSeconds;
                        if (used >= 0) entry.CpuPercent = Math.Min(100, 100 * used / (elapsed * processorCount));
                    }
                    next[key] = observation.CpuTime.Value;
                }
                snapshot.Entries.Add(entry);
            }
            previous = next; previousAt = at;
            return snapshot;
        }
    }

    internal static class ProcessRanking
    {
        public static List<ProcessEntry> Top(ProcessSnapshot snapshot, bool byMemory, int count)
        {
            bool memoryFirst = byMemory || snapshot.MeasuringCpu;
            var entries = snapshot.Entries.AsEnumerable();
            return (memoryFirst
                ? entries.OrderBy(e => !e.WorkingSetBytes.HasValue).ThenByDescending(e => e.WorkingSetBytes.GetValueOrDefault()).ThenBy(e => e.Pid)
                : entries.OrderBy(e => !e.CpuPercent.HasValue).ThenByDescending(e => e.CpuPercent.GetValueOrDefault()).ThenBy(e => e.Pid))
                .Take(count).ToList();
        }
    }
}
