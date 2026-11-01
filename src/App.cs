using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Neon
{
    internal static class App
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length >= 2 && args[0] == "--self-test") return Verify(args[1]);
                if (args.Length >= 2 && args[0] == "--snapshot") return SnapshotImage(args[1], args.Length >= 3 ? args[2] : null);
                if (args.Length >= 2 && args[0] == "--gallery") return ThemeGallery(args[1]);
                if (args.Length >= 2 && args[0] == "--menu-preview")
                {
                    var menuApp = new Application(); var menuWindow = new MonitorWindow(true);
                    SaveBitmap(RenderElement(menuWindow.MenuPreview()), args[1]);
                    menuWindow.Close(); menuApp.Shutdown(); return 0;
                }
                if (args.Length >= 2 && args[0] == "--icon-preview") { SaveBitmap(BrandIcon.Window(128), args[1]); return 0; }
                if (args.Length >= 2 && args[0] == "--ui-test")
                {
                    var testApp = new Application(); var testWindow = new MonitorWindow(true);
                    testWindow.ShowActivated = false; testWindow.Left = -32000; testWindow.Top = -32000;
                    testWindow.Show(); testWindow.UpdateLayout();
                    testWindow.VerifyInterface(); testWindow.Close(); testApp.Shutdown();
                    File.WriteAllText(args[1], "UI OK: metà altezza desktop, monitor piccolo senza tagli, compatta, pin, trasparenza, sensori mancanti, allarmi, unità multiple, unità inaccessibile/rimossa, sei temi e preferenze. Dimensioni finali: " + testWindow.ActualWidth.ToString("0") + " x " + testWindow.ActualHeight.ToString("0"));
                    return 0;
                }
                bool created;
                using (var mutex = new Mutex(true, "Local\\NeonPCMonitor.Desktop", out created))
                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\NeonPCMonitor.Show"))
                {
                    if (!created) { show.Set(); return 0; }
                    var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    var window = new MonitorWindow(false);
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    timer.Tick += delegate { if (show.WaitOne(0)) window.Reveal(); }; timer.Start();
                    app.Run(window); timer.Stop();
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "neon-error.log"), ex.ToString()); } catch { }
                if (args.Length == 0) MessageBox.Show("NEON non è riuscito ad avviarsi.\n\n" + ex.Message, "NEON // Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        private static void Assert(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Verifica fallita: " + description);
        }
        private static int Verify(string path)
        {
            Assert(Telemetry.CpuUsage(40, 80, 20) == 60, "CPU: il tempo kernel include idle");
            Assert(!Telemetry.CpuUsage(0, 0, 0).HasValue, "CPU: campione vuoto non è 0%");
            Assert(!Telemetry.CpuUsage(-1, 2, 3).HasValue, "CPU: contatori azzerati");
            var rates = new RateCounter();
            Assert(rates.Sample(new Dictionary<string, long[]> { { "a", new long[] { 100, 200 } } }, 1)[0] == 0, "Rete: primo campione");
            var next = rates.Sample(new Dictionary<string, long[]> { { "a", new long[] { 300, 300 } } }, 3);
            Assert(next[0] == 100 && next[1] == 50, "Rete: byte/s con intervallo effettivo");
            Assert(rates.Sample(new Dictionary<string, long[]> { { "a", new long[] { 1, 1 } }, { "b", new long[] { 999999, 999999 } } }, 4)[0] == 0, "Rete: reset e nuova interfaccia");
            Assert(rates.Sample(new Dictionary<string, long[]>(), 5)[0] == 0, "Rete: disconnessione");
            Assert(MonitorWindow.Speed(1048576).EndsWith("MiB/s"), "Unità rete binarie");
            using (var icon = BrandIcon.Tray()) Assert(icon.Width == 16 && icon.Height == 16, "Icona area di notifica a 16 pixel");
            Assert(BrandIcon.Window(128).PixelWidth == 128, "Icona ad alta risoluzione incorporata");
            var reader = new Telemetry(); reader.Read(); Thread.Sleep(1100); var sample = reader.Read();
            Assert(sample.CoreAvailable && sample.RamTotal > 0 && sample.RamUsed <= sample.RamTotal, "Memoria fisica reale");
            Assert(sample.Cpu >= 0 && sample.Cpu <= 100, "CPU reale");
            Assert(sample.Disks.Any(d => d.IsSystem && d.Total > 0) && sample.Disks.All(d => d.Used <= d.Total), "Dischi reali");
            var expected = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).Select(d => d.Name).OrderBy(n => n);
            Assert(expected.SequenceEqual(sample.Disks.Select(d => d.Name).OrderBy(n => n)), "Tutte le unità fisse incluse");
            Assert(sample.Download >= 0 && sample.Upload >= 0 && sample.Uptime.TotalSeconds > 0, "Rete e uptime reali");
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(new { passed = 15, sample = sample }));
            return 0;
        }

        private static int SnapshotImage(string path, string theme)
        {
            var app = new Application();
            var window = new MonitorWindow(true);
            if (theme != null) window.SetTheme(theme);
            var reader = new Telemetry(); reader.Read(); Thread.Sleep(1100);
            // This image contains actual readings; histories are never fabricated.
            window.Update(reader.Read());
            SaveBitmap(RenderWindow(window), path);
            window.Close(); app.Shutdown(); return 0;
        }

        private static RenderTargetBitmap RenderWindow(MonitorWindow window)
        {
            return RenderElement(window.Visual);
        }
        internal static RenderTargetBitmap RenderElement(FrameworkElement root)
        {
            root.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double width = root.DesiredSize.Width;
            double height = root.DesiredSize.Height;
            root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            return bitmap;
        }
        private static void SaveBitmap(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
        private static int ThemeGallery(string path)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var reader = new Telemetry(); reader.Read(); Thread.Sleep(1100); var sample = reader.Read();
            var drawing = new DrawingVisual();
            const double scale = 0.90, tileWidth = 346, tileHeight = 300;
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 15, 23)), null, new Rect(0, 0, tileWidth * 3, tileHeight * 2));
                for (int i = 0; i < ThemePalette.All.Length; i++)
                {
                    var window = new MonitorWindow(true); window.SetTheme(ThemePalette.All[i].Id); window.CompactPreview(); window.Update(sample);
                    var bitmap = RenderWindow(window);
                    double x = (i % 3) * tileWidth + 10, y = (i / 3) * tileHeight;
                    context.DrawImage(bitmap, new Rect(x, y, bitmap.PixelWidth * scale, bitmap.PixelHeight * scale));
                    window.Close();
                }
            }
            var gallery = new RenderTargetBitmap((int)tileWidth * 3, (int)tileHeight * 2, 96, 96, PixelFormats.Pbgra32);
            gallery.Render(drawing); SaveBitmap(gallery, path); app.Shutdown(); return 0;
        }
    }
}
