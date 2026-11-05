using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Neon
{
    internal static class ProcessTests
    {
        private sealed class FakeSource : IProcessSource
        {
            public readonly Queue<IList<ProcessObservation>> Frames = new Queue<IList<ProcessObservation>>();
            public int LogicalProcessorCount { get { return 4; } }
            public IList<ProcessObservation> Read() { return Frames.Dequeue(); }
        }

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Processes: " + message); }

        private static ProcessObservation Observation(int pid, DateTime? started, double? cpuSeconds, long? memory)
        {
            return new ProcessObservation { Pid = pid, Name = "Process " + pid, StartedUtc = started,
                CpuTime = cpuSeconds.HasValue ? TimeSpan.FromSeconds(cpuSeconds.Value) : (TimeSpan?)null,
                WorkingSetBytes = memory };
        }

        public static string Run()
        {
            var started = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            double seconds = 1;
            var fake = new FakeSource();
            var sampler = new ProcessSampler(fake, () => seconds);
            fake.Frames.Enqueue(new[] { Observation(7, started, 5, 100), Observation(8, started, 2, 200) });
            var first = sampler.Read(1);
            Check(first.MeasuringCpu && first.Entries.All(e => !e.CpuPercent.HasValue), "first sample has no fabricated CPU rate");
            Check(ProcessRanking.Top(first, false, 8)[0].Pid == 8, "first CPU sort temporarily uses memory");
            seconds = 3;
            fake.Frames.Enqueue(new[] { Observation(7, started, 7, 100), Observation(8, started, 2.4, 200) });
            var second = sampler.Read(1);
            Check(!second.MeasuringCpu && Math.Abs(second.Entries[0].CpuPercent.Value - 25) < 0.01 && Math.Abs(second.Entries[1].CpuPercent.Value - 5) < 0.01,
                "CPU is a percentage of total logical capacity and measured elapsed time");
            Check(ProcessRanking.Top(second, false, 8)[0].Pid == 7 && ProcessRanking.Top(second, true, 8)[0].Pid == 8,
                "CPU and memory sorting select different leaders");
            seconds = 4;
            fake.Frames.Enqueue(new[] { Observation(7, started.AddSeconds(1), 0.1, 500), Observation(9, null, 10, null) });
            var churn = sampler.Read(1);
            Check(churn.Entries.Count == 2 && churn.Entries.All(e => !e.CpuPercent.HasValue) && churn.Entries.All(e => e.Pid != 8),
                "PID reuse, new processes and exits do not retain CPU or stale rows");
            Check(ProcessRanking.Top(churn, true, 8)[0].Pid == 7 && !churn.Entries[1].WorkingSetBytes.HasValue,
                "unavailable fields are independent and sorted last");
            seconds = 5;
            fake.Frames.Enqueue(new[] { Observation(7, started.AddSeconds(1), 0.3, null), Observation(9, null, 12, 1000) });
            var partial = sampler.Read(1);
            Check(partial.Entries[0].CpuPercent.HasValue && !partial.Entries[0].WorkingSetBytes.HasValue && !partial.Entries[1].CpuPercent.HasValue,
                "CPU remains available when memory fails, while inaccessible identity has N/A CPU");
            seconds = 6;
            fake.Frames.Enqueue(new[] { Observation(7, started.AddSeconds(1), 0.1, 200) });
            Check(!sampler.Read(1).Entries[0].CpuPercent.HasValue, "reset process CPU counter is unavailable");
            seconds = 7;
            fake.Frames.Enqueue(new[] { Observation(7, started.AddSeconds(1), 0.2, 200) });
            Check(!sampler.Read(2).Entries[0].CpuPercent.HasValue, "reopening the tab resets the CPU baseline");
            seconds = 8;
            fake.Frames.Enqueue(new[] { Observation(7, started.AddSeconds(1), 0.4, 200) });
            Check(sampler.Read(2).Entries[0].CpuPercent.HasValue, "second sample after reopening computes CPU");
            try { sampler.Read(2); throw new InvalidOperationException("Expected scan failure"); }
            catch (InvalidOperationException ex) { if (ex.Message == "Expected scan failure") throw; }
            seconds = 9;
            fake.Frames.Enqueue(new[] { Observation(7, started.AddSeconds(1), 0.5, 200) });
            Check(!sampler.Read(2).Entries[0].CpuPercent.HasValue, "a failed scan clears the old CPU baseline");
            foreach (double interval in new[] { 0.5, 1.0, 2.5, 10.0 })
            {
                var intervalSource = new FakeSource(); double time = 0;
                var intervalSampler = new ProcessSampler(intervalSource, () => time);
                intervalSource.Frames.Enqueue(new[] { Observation(1, started, 1, 100) });
                intervalSampler.Read(1); time = interval;
                intervalSource.Frames.Enqueue(new[] { Observation(1, started, 1 + interval, 100) });
                Check(Math.Abs(intervalSampler.Read(1).Entries[0].CpuPercent.Value - 25) < 0.01,
                    "CPU rate uses the configured interval " + interval);
            }
            var ties = new ProcessSnapshot();
            ties.Entries.Add(new ProcessEntry { Pid = 3, Name = "C", CpuPercent = 5, WorkingSetBytes = 50 });
            ties.Entries.Add(new ProcessEntry { Pid = 2, Name = "B", CpuPercent = 5, WorkingSetBytes = 50 });
            ties.Entries.Add(new ProcessEntry { Pid = 1, Name = "A", WorkingSetBytes = null });
            Check(ProcessRanking.Top(ties, false, 2).Select(e => e.Pid).SequenceEqual(new[] { 2, 3 }) &&
                ProcessRanking.Top(ties, true, 2).Select(e => e.Pid).SequenceEqual(new[] { 2, 3 }), "N/A sorts last and PID breaks ties");

            var live = new ProcessSampler(new WindowsProcessSource(), () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            int ownPid = Process.GetCurrentProcess().Id;
            var liveFirst = live.Read(1);
            Check(liveFirst.Entries.Any(e => e.Pid == ownPid && e.WorkingSetBytes > 0), "standard user can read current process memory");
            Thread.Sleep(600);
            var liveSecond = live.Read(1);
            Check(liveSecond.Entries.Any(e => e.Pid == ownPid && e.CpuPercent >= 0 && e.CpuPercent <= 100), "live process CPU is measured on the second sample");
            return "Processes OK: first sample, total-capacity CPU, memory sorting, process churn, PID reuse, access limits, counter reset, session reset and live sampling.";
        }
    }
}
