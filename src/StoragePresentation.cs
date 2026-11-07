using System;
using System.Collections.Generic;
using System.Linq;

namespace Neon
{
    internal static class StoragePresentation
    {
        internal static List<StorageItem> Items(StorageFolder folder)
        {
            if (folder == null) return new List<StorageItem>();
            return folder.Folders.Cast<StorageItem>().Concat(folder.Files).Where(item => item.Bytes > 0)
                .OrderByDescending(item => item.Bytes).ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }
        internal static int PageSize(double height, double rowHeight)
        { return Math.Max(1, (int)Math.Floor(Math.Max(0, height) / rowHeight)); }
        internal static int Pages(int count, int size) { return Math.Max(1, (count + size - 1) / size); }
        internal static string Warning(StorageScanResult result)
        {
            if (result == null) return "";
            if (result.State == StorageScanState.Canceled) return "Incomplete · scan canceled";
            long excluded = result.Inaccessible + result.Reparse;
            return excluded == 0 ? "" : "Partial · " + excluded.ToString("N0") + " exclusions";
        }
    }
}
