using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Neon
{
    public sealed class Sparkline : FrameworkElement
    {
        private readonly List<double?> values = new List<double?>();
        internal int SampleCount { get { return values.Count; } }
        public Color Color = Color.FromRgb(85, 255, 153);
        public Color GridColor = Color.FromArgb(30, 120, 220, 170);
        public double Scale = 100;
        public void Push(double? value) { values.Add(value); if (values.Count > 60) values.RemoveAt(0); InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight;
            if (w < 1 || h < 1) return;
            var grid = new Pen(new SolidColorBrush(GridColor), 0.5);
            for (int row = 1; row < 4; row++) dc.DrawLine(grid, new Point(0, h * row / 4), new Point(w, h * row / 4));
            var pen = new Pen(new SolidColorBrush(Color), 1.5);
            Point? previous = null;
            for (int i = 0; i < values.Count; i++)
            {
                if (!values[i].HasValue) { previous = null; continue; }
                var point = new Point(w * (60 - values.Count + i) / 59.0, h - 2 - Math.Min(1, Math.Max(0, values[i].Value / Math.Max(Scale, 1))) * (h - 4));
                if (previous.HasValue) dc.DrawLine(pen, previous.Value, point);
                previous = point;
            }
            if (previous.HasValue) dc.DrawEllipse(new SolidColorBrush(Color), null, previous.Value, 2, 2);
        }
    }

    public sealed class Metric
    {
        public Border Container;
        public TextBlock Value, Detail;
        public Sparkline Chart;
    }

    public sealed class DiskRow
    {
        public TextBlock Label, Detail;
        public Grid Track;
        public Border Fill;
        public DiskSnapshot Data;
    }

    internal sealed class ProcessRow
    {
        public Grid Container;
        public TextBlock Name, Pid, Cpu, Memory;
    }

    public sealed class MonitorWindow : Window
    {
        public const double PanelWidth = 350;
        private readonly SolidColorBrush Green = Brush("#70FFA7"), White = Brush("#E3FFED"), Muted = Brush("#8AB49C"), Amber = Brush("#FFC66D"), Cyan = Brush("#73DDEB");
        private readonly SolidColorBrush Line = Brush("#41634E"), Surface = Brush("#050E0A");
        private readonly List<Border> panels = new List<Border>();
        private readonly Dictionary<TextBlock, double> fontSizes = new Dictionary<TextBlock, double>();
        private readonly Telemetry telemetry = new Telemetry();
        private readonly Stopwatch processClock = Stopwatch.StartNew();
        private readonly ProcessSampler processSampler;
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly Preferences preferences;
        private readonly string settingsPath;
        private readonly bool preview;
        private bool reading, shuttingDown, positioning, positionQueued, dragging;
        private Forms.NotifyIcon tray;
        private System.Drawing.Icon trayIcon;
        private DesktopLayer desktopLayer;
        private SettingsWindow settingsWindow;
        private SpeedTestWindow speedTestWindow;
        private double workAreaHeight = SystemParameters.WorkArea.Height, workAreaWidth = SystemParameters.WorkArea.Width;
        private Border shell;
        private Viewbox viewport;
        private StackPanel details;
        private StackPanel monitorContent;
        private FrameworkElement processesContent, storageContent;
        private readonly List<ProcessRow> processRows = new List<ProcessRow>();
        private TextBlock processNote;
        private TextBlock processTotalCpu, processTotalRam;
        private Button cpuSortButton, memorySortButton;
        private ProcessSnapshot latestProcesses;
        private bool sortProcessesByMemory, processReading, processRefreshPending;
        private int processSession;
        private readonly List<Button> tabButtons = new List<Button>();
        private int selectedTab;
        private TextBlock cpuModel, gpuModel, status, download, upload;
        private Border statusBox;
        private StackPanel diskList;
        private TextBlock emptyDiskNotice;
        private readonly Dictionary<string, DiskRow> diskRows = new Dictionary<string, DiskRow>();
        private string diskLayout = "";
        private Sparkline downloadChart, uploadChart;
        private Metric cpu, ram, gpu, temperature;
        public FrameworkElement Visual { get { return viewport; } }
        public Snapshot Latest { get; private set; }

        public MonitorWindow(bool isPreview)
        {
            preview = isPreview;
            processSampler = new ProcessSampler(new WindowsProcessSource(), () => processClock.Elapsed.TotalSeconds);
            settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "data", "settings.json");
            preferences = ReadPreferences();
            Title = "NEON // PC Monitor"; SizeToContent = SizeToContent.WidthAndHeight;
            Icon = BrandIcon.Window();
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI"); Foreground = White;
            Topmost = false; ShowActivated = false; UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Resources["Accent"] = Green; Resources["TextColor"] = White; Resources["MutedColor"] = Muted;
            Resources["LineColor"] = Line; Resources["SurfaceColor"] = Surface;
            Resources.MergedDictionaries.Add(ControlStyles.Load());
            Resources[typeof(Button)] = Resources["NeonIconButton"];
            Resources["ControlText"] = White; Resources["ControlSurface"] = Brushes.Transparent;
            Resources["ControlLine"] = Line; Resources["ControlAccent"] = Green;
            BuildInterface(); ApplyPreferences();
            SourceInitialized += delegate { FitToDesktop(); desktopLayer = new DesktopLayer(this); };
            IsVisibleChanged += delegate { if (desktopLayer != null) desktopLayer.KeepBehind(); };
            Closed += delegate { if (settingsWindow != null) settingsWindow.Close(); if (desktopLayer != null) desktopLayer.Dispose(); };
            if (!preview)
            {
                SetupTray();
                Left = SystemParameters.WorkArea.Right - PanelWidth; Top = SystemParameters.WorkArea.Top;
                Loaded += async delegate { FitToDesktop(); UpdateLayout(); RestorePosition(); await Refresh(); timer.Start(); };
                timer.Interval = TimeSpan.FromSeconds(preferences.RefreshSeconds);
                timer.Tick += async delegate { if (selectedTab == 1) RefreshProcesses(); await Refresh(); };
                Closing += delegate { shuttingDown = true; processSession++; timer.Stop(); if (settingsWindow != null) settingsWindow.Close(); if (speedTestWindow != null) speedTestWindow.Close(); SavePreferences(); if (shell.ContextMenu != null) shell.ContextMenu.IsOpen = false; if (tray != null) tray.Dispose(); if (trayIcon != null) trayIcon.Dispose(); };
                LocationChanged += delegate { if (IsLoaded && !positioning) { FitToDesktop(); if (dragging) { preferences.Left = Left; preferences.Top = Top; } PositionCompanionWindows(); } };
                SizeChanged += delegate { QueuePosition(); };
            }
        }

        // A binding keeps shared palette brushes mutable when WPF seals styles.
        private static SolidColorBrush Brush(string color)
        {
            var brush = new SolidColorBrush(); Tint(brush, color); return brush;
        }
        private static void Tint(SolidColorBrush brush, string color)
        {
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding { Source = (Color)ColorConverter.ConvertFromString(color) });
        }
        private TextBlock Text(string value, double size, Brush color, bool mono)
        {
            var text = new TextBlock { Text = value, FontSize = size * preferences.FontScale, Foreground = color, FontFamily = new FontFamily(mono ? "Consolas" : "Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis };
            fontSizes[text] = size; return text;
        }
        private static void Place(Grid grid, UIElement child, int row, int column) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
        private Button ActionButton(string text, string tip, Action action)
        {
            var button = new Button { Content = text, ToolTip = tip, Margin = new Thickness(3, 0, 0, 0) };
            button.Click += delegate { action(); }; return button;
        }

        private void BuildInterface()
        {
            shell = new Border { Width = PanelWidth, Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(12), BorderBrush = Line, BorderThickness = new Thickness(1) };
            viewport = new Viewbox { Child = shell, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxHeight = SystemParameters.WorkArea.Height / 2 };
            Content = viewport;
            var body = new StackPanel(); shell.Child = body;
            var headerPanel = CreatePanel(new Thickness(0)); headerPanel.Margin = new Thickness(0, 0, 0, 8);
            var headerBody = new StackPanel(); headerPanel.Child = headerBody; body.Children.Add(headerPanel);
            var header = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brand = Text("●  NEON / SYS", 10, Green, true); brand.VerticalAlignment = VerticalAlignment.Center;
            brand.ToolTip = "Drag here to move the widget. Right-click to personalize.";
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.OriginalSource is TextBlock || e.OriginalSource == header)
                    try {
                        dragging = true; DragMove();
                        var area = CurrentWorkArea();
                        preferences.DockToCorner = Math.Abs(Left + ActualWidth - area.Right) <= 10 && Math.Abs(Top - area.Top) <= 10;
                        RestorePosition(); SavePreferences();
                    } catch (InvalidOperationException) { }
                    finally { dragging = false; }
            };
            header.Children.Add(brand);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(ActionButton("⚙", "Open personalization menu", OpenOptions));
            actions.Children.Add(ActionButton("−", "Hide to notification area", delegate { Hide(); }));
            actions.Children.Add(ActionButton("×", "Close NEON", delegate { Close(); }));
            Place(header, actions, 0, 1); headerBody.Children.Add(header);
            status = Text("●  CONNECTING TO SYSTEM…", 10, Green, true);
            statusBox = new Border { Padding = new Thickness(7, 4, 7, 4), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = status }; headerBody.Children.Add(statusBox);
            cpuModel = Text("", 10, Muted, false); cpuModel.Visibility = Visibility.Collapsed; headerBody.Children.Add(cpuModel);
            var tabs = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                tabs.ColumnDefinitions.Add(new ColumnDefinition());
                var button = new Button { Content = new[] { "Monitor", "Processes", "Storage" }[i],
                    Style = (Style)Resources["NeonButton"], FontFamily = new FontFamily("Consolas"), FontSize = 10,
                    Margin = new Thickness(i == 0 ? 0 : 3, 0, i == 2 ? 0 : 3, 0), Padding = new Thickness(2, 3, 2, 3),
                    ToolTip = "Show " + new[] { "Monitor", "Processes", "Storage" }[i] + " tab" };
                System.Windows.Automation.AutomationProperties.SetName(button, (string)button.Content + " tab");
                button.Click += delegate { SelectTab(index); };
                button.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                    int target = e.Key == Key.Right ? (index + 1) % 3 : e.Key == Key.Left ? (index + 2) % 3 :
                        e.Key == Key.Home ? 0 : e.Key == Key.End ? 2 : -1;
                    if (target >= 0) { SelectTab(target); tabButtons[target].Focus(); e.Handled = true; }
                };
                Grid.SetColumn(button, i); tabs.Children.Add(button); tabButtons.Add(button);
            }
            body.Children.Add(tabs);
            var views = new Grid(); body.Children.Add(views);
            monitorContent = new StackPanel(); views.Children.Add(monitorContent);
            var cards = new Grid();
            cards.ColumnDefinitions.Add(new ColumnDefinition()); cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.RowDefinitions.Add(new RowDefinition()); cards.RowDefinitions.Add(new RowDefinition());
            cpu = CreateMetric("01 / CPU", Green); ram = CreateMetric("02 / RAM", Cyan);
            gpu = CreateMetric("03 / GPU", Green); temperature = CreateMetric("04 / CPU TEMP", Amber);
            cpu.Container.Margin = new Thickness(0, 0, 3, 6); ram.Container.Margin = new Thickness(3, 0, 0, 6);
            gpu.Container.Margin = new Thickness(0, 0, 3, 0); temperature.Container.Margin = new Thickness(3, 0, 0, 0);
            Place(cards, cpu.Container, 0, 0); Place(cards, ram.Container, 0, 1); Place(cards, gpu.Container, 1, 0); Place(cards, temperature.Container, 1, 1); monitorContent.Children.Add(cards);
            details = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; monitorContent.Children.Add(details);
            var networkPanel = CreatePanel(new Thickness(0)); networkPanel.Margin = new Thickness(0, 0, 0, 8);
            var networkBody = new StackPanel(); networkPanel.Child = networkBody; details.Children.Add(networkPanel);
            gpuModel = Text("", 10, Muted, false); gpuModel.Visibility = Visibility.Collapsed; networkBody.Children.Add(gpuModel);
            networkBody.Children.Add(SectionTitle("NETWORK", "↓ / ↑"));
            var networkGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) }; networkGrid.ColumnDefinitions.Add(new ColumnDefinition()); networkGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var down = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; var up = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            down.Children.Add(Text("↓ DOWNLOAD", 9, Muted, true)); download = Text("—", 15, Green, true); down.Children.Add(download);
            downloadChart = new Sparkline { Height = 12, Margin = new Thickness(0, 2, 0, 0) }; down.Children.Add(downloadChart);
            up.Children.Add(Text("↑ UPLOAD", 9, Muted, true)); upload = Text("—", 15, Cyan, true); up.Children.Add(upload);
            uploadChart = new Sparkline { Height = 12, Margin = new Thickness(0, 2, 0, 0), Color = ((SolidColorBrush)Cyan).Color }; up.Children.Add(uploadChart);
            networkGrid.Children.Add(down); Place(networkGrid, up, 0, 1); networkBody.Children.Add(networkGrid);
            var storagePanel = CreatePanel(new Thickness(0));
            var storageBody = new StackPanel(); storagePanel.Child = storageBody; details.Children.Add(storagePanel);
            storageBody.Children.Add(SectionTitle("LOCAL DRIVES", "STORAGE"));
            diskList = new StackPanel();
            storageBody.Children.Add(new ScrollViewer { Content = diskList, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            processesContent = CreateProcessView();
            storageContent = Placeholder("02 / STORAGE", "SSD usage and the largest space consumers will appear here.");
            processesContent.VerticalAlignment = VerticalAlignment.Top;
            storageContent.VerticalAlignment = VerticalAlignment.Top;
            views.Children.Add(processesContent); views.Children.Add(storageContent);
            UpdateTabs();
            shell.ContextMenu = CreateOptions();
        }

        private FrameworkElement Placeholder(string title, string description)
        {
            var panel = CreatePanel(new Thickness(10), true);
            var content = new StackPanel();
            content.Children.Add(Text(title, 10, Green, true));
            var message = Text(description, 11, Muted, false);
            message.TextWrapping = TextWrapping.Wrap; message.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(message); panel.Child = content;
            return new ScrollViewer { Content = panel, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        private FrameworkElement CreateProcessView()
        {
            var panel = CreatePanel(new Thickness(9), true);
            var content = new StackPanel(); panel.Child = content;
            var heading = new Grid();
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = Text("01 / PROCESSES", 10, Green, true); title.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(title);
            var sorts = new StackPanel { Orientation = Orientation.Horizontal };
            cpuSortButton = new Button { Content = "CPU", Style = (Style)Resources["NeonButton"], FontFamily = new FontFamily("Consolas"), FontSize = 10, Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0, 0, 3, 0), ToolTip = "Sort processes by CPU usage" };
            memorySortButton = new Button { Content = "Memory", Style = (Style)Resources["NeonButton"], FontFamily = new FontFamily("Consolas"), FontSize = 10, Padding = new Thickness(5, 2, 5, 2), ToolTip = "Sort processes by physical memory" };
            cpuSortButton.Click += delegate { sortProcessesByMemory = false; RenderProcesses(); };
            memorySortButton.Click += delegate { sortProcessesByMemory = true; RenderProcesses(); };
            sorts.Children.Add(cpuSortButton); sorts.Children.Add(memorySortButton);
            Place(heading, sorts, 0, 1); content.Children.Add(heading);
            var totals = new Grid { Margin = new Thickness(0, 7, 0, 2) };
            totals.ColumnDefinitions.Add(new ColumnDefinition()); totals.ColumnDefinitions.Add(new ColumnDefinition());
            var totalCpu = new StackPanel { Orientation = Orientation.Horizontal };
            totalCpu.Children.Add(Text("TOTAL CPU", 9, Muted, true));
            processTotalCpu = Text("—", 16, Green, true); processTotalCpu.Margin = new Thickness(7, 0, 0, 0);
            processTotalCpu.ToolTip = "Total PC CPU usage, also shown in Monitor."; totalCpu.Children.Add(processTotalCpu);
            totals.Children.Add(totalCpu);
            var totalRam = new StackPanel { Orientation = Orientation.Horizontal };
            totalRam.Children.Add(Text("TOTAL RAM", 9, Muted, true));
            processTotalRam = Text("—", 16, Cyan, true); processTotalRam.Margin = new Thickness(7, 0, 0, 0);
            processTotalRam.ToolTip = "Total physical memory usage, also shown in Monitor."; totalRam.Children.Add(processTotalRam);
            Place(totals, totalRam, 0, 1); content.Children.Add(totals);
            processNote = Text("Collecting processes…", 9, Muted, false);
            processNote.TextWrapping = TextWrapping.Wrap;
            processNote.Margin = new Thickness(0, 7, 0, 7); content.Children.Add(processNote);
            var list = new StackPanel();
            for (int i = 0; i < 8; i++)
            {
                var row = new ProcessRow();
                row.Container = new Grid { Margin = new Thickness(0, 0, 0, 5), Visibility = Visibility.Collapsed, Focusable = true };
                row.Container.ColumnDefinitions.Add(new ColumnDefinition());
                row.Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                row.Container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
                var identity = new StackPanel();
                row.Name = Text("", 11, White, true);
                row.Pid = Text("", 8, Muted, true);
                identity.Children.Add(row.Name); identity.Children.Add(row.Pid);
                row.Container.Children.Add(identity);
                row.Cpu = Text("", 10, Green, true); row.Cpu.TextAlignment = TextAlignment.Right;
                row.Cpu.VerticalAlignment = VerticalAlignment.Center; Place(row.Container, row.Cpu, 0, 1);
                row.Memory = Text("", 10, Cyan, true); row.Memory.TextAlignment = TextAlignment.Right;
                row.Memory.VerticalAlignment = VerticalAlignment.Center; Place(row.Container, row.Memory, 0, 2);
                processRows.Add(row); list.Children.Add(row.Container);
            }
            content.Children.Add(new ScrollViewer { Content = list, Height = 225, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            UpdateProcessSortButtons();
            return panel;
        }

        internal int SelectedTab { get { return selectedTab; } }
        internal void SelectTab(int index)
        {
            if (index < 0 || index >= tabButtons.Count) throw new ArgumentOutOfRangeException("index");
            int previous = selectedTab;
            if (previous != index && (previous == 1 || index == 1))
            {
                processSession++;
                if (previous == 1) processRefreshPending = false;
                if (index == 1) { latestProcesses = null; RenderProcesses(); }
            }
            selectedTab = index; UpdateTabs(); UpdateViewportWidth();
            shell.ContextMenu = CreateOptions();
            if (IsLoaded) { UpdateLayout(); RestorePosition(); }
            if (index == 1 && previous != 1 && !preview) RefreshProcesses(true);
        }
        private void UpdateTabs()
        {
            if (tabButtons.Count != 3 || monitorContent == null) return;
            monitorContent.Visibility = selectedTab == 0 ? Visibility.Visible : Visibility.Hidden;
            processesContent.Visibility = selectedTab == 1 ? Visibility.Visible : Visibility.Hidden;
            storageContent.Visibility = selectedTab == 2 ? Visibility.Visible : Visibility.Hidden;
            for (int i = 0; i < tabButtons.Count; i++)
            {
                bool active = selectedTab == i;
                tabButtons[i].Foreground = active ? Green : Muted;
                tabButtons[i].BorderBrush = active ? Green : Line;
                var color = Green.Color; color.A = (byte)(active ? 32 * preferences.Background : 0);
                tabButtons[i].Background = new SolidColorBrush(color);
                System.Windows.Automation.AutomationProperties.SetItemStatus(tabButtons[i], active ? "Selected" : "Not selected");
            }
        }

        private void UpdateProcessSortButtons()
        {
            if (cpuSortButton == null) return;
            foreach (var button in new[] { cpuSortButton, memorySortButton })
            {
                bool active = button == (sortProcessesByMemory ? memorySortButton : cpuSortButton);
                button.Content = button == cpuSortButton ? active ? "CPU ↓" : "CPU" : active ? "Memory ↓" : "Memory";
                button.Foreground = active ? Green : Muted;
                button.BorderBrush = active ? Green : Line;
                var color = Green.Color; color.A = (byte)(active ? 32 * preferences.Background : 0);
                button.Background = new SolidColorBrush(color);
                System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "Selected" : "Not selected");
            }
        }

        private static string ProcessMemory(long? bytes)
        {
            if (!bytes.HasValue) return "N/A";
            double mib = bytes.Value / 1048576.0;
            return mib >= 1024 ? (mib / 1024).ToString("0.0") + " GiB" : mib.ToString("0.0") + " MiB";
        }

        private void RenderProcesses()
        {
            if (processNote == null) return;
            UpdateProcessSortButtons();
            foreach (var row in processRows) row.Container.Visibility = Visibility.Collapsed;
            if (latestProcesses == null)
            {
                processNote.Text = "Collecting processes…"; processNote.ToolTip = null; return;
            }
            if (latestProcesses.Error != null)
            {
                processNote.Text = "Process scan unavailable. Retrying…";
                processNote.ToolTip = latestProcesses.Error; return;
            }
            if (latestProcesses.Entries.Count == 0)
            {
                processNote.Text = "No accessible processes found."; processNote.ToolTip = null; return;
            }
            processNote.Text = latestProcesses.Illustrative ? "Illustrative sample · CPU % of total PC capacity" :
                latestProcesses.MeasuringCpu && !sortProcessesByMemory ? "Measuring CPU · temporarily sorted by memory" :
                sortProcessesByMemory ? "Sorted by memory · CPU % of total PC" : "Sorted by CPU · CPU % of total PC";
            processNote.ToolTip = null;
            var top = ProcessRanking.Top(latestProcesses, sortProcessesByMemory, processRows.Count);
            for (int i = 0; i < top.Count; i++)
            {
                var entry = top[i]; var row = processRows[i];
                row.Name.Text = entry.Name; row.Name.ToolTip = entry.Name;
                row.Pid.Text = "PID " + entry.Pid;
                row.Cpu.Text = entry.CpuPercent.HasValue ? entry.CpuPercent.Value.ToString("0.0") + "%" : "N/A";
                row.Memory.Text = ProcessMemory(entry.WorkingSetBytes);
                row.Container.ToolTip = entry.Name + " · PID " + entry.Pid + "\nCPU " + row.Cpu.Text + " · RAM " + row.Memory.Text;
                System.Windows.Automation.AutomationProperties.SetName(row.Container, entry.Name + ", PID " + entry.Pid + ", CPU " + row.Cpu.Text + ", memory " + row.Memory.Text);
                row.Container.Visibility = Visibility.Visible;
            }
        }

        internal void PreviewProcesses(ProcessSnapshot sample)
        {
            if (!preview) throw new InvalidOperationException("Preview mode only.");
            sample.Illustrative = true; latestProcesses = sample; RenderProcesses();
        }

        private async void RefreshProcesses(bool onOpen = false)
        {
            if (preview || shuttingDown || selectedTab != 1) return;
            if (processReading) { if (onOpen) processRefreshPending = true; return; }
            processReading = true;
            int session = processSession;
            try
            {
                var sample = await Task.Run(() => processSampler.Read(session));
                if (!shuttingDown && selectedTab == 1 && session == processSession)
                { latestProcesses = sample; RenderProcesses(); }
            }
            catch (Exception ex)
            {
                if (!shuttingDown && selectedTab == 1 && session == processSession)
                { latestProcesses = new ProcessSnapshot { Error = ex.Message }; RenderProcesses(); }
            }
            finally
            {
                processReading = false;
                if (processRefreshPending && !shuttingDown && selectedTab == 1)
                { processRefreshPending = false; RefreshProcesses(); }
            }
        }

        private Grid SectionTitle(string title, string suffix)
        {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Text(title, 9, Muted, true)); Place(grid, Text(suffix, 8, Muted, true), 0, 1); return grid;
        }
        private Metric CreateMetric(string label, Brush accent)
        {
            var metric = new Metric();
            metric.Container = CreatePanel(new Thickness(8, 6, 8, 6), true);
            var stack = new StackPanel(); metric.Container.Child = stack;
            stack.Children.Add(Text(label, 9, Muted, true));
            metric.Value = Text("—", 24, accent, true); metric.Value.Margin = new Thickness(0, 1, 0, 0); stack.Children.Add(metric.Value);
            metric.Detail = Text("Waiting…", 9, Muted, true); stack.Children.Add(metric.Detail);
            metric.Chart = new Sparkline { Height = 12, Margin = new Thickness(0, 4, 0, 0), Color = ((SolidColorBrush)accent).Color }; stack.Children.Add(metric.Chart);
            metric.Container.ToolTip = "Last 60 samples. Gaps indicate unavailable readings.";
            return metric;
        }
        private Border CreatePanel(Thickness padding, bool outlined = false)
        {
            var panel = new Border { Padding = padding, CornerRadius = new CornerRadius(6), BorderBrush = Line, BorderThickness = new Thickness(outlined ? 1 : 0), Background = Brushes.Transparent };
            if (outlined) panels.Add(panel); return panel;
        }

        private ContextMenu CreateOptions()
        {
            var menu = CreateMenuChrome();
            menu.Items.Add(new MenuItem { Header = "NEON / PERSONALIZE", IsEnabled = false, FontSize = 10, Padding = new Thickness(8, 4, 8, 5) });
            var settings = Menu("Personalize…", OpenSettings, false); settings.Tag = "settings"; settings.Icon = MenuGlyph("M2,3H14 M2,8H14 M2,13H14 M5,1V5 M11,6V10 M6,11V15"); menu.Items.Add(settings);
            var templates = new MenuItem { Header = "Color themes", Tag = "themes", Icon = MenuGlyph("M1,1H6V6H1Z M10,1H15V6H10Z M1,10H6V15H1Z M10,10H15V15H10Z") };
            foreach (var palette in ThemePalette.All)
                templates.Items.Add(ThemeMenuItem(palette));
            menu.Items.Add(templates);
            var opacityMenu = new MenuItem { Header = "Background · " + (preferences.Background * 100).ToString("0") + "%", Tag = "opacity", Icon = MenuGlyph("M8,1 A7,7 0 1 0 8,15 A7,7 0 1 0 8,1 M8,1V15") };
            foreach (double opacity in new[] { 0.0, 0.35, 0.60, 0.85 })
            {
                double choice = opacity;
                string label = opacity == 0 ? "Transparent" : (opacity * 100).ToString("0") + "% opacity";
                var item = Menu(label, delegate { preferences.Background = choice; ApplyPreferences(); SavePreferences(); if (settingsWindow != null) settingsWindow.SyncSavedPreferences(preferences); }, Math.Abs(preferences.Background - choice) < 0.01, true);
                item.Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), BorderBrush = Muted, BorderThickness = new Thickness(1), Background = Green, Opacity = Math.Max(0.15, choice) };
                opacityMenu.Items.Add(item);
            }
            menu.Items.Add(opacityMenu);
            menu.Items.Add(new Separator { Style = (Style)menu.Resources[typeof(Separator)] });
            var desktopItem = Menu("Always on desktop", delegate { }, true); desktopItem.Tag = "desktop"; desktopItem.IsEnabled = false; desktopItem.Icon = MenuGlyph("M1,2H15V12H1Z M8,12V15 M4,15H12"); menu.Items.Add(desktopItem);
            var views = new MenuItem { Header = "Tabs" };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                views.Items.Add(Menu(new[] { "Monitor", "Processes", "Storage" }[i], delegate { SelectTab(index); }, selectedTab == i, true));
            }
            menu.Items.Add(views);
            menu.Items.Add(new Separator { Style = (Style)menu.Resources[typeof(Separator)] });
            menu.Items.Add(Menu("Internet speed test…", OpenSpeedTest, false));
            var info = Menu("Sensor information", ShowSensorInfo, false); info.Icon = MenuGlyph("M8,1 A7,7 0 1 0 8,15 A7,7 0 1 0 8,1 M8,7V12 M8,4V4.5"); menu.Items.Add(info);
            var visibility = Menu(IsVisible ? "Hide widget" : "Show widget", delegate { if (IsVisible) Hide(); else Reveal(); }, false); visibility.Icon = MenuGlyph("M1,8 Q8,-2 15,8 Q8,18 1,8 M8,5 A3,3 0 1 0 8,11 A3,3 0 1 0 8,5"); menu.Items.Add(visibility);
            var exit = Menu("Quit NEON", delegate { Close(); }, false); exit.Icon = MenuGlyph("M8,1V8 M4,3 A6,6 0 1 0 12,3"); menu.Items.Add(exit);
            return menu;
        }
        private ContextMenu CreateMenuChrome()
        {
            var menu = new ContextMenu();
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Neon.MenuStyles"))
                menu.Resources = (ResourceDictionary)XamlReader.Load(stream);
            menu.Resources["SurfaceColor"] = Surface; menu.Resources["TextColor"] = White; menu.Resources["LineColor"] = Line;
            menu.Resources["Accent"] = Green; menu.Resources["MutedColor"] = Muted;
            var hover = Green.Color; hover.A = 35; menu.Resources["MenuHoverColor"] = new SolidColorBrush(hover);
            menu.Style = (Style)menu.Resources[typeof(ContextMenu)];
            return menu;
        }
        private MenuItem ThemeMenuItem(ThemePalette palette)
        {
            string choice = palette.Id;
            var item = Menu(palette.Name, delegate { SetTheme(choice); }, preferences.Theme == choice, true);
            item.Tag = choice;
            item.Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Background = Brush(palette.Primary) };
            return item;
        }
        private FrameworkElement MenuGlyph(string data)
        {
            return new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Stroke = Muted, StrokeThickness = 1.3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 15, Height = 15, Stretch = Stretch.Uniform };
        }
        private static MenuItem Menu(string text, Action action, bool selected, bool checkable = false)
        {
            var item = new MenuItem { Header = text, IsCheckable = checkable, IsChecked = selected };
            item.Click += delegate { action(); }; return item;
        }
        private void OpenOptions()
        {
            shell.ContextMenu = CreateOptions();
            shell.ContextMenu.PlacementTarget = shell;
            shell.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            shell.ContextMenu.IsOpen = true; shell.ContextMenu.Focus();
        }
        private void OpenSpeedTest()
        {
            if (!preview && !IsVisible) Reveal();
            if (speedTestWindow != null) { speedTestWindow.KeepOnDesktop(); return; }
            if (settingsWindow != null) settingsWindow.Close();
            speedTestWindow = new SpeedTestWindow(preferences);
            var area = CurrentWorkArea();
            speedTestWindow.MinWidth = 100; speedTestWindow.MinHeight = 100;
            speedTestWindow.MaxWidth = area.Width; speedTestWindow.MaxHeight = area.Height;
            PositionCompanionWindows();
            if (preview) { speedTestWindow.Left = speedTestWindow.Top = -32000; }
            speedTestWindow.Closed += delegate { speedTestWindow = null; };
            speedTestWindow.Show();
        }
        public FrameworkElement MenuPreview()
        {
            var grid = new Grid { Background = Brush("#111A21"), Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var main = CreateOptions(); main.Margin = new Thickness(12); main.VerticalAlignment = VerticalAlignment.Top;
            var themes = CreateMenuChrome(); themes.MinWidth = 220; themes.Margin = new Thickness(0, 12, 12, 12); themes.VerticalAlignment = VerticalAlignment.Top;
            themes.Items.Add(new MenuItem { Header = "COLOR THEMES", IsEnabled = false, FontSize = 10, Padding = new Thickness(8, 4, 8, 5) });
            foreach (var palette in ThemePalette.All) themes.Items.Add(ThemeMenuItem(palette));
            grid.Children.Add(new Image { Source = App.RenderElement(main), Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Top });
            Place(grid, new Image { Source = App.RenderElement(themes), Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Top }, 0, 1); return grid;
        }
        private void ApplyPreferences()
        {
            var palette = ThemePalette.Find(preferences.Theme); preferences.Theme = palette.Id;
            Tint(Green, palette.Primary); Tint(Cyan, palette.Secondary);
            Tint(White, palette.Text); Tint(Muted, palette.Muted);
            Tint(Amber, palette.Warning); Tint(Line, palette.Line); Tint(Surface, palette.Surface);
            var background = Surface.Color; background.A = (byte)(255 * preferences.Background);
            shell.Background = new SolidColorBrush(background);
            foreach (var panel in panels)
            {
                var color = Surface.Color; color.A = (byte)(36 * preferences.Background);
                panel.Background = new SolidColorBrush(color); panel.InvalidateVisual();
            }
            foreach (var chart in new[] { cpu.Chart, ram.Chart, gpu.Chart, temperature.Chart, downloadChart, uploadChart })
            {
                chart.Color = chart == ram.Chart || chart == uploadChart ? Cyan.Color : chart == temperature.Chart ? Amber.Color : Green.Color;
                var gridColor = Line.Color; gridColor.A = 65; chart.GridColor = gridColor; chart.InvalidateVisual();
            }
            timer.Interval = TimeSpan.FromSeconds(preferences.RefreshSeconds);
            if (speedTestWindow != null) speedTestWindow.ApplyAppearance(preferences);
            Topmost = false;
            UpdateTabs();
            UpdateProcessSortButtons();
            UpdateViewportWidth();
            shell.ContextMenu = CreateOptions();
            if (desktopLayer != null) desktopLayer.KeepBehind();
        }
        public void SetTheme(string theme) { preferences.Theme = theme; ApplyPreferences(); SavePreferences(); if (settingsWindow != null) settingsWindow.SyncSavedPreferences(preferences); }
        private void OpenSettings()
        {
            if (!preview && !IsVisible) Reveal();
            if (settingsWindow != null) { if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal; settingsWindow.RefreshStartupState(); settingsWindow.KeepOnDesktop(); return; }
            if (speedTestWindow != null) speedTestWindow.Close();
            settingsWindow = new SettingsWindow(preferences, delegate(Preferences value) { return ApplyUserSettings(value, false); }, ApplyUserSettings,
                preview ? (IStartupRegistration)new PreviewStartupRegistration() : new WindowsStartupRegistration());
            var area = CurrentWorkArea();
            settingsWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            settingsWindow.MinWidth = Math.Min(settingsWindow.MinWidth, area.Width);
            settingsWindow.MinHeight = Math.Min(settingsWindow.MinHeight, Math.Max(200, area.Height - 24));
            settingsWindow.MaxWidth = area.Width; settingsWindow.MaxHeight = Math.Max(200, area.Height - 24);
            settingsWindow.Width = Math.Min(settingsWindow.Width, area.Width);
            settingsWindow.Height = Math.Min(settingsWindow.Height, settingsWindow.MaxHeight);
            PositionCompanionWindows();
            if (preview) { settingsWindow.ShowActivated = false; settingsWindow.ShowInTaskbar = false; settingsWindow.Left = settingsWindow.Top = -32000; }
            settingsWindow.Closed += delegate { settingsWindow = null; };
            settingsWindow.Show();
        }
        internal bool ApplyUserSettings(Preferences value)
        { return ApplyUserSettings(value, true); }
        internal bool ApplyUserSettings(Preferences value, bool persist)
        {
            var normalized = value.Copy(); normalized.Normalize();
            preferences.WidgetScale = normalized.WidgetScale; preferences.FontScale = normalized.FontScale;
            preferences.RefreshSeconds = normalized.RefreshSeconds; preferences.Background = normalized.Background; preferences.Theme = normalized.Theme;
            ApplyPreferences();
            if (IsLoaded) { UpdateLayout(); RestorePosition(); }
            return !persist || SavePreferences();
        }
        internal Preferences CurrentPreferences { get { return preferences.Copy(); } }
        internal double RefreshInterval { get { return timer.Interval.TotalSeconds; } }
        public void VerifyInterface()
        {
            if (!preview) throw new InvalidOperationException("UI verification requires preview mode without saving preferences.");
            if (selectedTab != 0 || tabButtons.Count != 3 || monitorContent.Visibility != Visibility.Visible ||
                statusBox.Visibility != Visibility.Visible || !status.Text.Contains("CONNECTING TO SYSTEM"))
                throw new InvalidOperationException("Monitor is not the initial tab.");
            int samples = cpu.Chart.SampleCount;
            SelectTab(1); Update(new Snapshot { CoreAvailable = true, Cpu = 27, RamTotal = 100, RamUsed = 30, RamPercent = 30 });
            if (selectedTab != 1 || monitorContent.Visibility != Visibility.Hidden || cpu.Chart.SampleCount <= samples ||
                statusBox.Visibility != Visibility.Visible || !status.Text.Contains("SYSTEM RUNNING") || status.Foreground != Green ||
                processTotalCpu.Text != cpu.Value.Text || processTotalRam.Text != ram.Value.Text)
                throw new InvalidOperationException("Processes tab stopped hidden monitor updates.");
            UpdateLayout();
            double measuringHeight = ActualHeight;
            var processExample = new ProcessSnapshot();
            for (int i = 0; i < 8; i++)
                processExample.Entries.Add(new ProcessEntry { Pid = 100 + i, Name = i == 0 ? "A very long illustrative process name" : "Example " + i,
                    CpuPercent = i == 0 ? 20 : i == 1 ? 5 : 1, WorkingSetBytes = (i == 1 ? 2048L : 100L) * 1048576 });
            PreviewProcesses(processExample); UpdateLayout();
            if (processRows.Count != 8 || processRows[7].Container.Visibility != Visibility.Visible ||
                ActualHeight > viewport.MaxHeight + 1 || Math.Abs(ActualHeight - measuringHeight) > 1 ||
                processRows[0].Name.Text != processExample.Entries[0].Name ||
                processRows[0].Name.ToolTip as string != processExample.Entries[0].Name || !processNote.Text.Contains("Illustrative"))
                throw new InvalidOperationException("The process list or its long-name tooltip is missing.");
            memorySortButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (processRows[0].Pid.Text != "PID 101" || !sortProcessesByMemory)
                throw new InvalidOperationException("Memory sorting did not update the process list.");
            cpuSortButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (processRows[0].Pid.Text != "PID 100" || sortProcessesByMemory)
                throw new InvalidOperationException("CPU sorting did not update the process list.");
            PreviewProcesses(new ProcessSnapshot { Error = "Process scan failed" });
            if (processRows.Any(row => row.Container.Visibility != Visibility.Collapsed) || !processNote.Text.Contains("unavailable"))
                throw new InvalidOperationException("A failed process scan left stale rows visible.");
            UpdateLayout();
            if (Math.Abs(ActualHeight - measuringHeight) > 1)
                throw new InvalidOperationException("The process list changed the window height while loading or retrying.");
            SelectTab(2);
            if (selectedTab != 2 || storageContent.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Storage tab is unavailable.");
            var tabMenu = shell.ContextMenu.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Header) == "Tabs");
            if (!((MenuItem)tabMenu.Items[2]).IsChecked || ((MenuItem)tabMenu.Items[0]).IsChecked ||
                System.Windows.Automation.AutomationProperties.GetItemStatus(tabButtons[2]) != "Selected")
                throw new InvalidOperationException("The selected tab is not reflected in navigation state.");
            Update(new Snapshot { CoreAvailable = true, CpuTemperature = 95, RamTotal = 100, RamUsed = 30, RamPercent = 30 });
            if (statusBox.Visibility != Visibility.Visible || !status.Text.Contains("HIGH TEMPERATURE") || status.Foreground != Amber)
                throw new InvalidOperationException("Warnings are not visible outside Monitor.");
            Update(new Snapshot { CoreAvailable = true, Cpu = 27, RamTotal = 100, RamUsed = 30, RamPercent = 30 });
            if (!status.Text.Contains("SYSTEM RUNNING") || status.Foreground != Green)
                throw new InvalidOperationException("The status did not recover after a warning.");
            Update(new Snapshot { CoreAvailable = true, Error = "A drive is inaccessible" });
            if (!status.Text.Contains("SOME READINGS UNAVAILABLE") || status.Foreground != Amber ||
                !Convert.ToString(status.ToolTip).Contains("A drive is inaccessible"))
                throw new InvalidOperationException("Partial reading errors are not shown in the shared status.");
            Update(new Snapshot { CoreAvailable = true, Cpu = 27, RamTotal = 100, RamUsed = 30, RamPercent = 30 });
            SelectTab(0);
            if (cpu.Chart.SampleCount <= samples)
                throw new InvalidOperationException("Returning to Monitor lost chart history.");
            UpdateLayout();
            double fullWidth = ActualWidth, fullHeight = ActualHeight;
            if (!details.IsVisible || shell.ContextMenu.Items.OfType<MenuItem>().Any(item => Convert.ToString(item.Tag) == "compact"))
                throw new InvalidOperationException("The obsolete compact mode is still available.");
            foreach (int tab in new[] { 1, 2, 0 })
            {
                SelectTab(tab); UpdateLayout();
                if (selectedTab != tab || Math.Abs(ActualWidth - fullWidth) > 1 || Math.Abs(ActualHeight - fullHeight) > 1 || !status.Text.Contains("SYSTEM RUNNING"))
                    throw new InvalidOperationException("Tab switching changed the panel size or selection.");
            }
            var area = new Rect(0, 0, 1536, 816);
            var corner = PositionInArea(area, new Size(300, 408), true, new Point(250, 16));
            if (corner.X != 1236 || corner.Y != 0) throw new InvalidOperationException("Default corner placement is incorrect.");
            var secondary = PositionInArea(new Rect(-1920, -100, 1920, 1080), new Size(350, 400), true, new Point(0, 0));
            if (secondary.X != -350 || secondary.Y != -100) throw new InvalidOperationException("Secondary monitor corner placement is incorrect.");
            var clamped = PositionInArea(area, new Size(350, 400), false, new Point(1450, 700));
            if (clamped.X != 1186 || clamped.Y != 416) throw new InvalidOperationException("The panel extends beyond the screen.");
            double testLeft = Left, testTop = Top;
            preferences.DockToCorner = true;
            foreach (int tab in new[] { 1, 2, 0 })
            {
                SelectTab(tab);
                UpdateLayout(); RestorePosition();
                var workArea = CurrentWorkArea();
                if (Math.Abs(Left + ActualWidth - workArea.Right) > 1 || Math.Abs(Top - workArea.Top) > 1)
                    throw new InvalidOperationException("Switching tabs lost the top-right anchor.");
                if (Math.Abs(ActualWidth - fullWidth) > 1 || Math.Abs(ActualHeight - fullHeight) > 1)
                    throw new InvalidOperationException("Switching tabs changed the size.");
            }
            Left = testLeft; Top = testTop;
            if (Topmost || ShowActivated) throw new InvalidOperationException("The widget can take foreground focus.");
            preferences.Background = 0; ApplyPreferences();
            if (((SolidColorBrush)shell.Background).Color.A != 0 || ((SolidColorBrush)cpu.Container.Background).Color.A != 0)
                throw new InvalidOperationException("The background is not fully transparent.");
            Update(new Snapshot { CoreAvailable = true, Cpu = 20, RamTotal = 100, RamUsed = 50, RamPercent = 50, CpuTemperature = null });
            if (temperature.Value.Text != "N/A" || gpu.Value.Text != "N/A") throw new InvalidOperationException("Missing sensors are displayed as real readings.");
            Update(new Snapshot { CoreAvailable = true, CpuTemperature = 95, RamTotal = 100, RamUsed = 50, RamPercent = 50 });
            if (!status.Text.Contains("HIGH TEMPERATURE") || statusBox.Visibility != Visibility.Visible) throw new InvalidOperationException("High-temperature alert is missing.");
            Update(new Snapshot { CoreAvailable = true, Cpu = 20, RamTotal = 100, RamUsed = 50, RamPercent = 50 });
            if (temperature.Value.Text != "N/A" || !status.Text.Contains("SYSTEM RUNNING")) throw new InvalidOperationException("A disconnected sensor still displays an old reading.");
            Update(new Snapshot { CoreAvailable = false });
            if (ram.Value.Text != "N/A" || processTotalRam.Text != "N/A" || !status.Text.Contains("UNAVAILABLE") || statusBox.Visibility != Visibility.Visible) throw new InvalidOperationException("A reading error was not reported.");
            var multi = new Snapshot { CoreAvailable = true, Disks = new List<DiskSnapshot> {
                new DiskSnapshot { Name = "C:\\", Used = 40, Total = 100, IsSystem = true },
                new DiskSnapshot { Name = "E:\\", Used = 98, Total = 100 }
            } };
            Update(multi);
            if (diskRows.Count != 2 || !diskRows.ContainsKey("E:\\") || !status.Text.Contains("E:\\")) throw new InvalidOperationException("The second drive or its warning is missing.");
            ApplyPreferences(); UpdateLayout();
            if (ActualHeight > viewport.MaxHeight + 1 || ActualWidth >= 480 || !details.IsVisible)
                throw new InvalidOperationException("The panel exceeds half the desktop height.");
            double desktopLimit = viewport.MaxHeight;
            viewport.MaxHeight = 300; UpdateLayout();
            var diskBounds = diskList.TransformToAncestor(viewport).TransformBounds(new Rect(diskList.RenderSize));
            if (ActualHeight > 301 || diskBounds.Bottom > viewport.ActualHeight + 1)
                throw new InvalidOperationException("Small screen: drives are clipped or the panel is too tall.");
            viewport.MaxHeight = desktopLimit; UpdateLayout();
            multi.Disks[1].Total = 0; multi.Disks[1].Error = "Drive locked"; Update(multi);
            if (!diskRows["E:\\"].Label.Text.Contains("N/A") || diskRows["E:\\"].Fill.Width != 0) throw new InvalidOperationException("An inaccessible drive still displays old readings.");
            multi.Disks.RemoveAt(1); Update(multi);
            if (diskRows.Count != 1) throw new InvalidOperationException("A removed drive is still visible.");
            foreach (var theme in ThemePalette.All)
            {
                SetTheme(theme.Id);
                if (Green.Color != Brush(theme.Primary).Color || cpu.Chart.Color != Green.Color) throw new InvalidOperationException("Theme was not applied: " + theme.Id);
                foreach (int tab in new[] { 0, 1, 2 })
                {
                    SelectTab(tab); UpdateLayout();
                    if (tabButtons[tab].Foreground != Green || ActualHeight > viewport.MaxHeight + 1)
                        throw new InvalidOperationException("A tab is unreadable or outside the desktop with theme: " + theme.Id);
                }
            }
            var restored = new JavaScriptSerializer().Deserialize<Preferences>(new JavaScriptSerializer().Serialize(preferences));
            if (restored.Theme != preferences.Theme) throw new InvalidOperationException("The theme was not preserved in preferences.");
            SetTheme("unknown-theme"); if (preferences.Theme != "matrix") throw new InvalidOperationException("An unknown theme has no fallback.");
            var options = CreateOptions();
            options.ApplyTemplate(); options.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            var themes = options.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Tag) == "themes");
            var cyberpunk = themes.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Tag) == "cyberpunk");
            cyberpunk.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (preferences.Theme != "cyberpunk") throw new InvalidOperationException("Theme selection from the menu failed.");
            options = CreateOptions();
            var opacityMenu = options.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Tag) == "opacity");
            ((MenuItem)opacityMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (Math.Abs(preferences.Background - 0.60) > 0.001) throw new InvalidOperationException("Opacity selection from the menu failed.");
            options = CreateOptions();
            var desktopItem = options.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Tag) == "desktop");
            if (desktopItem.IsEnabled || !desktopItem.IsChecked || Topmost) throw new InvalidOperationException("Desktop mode was not applied.");
            if (!options.Items.OfType<MenuItem>().Any(item => Convert.ToString(item.Header) == "Internet speed test…"))
                throw new InvalidOperationException("The on-demand Internet speed test is missing from the menu.");
            var legacy = new JavaScriptSerializer().Deserialize<Preferences>("{\"Pinned\":true,\"Theme\":\"ice\"}");
            if (legacy.Theme != "ice") throw new InvalidOperationException("Legacy preferences are incompatible.");
            SetTheme("matrix");
        }
        private void ShowSensorInfo()
        {
            MessageBox.Show(this, "CPU, RAM, disk and network data are read directly from Windows.\n\nFor CPU temperatures and AMD/Intel sensors, run Libre Hardware Monitor with its WMI provider enabled. Some sensors require running it as administrator. NEON runs without elevated privileges.\n\nFor NVIDIA, NEON also tries nvidia-smi if it is included with the driver.\n\nN/A means the reading is unavailable. Temperatures are the highest available readings for each device, not estimates.\n\nAll monitoring data stays on this PC. No account or cloud service is required.", "NEON // Sensors", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task Refresh()
        {
            if (reading || shuttingDown) return;
            if (desktopLayer != null) desktopLayer.KeepBehind();
            reading = true;
            try { var sample = await Task.Run(() => telemetry.Read()); if (!shuttingDown) Update(sample); }
            catch (Exception ex) {
                status.Text = "●  SYSTEM READING UNAVAILABLE"; status.Foreground = Amber;
                status.ToolTip = ex.Message;
            }
            finally { reading = false; }
        }
        public void Update(Snapshot s)
        {
            Latest = s;
            processTotalCpu.Text = Percent(s.Cpu);
            processTotalRam.Text = s.CoreAvailable ? s.RamPercent.ToString("0") + "%" : "N/A";
            cpuModel.Text = s.CpuName; cpuModel.ToolTip = s.CpuName;
            gpuModel.Text = s.GpuName; gpuModel.ToolTip = s.GpuName;
            cpu.Container.ToolTip = s.CpuName + "\nLast 60 samples.";
            gpu.Container.ToolTip = s.GpuName + "\nLast 60 samples.";
            cpu.Value.Text = Percent(s.Cpu); cpu.Detail.Text = Environment.ProcessorCount + " LOGICAL THREADS"; cpu.Chart.Push(s.Cpu);
            ram.Value.Text = s.CoreAvailable ? s.RamPercent.ToString("0") + "%" : "N/A";
            ram.Detail.Text = s.CoreAvailable ? GiB(s.RamUsed) + " / " + GiB(s.RamTotal) + " GiB" : "Reading unavailable"; ram.Chart.Push(s.CoreAvailable ? (double?)s.RamPercent : null);
            gpu.Value.Text = Percent(s.Gpu); gpu.Detail.Text = s.GpuMemoryUsed.HasValue && s.GpuMemoryTotal.HasValue ? "VRAM " + GiB(s.GpuMemoryUsed.Value) + "/" + GiB(s.GpuMemoryTotal.Value) + " GiB" : s.Gpu.HasValue ? "CORE USAGE" : "Sensor unavailable"; gpu.Chart.Push(s.Gpu);
            temperature.Value.Text = Temperature(s.CpuTemperature); temperature.Detail.Text = "GPU " + Temperature(s.GpuTemperature); temperature.Chart.Scale = 110; temperature.Chart.Push(s.CpuTemperature);
            temperature.Container.ToolTip = "CPU: highest CPU sensor reading. GPU: highest GPU sensor reading. Temperature providers are polled no more than once every 5 seconds.\n" + s.SensorSource + "\n" + s.SensorNote + (s.FanRpm.HasValue ? "\nFastest fan: " + s.FanRpm.Value.ToString("0") + " RPM" : "");
            download.Text = Speed(s.Download); upload.Text = Speed(s.Upload);
            networkPeak = Math.Max(1024, Math.Max(Math.Max(s.Download, s.Upload), networkPeak * 0.98));
            downloadChart.Scale = uploadChart.Scale = networkPeak; downloadChart.Push(s.Download); uploadChart.Push(s.Upload);
            string previousDiskLayout = diskLayout;
            UpdateDisks(s.Disks);
            if (previousDiskLayout != diskLayout) UpdateViewportWidth();
            bool hot = s.CpuTemperature >= 90 || s.GpuTemperature >= 85;
            bool pressure = s.RamPercent >= 90 || (s.Cpu.HasValue && s.Cpu >= 90);
            var fullDisk = s.Disks.FirstOrDefault(d => d.Total > 0 && d.Used / d.Total >= 0.95);
            bool full = fullDisk != null;
            bool warning = !s.CoreAvailable || hot || full || pressure || s.Error != null;
            status.Text = !s.CoreAvailable ? "●  SYSTEM READING UNAVAILABLE" : hot ? "●  HIGH TEMPERATURE" : full ? "●  DRIVE ALMOST FULL  " + fullDisk.Name : pressure ? "●  HIGH LOAD" : s.Error != null ? "●  SOME READINGS UNAVAILABLE" : "●  SYSTEM RUNNING";
            status.Foreground = warning ? Amber : Green; status.ToolTip = s.Error ?? status.Text;
            temperature.Value.Foreground = s.CpuTemperature >= 90 ? Amber : White;
            if (tray != null) tray.Text = "NEON | CPU " + Percent(s.Cpu) + " | RAM " + s.RamPercent.ToString("0") + "%";
        }
        private double networkPeak = 1024;
        private void UpdateDisks(List<DiskSnapshot> disks)
        {
            string layout = String.Join("|", disks.Select(d => d.Name));
            if (layout != diskLayout || diskList.Children.Count == 0)
            {
                foreach (var oldRow in diskRows.Values) { fontSizes.Remove(oldRow.Label); fontSizes.Remove(oldRow.Detail); }
                if (emptyDiskNotice != null) { fontSizes.Remove(emptyDiskNotice); emptyDiskNotice = null; }
                diskLayout = layout; diskRows.Clear(); diskList.Children.Clear();
                foreach (var disk in disks)
                {
                    var row = new DiskRow();
                    var block = new StackPanel { Margin = new Thickness(0, 6, 0, 1) };
                    var labels = new Grid { Margin = new Thickness(0, 0, 0, 3) };
                    labels.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Label = Text("", 10, White, true); row.Detail = Text("", 9, Muted, true); row.Detail.VerticalAlignment = VerticalAlignment.Center;
                    labels.Children.Add(row.Label); Place(labels, row.Detail, 0, 1); block.Children.Add(labels);
                    row.Track = new Grid { Height = 3, Background = Line };
                    row.Fill = new Border { Height = 3, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, Background = Green };
                    row.Track.Children.Add(row.Fill); block.Children.Add(row.Track); diskList.Children.Add(block);
                    row.Track.SizeChanged += delegate { SizeDiskBar(row); };
                    diskRows[disk.Name] = row;
                }
                if (disks.Count == 0) { emptyDiskNotice = Text("No fixed drives available", 11, Muted, false); diskList.Children.Add(emptyDiskNotice); }
            }
            foreach (var disk in disks)
            {
                var row = diskRows[disk.Name]; row.Data = disk;
                row.Label.Text = disk.Name + (disk.Total > 0 ? "  " + (100 * disk.Used / disk.Total).ToString("0") + "%" : "  N/A") + (disk.IsSystem ? "  SYS" : "");
                row.Detail.Text = disk.Total > 0 ? GiB(disk.Used) + " / " + GiB(disk.Total) + " GiB" : "Unavailable";
                row.Label.ToolTip = disk.Error ?? (disk.Label + (disk.IsSystem ? " · System drive" : ""));
                SizeDiskBar(row);
            }
        }
        private void SizeDiskBar(DiskRow row)
        {
            var disk = row.Data;
            double fraction = disk != null && disk.Total > 0 ? Math.Max(0, Math.Min(1, disk.Used / disk.Total)) : 0;
            row.Fill.Width = row.Track.ActualWidth * fraction;
            row.Fill.Background = fraction >= 0.95 ? Amber : Green;
        }
        private static string Percent(double? value) { return value.HasValue ? value.Value.ToString("0") + "%" : "N/A"; }
        private static string Temperature(double? value) { return value.HasValue ? value.Value.ToString("0") + "°C" : "N/A"; }
        private static string GiB(double bytes) { return (bytes / 1073741824.0).ToString("0.0"); }
        public static string Speed(double bytes)
        {
            if (bytes >= 1048576) return (bytes / 1048576).ToString("0.0") + " MiB/s";
            if (bytes >= 1024) return (bytes / 1024).ToString("0.0") + " KiB/s";
            return bytes.ToString("0") + " B/s";
        }
        private void SetupTray()
        {
            trayIcon = BrandIcon.Tray();
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "NEON // PC Monitor", Visible = true };
            tray.MouseUp += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Right) Dispatcher.BeginInvoke(new Action(OpenOptions)); };
            tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(Reveal)); };
        }
        public void Reveal()
        {
            ShowActivated = false;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show(); if (desktopLayer != null) desktopLayer.KeepBehind();
        }
        private Preferences ReadPreferences()
        {
            return PreferencesStore.Load(settingsPath);
        }
        private bool SavePreferences()
        {
            if (preview) return true;
            preferences.Left = Left; preferences.Top = Top;
            return PreferencesStore.Save(settingsPath, preferences);
        }
        private void RestorePosition()
        {
            if (positioning || shuttingDown) return;
            positioning = true;
            try {
                var area = SystemParameters.WorkArea;
                UpdateLayout();
                double width = ActualWidth > 0 ? ActualWidth : viewport.DesiredSize.Width;
                double height = ActualHeight > 0 ? ActualHeight : viewport.DesiredSize.Height;
                double savedLeft = preferences.Left, savedTop = preferences.Top;
                bool reachable = false;
                var source = PresentationSource.FromVisual(this);
                double dpi = source == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
                foreach (var screen in Forms.Screen.AllScreens)
                {
                    var bounds = screen.WorkingArea;
                    var screenArea = new Rect(bounds.Left / dpi, bounds.Top / dpi, bounds.Width / dpi, bounds.Height / dpi);
                    if (screenArea.Contains(new Point(savedLeft + 20, savedTop + 10))) { reachable = true; area = screenArea; break; }
                }
                var point = PositionInArea(area, new Size(width, height), preferences.DockToCorner || !reachable, new Point(savedLeft, savedTop));
                Left = point.X; Top = point.Y;
                FitToDesktop(); UpdateLayout();
                point = PositionInArea(area, new Size(ActualWidth, ActualHeight), preferences.DockToCorner || !reachable, new Point(savedLeft, savedTop));
                Left = point.X; Top = point.Y;
                preferences.Left = Left; preferences.Top = Top;
            } finally { positioning = false; }
            PositionCompanionWindows();
        }
        private void PositionCompanionWindows()
        {
            var area = CurrentWorkArea();
            var widgetWidth = ActualWidth > 0 ? ActualWidth : PanelWidth;
            var widgetHeight = ActualHeight > 0 ? ActualHeight : viewport.DesiredSize.Height;
            if (speedTestWindow != null)
            {
                speedTestWindow.MaxWidth = area.Width; speedTestWindow.MaxHeight = area.Height;
                speedTestWindow.Width = Math.Min(widgetWidth, area.Width);
                speedTestWindow.Height = Math.Min(widgetHeight, area.Height);
            }
            foreach (Window companion in new Window[] { settingsWindow, speedTestWindow })
            {
                if (companion == null) continue;
                var point = SettingsPosition(area, new Rect(Left, Top, widgetWidth, widgetHeight),
                    new Size(companion.ActualWidth > 0 ? companion.ActualWidth : companion.Width, companion.ActualHeight > 0 ? companion.ActualHeight : companion.Height));
                companion.Left = point.X; companion.Top = point.Y;
            }
        }
        internal static Point SettingsPosition(Rect area, Rect widget, Size dialog)
        {
            double left = widget.Left - dialog.Width - 8;
            if (left < area.Left && widget.Right + 8 + dialog.Width <= area.Right) left = widget.Right + 8;
            return new Point(Math.Max(area.Left, Math.Min(left, area.Right - dialog.Width)), Math.Max(area.Top, Math.Min(widget.Top, area.Bottom - dialog.Height)));
        }
        internal static Point PositionInArea(Rect area, Size panel, bool docked, Point saved)
        {
            double left = docked ? area.Right - panel.Width : saved.X;
            double top = docked ? area.Top : saved.Y;
            return new Point(Math.Max(area.Left, Math.Min(left, area.Right - panel.Width)), Math.Max(area.Top, Math.Min(top, area.Bottom - panel.Height)));
        }
        private Rect CurrentWorkArea()
        {
            var source = PresentationSource.FromVisual(this);
            if (source == null) return SystemParameters.WorkArea;
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var transform = source.CompositionTarget.TransformFromDevice;
            return new Rect(screen.WorkingArea.Left * transform.M11, screen.WorkingArea.Top * transform.M22, screen.WorkingArea.Width * transform.M11, screen.WorkingArea.Height * transform.M22);
        }
        private void QueuePosition()
        {
            if (preview || !IsLoaded || positionQueued || positioning || dragging || shuttingDown) return;
            positionQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate {
                positionQueued = false;
                if (!shuttingDown && !dragging) RestorePosition();
            }));
        }
        private void UpdateViewportWidth()
        {
            foreach (var item in fontSizes) item.Key.FontSize = item.Value;
            InvalidateMeasurements(shell);
            shell.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double fullHeight = shell.DesiredSize.Height;
            foreach (var item in fontSizes) item.Key.FontSize = item.Value * preferences.FontScale;
            InvalidateMeasurements(shell);
            shell.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            double currentFullHeight = shell.DesiredSize.Height;
            double heightLimit = Math.Min(workAreaHeight * 0.90, workAreaHeight / 2 * preferences.WidgetScale * Math.Max(1, preferences.FontScale));
            double desiredWidth = PanelWidth * Math.Min(1, workAreaHeight / 2 / Math.Max(1, fullHeight)) * preferences.WidgetScale;
            viewport.MaxWidth = Math.Min(Math.Min(workAreaWidth, desiredWidth), PanelWidth * heightLimit / Math.Max(1, currentFullHeight));
            viewport.MaxHeight = heightLimit;
            InvalidateMeasurements(shell);
            viewport.InvalidateMeasure();
        }
        private static void InvalidateMeasurements(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) InvalidateMeasurements(VisualTreeHelper.GetChild(root, i));
            var element = root as FrameworkElement; if (element != null) element.InvalidateMeasure();
        }
        private void FitToDesktop()
        {
            var source = PresentationSource.FromVisual(this);
            if (source == null) return;
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            // Screen bounds are physical pixels; WPF layout uses device-independent units.
            double height = screen.WorkingArea.Height * source.CompositionTarget.TransformFromDevice.M22;
            double width = screen.WorkingArea.Width * source.CompositionTarget.TransformFromDevice.M11;
            if (Math.Abs(workAreaHeight - height) > 0.1 || Math.Abs(workAreaWidth - width) > 0.1) { workAreaHeight = height; workAreaWidth = width; UpdateViewportWidth(); }
        }
    }
}
