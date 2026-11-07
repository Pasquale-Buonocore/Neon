using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Neon
{
    internal sealed class StorageVolume
    {
        public string Id, Root, Mounts, Label, MediaType, Error;
        public long TotalBytes, FreeBytes;
        public int? DiskNumber;
        public long UsedBytes { get { return Math.Max(0, TotalBytes - FreeBytes); } }
        public string Display { get { return Root + (String.IsNullOrEmpty(Label) ? "" : "  " + Label) + " · " + MediaType; } }
    }

    internal static class StorageVolumeCatalog
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder name, int capacity);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetDriveType(string root);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceEx(string root, out ulong available, out ulong total, out ulong free);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumeInformation(string root, StringBuilder label, int labelSize,
            out uint serial, out uint maxComponent, out uint flags, StringBuilder fileSystem, int fileSystemSize);

        private sealed class PhysicalDisk
        {
            public int Number; public long Size; public ushort Media;
        }
        internal static List<StorageVolume> Read()
        {
            var paths = new List<Tuple<string, int?>>();
            var disks = new Dictionary<int, long>();
            var physical = new List<PhysicalDisk>();
            try
            {
                using (var search = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DiskNumber, AccessPaths FROM MSFT_Partition"))
                using (var rows = search.Get())
                    foreach (ManagementObject row in rows)
                    using (row)
                    {
                        int number = Convert.ToInt32(row["DiskNumber"]);
                        var mounts = row["AccessPaths"] as string[];
                        if (mounts == null) continue;
                        foreach (string mount in mounts)
                            if (!String.IsNullOrEmpty(mount) && !mount.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
                                paths.Add(Tuple.Create(mount, (int?)number));
                    }
                using (var search = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT Number, Size FROM MSFT_Disk"))
                using (var rows = search.Get())
                    foreach (ManagementObject row in rows) using (row)
                        disks[Convert.ToInt32(row["Number"])] = Convert.ToInt64(row["Size"]);
                using (var search = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, Size, MediaType FROM MSFT_PhysicalDisk"))
                using (var rows = search.Get())
                    foreach (ManagementObject row in rows)
                    using (row)
                    {
                        int number;
                        if (Int32.TryParse(Convert.ToString(row["DeviceId"]), out number))
                            physical.Add(new PhysicalDisk { Number = number, Size = Convert.ToInt64(row["Size"]), Media = Convert.ToUInt16(row["MediaType"]) });
                    }
            }
            catch (Exception) { paths.Clear(); disks.Clear(); physical.Clear(); }
            // The Storage Management provider may be unavailable. Keep local fixed drives selectable.
            foreach (var drive in DriveInfo.GetDrives())
                if (drive.DriveType == DriveType.Fixed && !paths.Any(p => String.Equals(p.Item1, drive.Name, StringComparison.OrdinalIgnoreCase)))
                    paths.Add(Tuple.Create(drive.Name, (int?)null));
            var volumes = new Dictionary<string, StorageVolume>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in paths.OrderBy(p => p.Item1.Length).ThenBy(p => p.Item1, StringComparer.OrdinalIgnoreCase))
            {
                string mount = pair.Item1;
                uint driveType = GetDriveType(mount);
                if (driveType != 3 && !(driveType == 0 && pair.Item2.HasValue)) continue;
                var guid = new StringBuilder(128);
                string id = GetVolumeNameForVolumeMountPoint(mount, guid, guid.Capacity) ? guid.ToString() : mount;
                StorageVolume existing;
                if (volumes.TryGetValue(id, out existing)) { existing.Mounts += ", " + mount; continue; }
                var volume = new StorageVolume { Id = id, Root = mount, Mounts = mount, DiskNumber = pair.Item2, MediaType = "Type unverified" };
                ulong available, total, free;
                if (GetDiskFreeSpaceEx(mount, out available, out total, out free))
                { volume.TotalBytes = total > Int64.MaxValue ? Int64.MaxValue : (long)total; volume.FreeBytes = free > Int64.MaxValue ? Int64.MaxValue : (long)free; }
                else volume.Error = "Volume unavailable";
                var label = new StringBuilder(256); var fileSystem = new StringBuilder(64);
                uint serial, maxComponent, flags;
                if (GetVolumeInformation(mount, label, label.Capacity, out serial, out maxComponent, out flags, fileSystem, fileSystem.Capacity)) volume.Label = label.ToString();
                long diskSize;
                if (pair.Item2.HasValue && disks.TryGetValue(pair.Item2.Value, out diskSize))
                {
                    var matches = physical.Where(p => p.Number == pair.Item2.Value && p.Size == diskSize).ToList();
                    if (matches.Count == 1) volume.MediaType = matches[0].Media == 4 ? "SSD" : matches[0].Media == 3 ? "HDD" : "Type unverified";
                }
                volumes[id] = volume;
            }
            return volumes.Values.OrderBy(v => v.Root, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    internal enum StorageScanState { Complete, Partial, Canceled, Unavailable, Failed }
    internal class StorageItem
    {
        public string Path, AppName;
        public long Bytes;
    }
    internal sealed class StorageFolder : StorageItem
    {
        public StorageFolder Parent;
        public long DirectFolderCount, DirectFileCount, Inaccessible, Reparse;
        public bool Incomplete;
        public readonly List<StorageFolder> Folders = new List<StorageFolder>();
        public readonly List<StorageItem> Files = new List<StorageItem>();
    }
    internal sealed class StorageProgress
    {
        public string Path;
        public long Files, Folders;
    }
    internal sealed class StorageScanResult
    {
        public string VolumeId, Root, Error;
        public StorageScanState State;
        public long Files, Folders, Inaccessible, Reparse;
        public DateTime FinishedAt;
        public StorageFolder RootFolder;
    }

    internal sealed class StorageScanner
    {
        private sealed class Frame
        {
            public StorageFolder Folder;
            public IEnumerator<string> Entries;
        }
        internal Task<StorageScanResult> ScanAsync(StorageVolume volume, IProgress<StorageProgress> progress, CancellationToken cancel,
            IDictionary<string, string> applicationPaths = null, int progressIntervalMs = 250)
        { return Task.Run(() => Scan(volume, progress, cancel, applicationPaths ?? StorageApplications.Read(), progressIntervalMs), CancellationToken.None); }

        private static void AddTop<T>(List<T> top, T item) where T : StorageItem
        {
            int index = top.FindIndex(existing => item.Bytes > existing.Bytes || item.Bytes == existing.Bytes &&
                String.Compare(item.Path, existing.Path, StringComparison.OrdinalIgnoreCase) < 0);
            if (index < 0) index = top.Count;
            if (index >= 50) return;
            top.Insert(index, item);
            if (top.Count > 50) top.RemoveAt(50);
        }
        private static StorageScanResult Scan(StorageVolume volume, IProgress<StorageProgress> progress, CancellationToken cancel,
            IDictionary<string, string> applicationPaths, int progressIntervalMs)
        {
            var result = new StorageScanResult { VolumeId = volume.Id, Root = volume.Root, State = StorageScanState.Complete,
                RootFolder = new StorageFolder { Path = volume.Root } };
            var stack = new Stack<Frame>();
            var clock = Stopwatch.StartNew();
            try
            {
                if (volume.Error != null || !Directory.Exists(volume.Root))
                { result.State = StorageScanState.Unavailable; result.Error = "Volume unavailable"; return result; }
                stack.Push(new Frame { Folder = result.RootFolder });
                while (stack.Count > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    var frame = stack.Peek();
                    if (frame.Entries == null)
                    {
                        try { frame.Entries = Directory.EnumerateFileSystemEntries(frame.Folder.Path).GetEnumerator(); result.Folders++; }
                        catch (Exception ex)
                        { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)) throw; if (!Directory.Exists(volume.Root)) { result.State = StorageScanState.Unavailable; result.Error = "Volume unavailable during scan"; break; } result.Inaccessible++; frame.Folder.Inaccessible++; Finish(stack, applicationPaths); continue; }
                    }
                    bool next;
                    try { next = frame.Entries.MoveNext(); }
                    catch (Exception ex)
                    { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)) throw; if (!Directory.Exists(volume.Root)) { result.State = StorageScanState.Unavailable; result.Error = "Volume unavailable during scan"; break; } result.Inaccessible++; frame.Folder.Inaccessible++; Finish(stack, applicationPaths); continue; }
                    if (!next) { Finish(stack, applicationPaths); continue; }
                    string path = frame.Entries.Current;
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { result.Reparse++; frame.Folder.Reparse++; continue; }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            frame.Folder.DirectFolderCount++;
                            stack.Push(new Frame { Folder = new StorageFolder { Path = path, Parent = frame.Folder } });
                        }
                        else
                        {
                            long size = new FileInfo(path).Length;
                            frame.Folder.Bytes += size; result.Files++; frame.Folder.DirectFileCount++;
                            AddTop(frame.Folder.Files, new StorageItem { Path = path, Bytes = size });
                        }
                    }
                    catch (Exception ex)
                    { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)) throw; result.Inaccessible++; frame.Folder.Inaccessible++; }
                    if (progress != null && clock.ElapsedMilliseconds >= progressIntervalMs)
                    { progress.Report(new StorageProgress { Path = path, Files = result.Files, Folders = result.Folders }); clock.Restart(); }
                }
                if (result.State == StorageScanState.Complete && result.Inaccessible + result.Reparse > 0) result.State = StorageScanState.Partial;
            }
            catch (OperationCanceledException) { result.State = StorageScanState.Canceled; }
            catch (Exception ex)
            { result.State = Directory.Exists(volume.Root) ? StorageScanState.Failed : StorageScanState.Unavailable; result.Error = ex.Message; }
            finally
            {
                while (stack.Count > 0) { stack.Peek().Folder.Incomplete = true; Finish(stack, applicationPaths); }
                result.FinishedAt = DateTime.Now;
                if (progress != null) progress.Report(new StorageProgress { Path = volume.Root, Files = result.Files, Folders = result.Folders });
            }
            return result;
        }
        private static void Finish(Stack<Frame> stack, IDictionary<string, string> applications)
        {
            var frame = stack.Pop(); if (frame.Entries != null) frame.Entries.Dispose();
            if (stack.Count == 0) return;
            var parent = stack.Peek().Folder; var folder = frame.Folder;
            parent.Bytes += folder.Bytes; parent.Inaccessible += folder.Inaccessible;
            parent.Reparse += folder.Reparse; parent.Incomplete |= folder.Incomplete;
            string app;
            applications.TryGetValue(StorageApplications.Normalize(folder.Path), out app);
            folder.AppName = app;
            AddTop(parent.Folders, folder);
        }
    }

    internal static class StorageApplications
    {
        internal static string Normalize(string path)
        { try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); } catch { return ""; } }
        internal static IDictionary<string, string> Read()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    try
                    {
                        using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (var key in uninstall.GetSubKeyNames())
                                try
                                {
                                    using (var entry = uninstall.OpenSubKey(key))
                                    {
                                        if (entry == null) continue;
                                        string path = Normalize(Convert.ToString(entry.GetValue("InstallLocation")));
                                        string name = Convert.ToString(entry.GetValue("DisplayName"));
                                        if (String.IsNullOrEmpty(path) || String.IsNullOrWhiteSpace(name)) continue;
                                        string existing;
                                        if (names.TryGetValue(path, out existing) && !String.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) names[path] = null;
                                        else if (!names.ContainsKey(path)) names[path] = name;
                                    }
                                }
                                catch (Exception) { }
                        }
                    }
                    catch (Exception) { }
            return names;
        }
    }
}
