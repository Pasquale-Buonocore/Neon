using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Neon
{
    internal static class StorageTests
    {
        private sealed class CancelOnFirstFile : IProgress<StorageProgress>
        {
            private readonly CancellationTokenSource stop;
            internal CancelOnFirstFile(CancellationTokenSource stop) { this.stop = stop; }
            public void Report(StorageProgress progress) { if (progress.Files > 0) stop.Cancel(); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string link, string existing, IntPtr security);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(string link, string target, uint flags);

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Storage verification failed: " + message); }

        internal static string Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "NEON storage test " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string large = Path.Combine(root, "Large folder");
                string nested = Path.Combine(large, "Nested folder");
                string small = Path.Combine(root, "Small folder");
                Directory.CreateDirectory(nested); Directory.CreateDirectory(small);
                File.WriteAllBytes(Path.Combine(large, "large.bin"), new byte[4096]);
                File.WriteAllBytes(Path.Combine(nested, "nested.bin"), new byte[2048]);
                File.WriteAllBytes(Path.Combine(small, "small.bin"), new byte[32]);
                for (int i = 0; i < 60; i++)
                {
                    string sibling = Path.Combine(root, "Sibling " + i.ToString("00"));
                    Directory.CreateDirectory(sibling);
                    File.WriteAllBytes(Path.Combine(sibling, "data.bin"), new byte[1]);
                    File.WriteAllBytes(Path.Combine(small, "small " + i.ToString("00") + ".bin"), new byte[1]);
                }
                bool hardLink = CreateHardLink(Path.Combine(root, "linked.bin"), Path.Combine(large, "large.bin"), IntPtr.Zero);
                string loop = Path.Combine(root, "linked folder");
                bool reparse = CreateSymbolicLink(loop, root, 3);
                if (reparse) Check((File.GetAttributes(loop) & FileAttributes.ReparsePoint) != 0, "created link has reparse attribute");
                var volume = new StorageVolume { Id = root, Root = root, MediaType = "Type unverified" };
                var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { StorageApplications.Normalize(large), "Sample app" } };
                var scan = new StorageScanner().ScanAsync(volume, null, CancellationToken.None, apps).GetAwaiter().GetResult();
                Check(scan.State == (reparse ? StorageScanState.Partial : StorageScanState.Complete), "complete or excluded scan state: " + scan.State);
                if (reparse) Check(scan.Reparse == 1 && scan.Inaccessible == 0, "reparse point classified separately");
                var rootResult = scan.RootFolder;
                Check(rootResult.DirectFolderCount == 62 && rootResult.Folders.Count == 50, "50 direct folders retained");
                Check(rootResult.Folders[0].Path == large && rootResult.Folders[0].Bytes == 6144, "largest root folder");
                Check(rootResult.Folders[0].AppName == "Sample app", "exact registered application location");
                Check(rootResult.Folders[0].Folders.Count == 1 && rootResult.Folders[0].Folders[0].Path == nested &&
                    rootResult.Folders[0].Folders[0].Bytes == 2048, "nested folder only in its parent level");
                Check(rootResult.Folders.All(f => f.Path != nested), "nested folder not duplicated at root");
                var smallResult = rootResult.Folders.First(f => f.Path == small);
                Check(smallResult.Bytes == 92 && smallResult.DirectFileCount == 61 && smallResult.Files.Count == 50,
                    "50 direct files retained and logical total includes omitted files");
                Check(smallResult.Files[0].Bytes == 32 && smallResult.Files.Last().Bytes == 1, "files sorted per folder");
                Check(scan.Files == (hardLink ? 124 : 123), "hard link counted as a separate logical entry");
                Check(rootResult.Bytes == 6144 + 92 + 60 + (hardLink ? 4096 : 0), "root total includes all measured entries");
                var presentation = new StorageFolder { Path = "example", Inaccessible = 1 };
                presentation.Folders.Add(new StorageFolder { Path = "large folder", Bytes = 20 });
                presentation.Folders.Add(new StorageFolder { Path = "unreadable", Bytes = 0, Inaccessible = 1 });
                presentation.Files.Add(new StorageItem { Path = "large file", Bytes = 30 });
                presentation.Files.Add(new StorageItem { Path = "empty file", Bytes = 0 });
                var visible = StoragePresentation.Items(presentation);
                Check(visible.Count == 2 && visible[0].Path == "large file" && visible[1].Path == "large folder",
                    "mixed list sorts files and folders together and hides exact zero sizes");
                Check(presentation.Inaccessible == 1 && StoragePresentation.Warning(new StorageScanResult { Inaccessible = 1 }).Contains("1 exclusions"),
                    "hidden zero folder retains exclusion warning");
                Check(StoragePresentation.PageSize(95, 30) == 3 && StoragePresentation.Pages(10, 3) == 4,
                    "page capacity uses available height and preserves last page");
                using (var stop = new CancellationTokenSource())
                {
                    stop.Cancel();
                    var canceled = new StorageScanner().ScanAsync(volume, null, stop.Token).GetAwaiter().GetResult();
                    Check(canceled.State == StorageScanState.Canceled && canceled.RootFolder.Incomplete, "cancellation retains incomplete root");
                }
                using (var stop = new CancellationTokenSource())
                {
                    var partial = new StorageScanner().ScanAsync(volume, new CancelOnFirstFile(stop), stop.Token, apps, 0).GetAwaiter().GetResult();
                    Check(partial.State == StorageScanState.Canceled && partial.Files > 0 && partial.RootFolder.Bytes > 0 &&
                        partial.RootFolder.Incomplete, "cancellation retains measured branches and marks totals incomplete");
                }
                var missing = new StorageVolume { Id = "missing", Root = Path.Combine(root, "removed") };
                Check(new StorageScanner().ScanAsync(missing, null, CancellationToken.None).GetAwaiter().GetResult().State ==
                    StorageScanState.Unavailable, "unavailable volume state");
                var found = StorageVolumeCatalog.Read();
                Check(found.All(v => v.Id.Length > 0 && v.Root.Length > 0), "volume identity and mount");
                Check(found.Any(v => String.Equals(v.Root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)),
                    "current local volume listed");
                return "Storage OK: nested totals, top order, spaced paths, hard link " + (hardLink ? "checked" : "unavailable") +
                    ", reparse point " + (reparse ? "checked" : "unavailable") + ", cancel, unavailable volume, local catalog.";
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
