using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace Neon
{
    internal sealed class SpeedTestWindow : Window
    {
        private readonly Border frame;
        private readonly SpeedGauge gauge;
        private readonly TextBlock status, speed, download, upload, latency, server;
        private readonly Button start, cancel;
        private readonly Func<IProgress<SpeedTestProgress>, CancellationToken, Task<SpeedTestResult>> run;
        private DesktopLayer desktopLayer;
        private CancellationTokenSource active;
        private string phase = "idle";
        private bool hasResult;
        internal FrameworkElement Visual { get { return frame; } }
        internal bool IsTesting { get { return active != null; } }

        private static Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        private TextBlock Label(string value, double size, string resource)
        {
            var result = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
            result.SetResourceReference(TextBlock.ForegroundProperty, resource); return result;
        }
        private static Button Button(string value, Action action)
        { var button = new Button { Content = value }; button.Click += delegate { action(); }; return button; }

        internal SpeedTestWindow(Preferences preferences = null,
            Func<IProgress<SpeedTestProgress>, CancellationToken, Task<SpeedTestResult>> testRunner = null)
        {
            run = testRunner ?? new MlabSpeedTest().RunAsync;
            Title = "NEON // Internet speed test"; Icon = BrandIcon.Window();
            Width = 320; Height = 470; MinWidth = 300;
            MinHeight = Math.Min(330, SystemParameters.WorkArea.Height - 24);
            MaxHeight = SystemParameters.WorkArea.Height - 24;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false; ShowActivated = false; Topmost = false; UseLayoutRounding = true;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; FontFamily = new FontFamily("Segoe UI"); FontSize = 11;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SourceInitialized += delegate { desktopLayer = new DesktopLayer(this); };
            IsVisibleChanged += delegate { KeepOnDesktop(); };
            Closed += delegate { CancelTest(); if (desktopLayer != null) desktopLayer.Dispose(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Neon.SettingsStyles"))
                Resources = (ResourceDictionary)XamlReader.Load(stream);
            Resources.MergedDictionaries.Add(ControlStyles.Load());
            Resources[typeof(Button)] = Resources["NeonButton"];
            frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10) };
            frame.SetResourceReference(Border.BackgroundProperty, "SettingsSurface");
            frame.SetResourceReference(Border.BorderBrushProperty, "SettingsLine"); Content = frame;
            var layout = new DockPanel(); frame.Child = layout;
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            var close = Button("×", Close); close.ToolTip = "Close speed test";
            close.Style = (Style)Resources["NeonIconButton"]; close.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            var heading = new StackPanel(); heading.Children.Add(Label("NEON / SPEED TEST", 9, "SettingsAccent"));
            heading.Children.Add(Label("Internet speed test", 20, "SettingsText")); header.Children.Add(heading);
            heading.MouseLeftButtonDown += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
            DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
            var footer = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            start = Button("Start speed test", delegate { RunTest(); }); actions.Children.Add(start);
            cancel = Button("Cancel", CancelTest); cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsEnabled = false; actions.Children.Add(cancel); footer.Children.Add(actions);
            DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
            var body = new StackPanel();
            body.Children.Add(Label("A test can transfer up to about 100 MiB in each direction, plus network overhead. M-Lab collects your public IP and publishes measurement data. Start only if you accept its data policy.", 11, "SettingsMuted"));
            var policy = new TextBlock { Margin = new Thickness(0, 3, 0, 2), FontSize = 11, Cursor = Cursors.Hand,
                Text = "Read M-Lab's data policy" }; policy.SetResourceReference(TextBlock.ForegroundProperty, "SettingsAccent");
            policy.MouseLeftButtonUp += delegate { try { Process.Start(new ProcessStartInfo("https://www.measurementlab.net/privacy/") { UseShellExecute = true }); } catch { } };
            body.Children.Add(policy);
            gauge = new SpeedGauge { Height = 94, Margin = new Thickness(0, 3, 0, 0) }; body.Children.Add(gauge);
            var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 4) };
            speed = Label("0.0 Mbps", 22, "SettingsText"); speed.FontWeight = FontWeights.SemiBold; speed.TextAlignment = TextAlignment.Center;
            center.Children.Add(speed); body.Children.Add(center);
            var metrics = new Grid(); for (int i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
            AddMetric(metrics, 0, "DOWNLOAD", out download); AddMetric(metrics, 1, "UPLOAD", out upload); AddMetric(metrics, 2, "LATENCY", out latency);
            body.Children.Add(metrics);
            status = Label("Ready to test", 11, "SettingsMuted"); status.Margin = new Thickness(0, 7, 0, 3); body.Children.Add(status);
            server = Label("", 10, "SettingsMuted"); body.Children.Add(server);
            layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) });
            ApplyAppearance(preferences ?? new Preferences());
        }

        private void AddMetric(Grid grid, int column, string title, out TextBlock value)
        {
            var stack = new StackPanel(); stack.Children.Add(Label(title, 9, "SettingsAccent"));
            value = Label("—", 11, "SettingsText"); value.FontWeight = FontWeights.SemiBold;
            stack.Children.Add(value); Grid.SetColumn(stack, column); grid.Children.Add(stack);
        }
        internal void ApplyAppearance(Preferences preferences)
        {
            var palette = ThemePalette.Find(preferences.Theme);
            Resources["SettingsText"] = Brush(palette.Text); Resources["SettingsMuted"] = Brush(palette.Muted);
            Resources["SettingsAccent"] = Brush(palette.Primary); Resources["SettingsLine"] = Brush(palette.Line);
            Resources["SettingsWarning"] = Brush(palette.Warning);
            Resources["ControlText"] = Resources["SettingsText"]; Resources["ControlLine"] = Resources["SettingsLine"];
            Resources["ControlAccent"] = Resources["SettingsAccent"];
            var surface = ((SolidColorBrush)Brush(palette.Surface)).Color;
            surface.A = (byte)(255 * preferences.Background);
            Resources["SettingsSurface"] = new SolidColorBrush(surface);
            var accent = ((SolidColorBrush)Brush(palette.Primary)).Color;
            var button = new SolidColorBrush(accent) { Opacity = preferences.Background * 0.14 };
            var selected = new SolidColorBrush(accent) { Opacity = preferences.Background * 0.24 };
            Resources["ControlSurface"] = button; Resources["SettingsButton"] = button; Resources["SettingsSelected"] = selected;
            gauge.DownloadColor = accent; gauge.UploadColor = ((SolidColorBrush)Brush(palette.Secondary)).Color;
            gauge.TrackColor = ((SolidColorBrush)Brush(palette.Line)).Color; gauge.InvalidateVisual();
        }
        internal void KeepOnDesktop() { if (desktopLayer != null) desktopLayer.KeepBehind(); }
        internal async void RunTest()
        {
            if (active != null) return;
            active = new CancellationTokenSource(); var token = active.Token;
            hasResult = false; phase = "finding"; gauge.Value = 0; gauge.Upload = false;
            speed.Text = "0.0 Mbps"; download.Text = upload.Text = latency.Text = "—"; server.Text = "";
            start.IsEnabled = false; cancel.IsEnabled = true; status.Text = "Finding an M-Lab server…";
            try
            {
                var progress = new Progress<SpeedTestProgress>(p =>
                {
                    if (token.IsCancellationRequested || !IsLoaded) return;
                    phase = p.Phase; status.Text = p.Message; gauge.Upload = p.Phase == "upload";
                    gauge.Value = p.Mbps; gauge.InvalidateVisual(); speed.Text = p.Mbps.ToString("0.0") + " Mbps";
                    if (p.Phase == "download" || p.Phase == "download-done") download.Text = p.Mbps.ToString("0.0") + " Mbps";
                    if (p.Phase == "upload" || p.Phase == "upload-done") upload.Text = p.Mbps.ToString("0.0") + " Mbps";
                    if (p.Phase == "download-done" || p.Phase == "upload-done") hasResult = true;
                });
                var result = await run(progress, token);
                if (token.IsCancellationRequested) return;
                phase = "complete"; hasResult = true;
                download.Text = result.DownloadMbps.ToString("0.0") + " Mbps";
                upload.Text = result.UploadMbps.ToString("0.0") + " Mbps";
                latency.Text = result.LatencyMs.HasValue ? result.LatencyMs.Value.ToString("0.0") + " ms" : "—";
                speed.Text = result.UploadMbps.ToString("0.0") + " Mbps"; gauge.Upload = true;
                gauge.Value = result.UploadMbps; gauge.InvalidateVisual();
                status.Text = "Test complete · " + result.FinishedAt.ToString("g"); server.Text = "Server: " + result.Server;
            }
            catch (OperationCanceledException) { if (IsLoaded) status.Text = "Test canceled."; }
            catch (Exception ex) { if (IsLoaded) status.Text = FailureMessage(ex, phase, hasResult); }
            finally
            {
                if (IsLoaded) { start.IsEnabled = true; cancel.IsEnabled = false; }
                active.Dispose(); active = null;
            }
        }
        internal static string FailureMessage(Exception error, string phase, bool hasResult)
        {
            if (phase == "finding" && !hasResult)
            {
                if (error is TimeoutException) return "Connection timed out while finding M-Lab. No measurement was obtained.";
                var network = error as WebException;
                if (network != null && network.Status == WebExceptionStatus.NameResolutionFailure)
                    return "Cannot resolve the M-Lab server (DNS). Check your connection. No measurement was obtained.";
                if (network != null) return "Cannot connect to M-Lab. Check your connection. No measurement was obtained.";
                return "Could not start the test: " + error.Message + " No measurement was obtained.";
            }
            return "Test failed: " + error.Message + (hasResult ? " Completed phase results above are partial." : " No measurement was obtained.");
        }
        internal void CancelTest() { if (active != null) active.Cancel(); }
    }
}
