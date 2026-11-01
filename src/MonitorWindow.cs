using System;
using System.Collections.Generic;
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
    public sealed class Preferences
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Background { get; set; }
        public bool Compact { get; set; }
        public string Theme { get; set; }
        public Preferences() { Left = -99999; Top = -99999; Background = 0.60; Theme = "matrix"; }
    }

    public sealed class Sparkline : FrameworkElement
    {
        private readonly List<double?> values = new List<double?>();
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

    public sealed class MonitorWindow : Window
    {
        public const double PanelWidth = 350;
        private readonly SolidColorBrush Green = Brush("#70FFA7"), White = Brush("#E3FFED"), Muted = Brush("#8AB49C"), Amber = Brush("#FFC66D"), Cyan = Brush("#73DDEB");
        private readonly SolidColorBrush Line = Brush("#41634E"), Surface = Brush("#050E0A");
        private readonly List<Border> panels = new List<Border>();
        private readonly Telemetry telemetry = new Telemetry();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private readonly Preferences preferences;
        private readonly string settingsPath;
        private readonly bool preview;
        private bool reading, shuttingDown;
        private Forms.NotifyIcon tray;
        private System.Drawing.Icon trayIcon;
        private DesktopLayer desktopLayer;
        private Border shell;
        private Viewbox viewport;
        private StackPanel details;
        private TextBlock cpuModel, gpuModel, status, time, footer, download, upload, uptime, memoryDetail, themeLabel;
        private StackPanel diskList;
        private readonly Dictionary<string, DiskRow> diskRows = new Dictionary<string, DiskRow>();
        private string diskLayout = "";
        private Sparkline downloadChart, uploadChart;
        private Metric cpu, ram, gpu, temperature;
        private Button compactButton;
        public FrameworkElement Visual { get { return viewport; } }
        public Snapshot Latest { get; private set; }

        public MonitorWindow(bool isPreview)
        {
            preview = isPreview;
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
            Resources.Add(typeof(Button), XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
              <Setter Property='Foreground' Value='{DynamicResource MutedColor}'/><Setter Property='Background' Value='Transparent'/>
              <Setter Property='BorderBrush' Value='{DynamicResource LineColor}'/><Setter Property='BorderThickness' Value='1'/>
              <Setter Property='FontFamily' Value='Consolas'/><Setter Property='FontSize' Value='10'/>
              <Setter Property='Cursor' Value='Hand'/><Setter Property='Padding' Value='5,3'/>
              <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
              <Border x:Name='chrome' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='4' Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
              <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{DynamicResource SurfaceColor}'/><Setter Property='Foreground' Value='{DynamicResource Accent}'/></Trigger></ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter></Style>"));
            BuildInterface(); ApplyPreferences();
            SourceInitialized += delegate { FitToDesktop(); desktopLayer = new DesktopLayer(this); };
            IsVisibleChanged += delegate { if (desktopLayer != null) desktopLayer.KeepBehind(); };
            Closed += delegate { if (desktopLayer != null) desktopLayer.Dispose(); };
            if (!preview)
            {
                SetupTray(); RestorePosition();
                Loaded += async delegate { FitToDesktop(); UpdateLayout(); RestorePosition(); await Refresh(); timer.Start(); };
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += async delegate { await Refresh(); };
                Closing += delegate { shuttingDown = true; timer.Stop(); SavePreferences(); if (shell.ContextMenu != null) shell.ContextMenu.IsOpen = false; if (tray != null) tray.Dispose(); if (trayIcon != null) trayIcon.Dispose(); };
                LocationChanged += delegate { if (IsLoaded) { FitToDesktop(); preferences.Left = Left; preferences.Top = Top; } };
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
        private static TextBlock Text(string value, double size, Brush color, bool mono)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = color, FontFamily = new FontFamily(mono ? "Consolas" : "Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis };
        }
        private static void Place(Grid grid, UIElement child, int row, int column) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
        private Button ActionButton(string text, string tip, Action action)
        {
            var button = new Button { Content = text, ToolTip = tip, Margin = new Thickness(3, 0, 0, 0) };
            button.Click += delegate { action(); }; return button;
        }

        private void BuildInterface()
        {
            shell = new Border { Width = PanelWidth, Margin = new Thickness(6), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(12), BorderBrush = Line, BorderThickness = new Thickness(1) };
            viewport = new Viewbox { Child = shell, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxHeight = SystemParameters.WorkArea.Height / 2 };
            Content = viewport;
            var body = new StackPanel(); shell.Child = body;
            var headerPanel = CreatePanel(new Thickness(0)); headerPanel.Margin = new Thickness(0, 0, 0, 8);
            var headerBody = new StackPanel(); headerPanel.Child = headerBody; body.Children.Add(headerPanel);
            var header = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brand = Text("●  NEON / SYS", 10, Green, true); brand.VerticalAlignment = VerticalAlignment.Center;
            brand.ToolTip = "Trascina da qui per spostare il widget. Tasto destro per le opzioni.";
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.OriginalSource is TextBlock || e.OriginalSource == header)
                    try { DragMove(); SavePreferences(); } catch (InvalidOperationException) { }
            };
            header.Children.Add(brand);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            compactButton = ActionButton("=", "Modalità compatta", ToggleCompact);
            actions.Children.Add(compactButton);
            actions.Children.Add(ActionButton("···", "Impostazioni e trasparenza", OpenOptions));
            actions.Children.Add(ActionButton("−", "Nascondi nell'area di notifica", delegate { Hide(); }));
            actions.Children.Add(ActionButton("×", "Chiudi NEON", delegate { Close(); }));
            Place(header, actions, 0, 1); headerBody.Children.Add(header);
            var titleRow = new Grid(); titleRow.ColumnDefinitions.Add(new ColumnDefinition()); titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.Children.Add(Text("PC MONITOR", 20, White, true));
            time = Text("--:--:--", 10, Muted, true); time.VerticalAlignment = VerticalAlignment.Center; Place(titleRow, time, 0, 1); headerBody.Children.Add(titleRow);
            themeLabel = Text("MATRIX  /  LIVE 1s", 9, Green, true); themeLabel.Margin = new Thickness(1, 2, 0, 6); headerBody.Children.Add(themeLabel);
            status = Text("●  COLLEGAMENTO AL SISTEMA…", 10, Green, true);
            var statusBox = new Border { Padding = new Thickness(7, 4, 7, 4), BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = status }; headerBody.Children.Add(statusBox);
            cpuModel = Text("", 10, Muted, false); cpuModel.Visibility = Visibility.Collapsed; headerBody.Children.Add(cpuModel);
            var cards = new Grid();
            cards.ColumnDefinitions.Add(new ColumnDefinition()); cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.RowDefinitions.Add(new RowDefinition()); cards.RowDefinitions.Add(new RowDefinition());
            cpu = CreateMetric("01 / CPU", Green); ram = CreateMetric("02 / RAM", Cyan);
            gpu = CreateMetric("03 / GPU", Green); temperature = CreateMetric("04 / TEMP CPU", Amber);
            cpu.Container.Margin = new Thickness(0, 0, 3, 6); ram.Container.Margin = new Thickness(3, 0, 0, 6);
            gpu.Container.Margin = new Thickness(0, 0, 3, 0); temperature.Container.Margin = new Thickness(3, 0, 0, 0);
            Place(cards, cpu.Container, 0, 0); Place(cards, ram.Container, 0, 1); Place(cards, gpu.Container, 1, 0); Place(cards, temperature.Container, 1, 1); body.Children.Add(cards);
            details = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; body.Children.Add(details);
            var networkPanel = CreatePanel(new Thickness(0)); networkPanel.Margin = new Thickness(0, 0, 0, 8);
            var networkBody = new StackPanel(); networkPanel.Child = networkBody; details.Children.Add(networkPanel);
            gpuModel = Text("", 10, Muted, false); gpuModel.Visibility = Visibility.Collapsed; networkBody.Children.Add(gpuModel);
            networkBody.Children.Add(SectionTitle("RETE", "↓ / ↑"));
            var networkGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) }; networkGrid.ColumnDefinitions.Add(new ColumnDefinition()); networkGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var down = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; var up = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            down.Children.Add(Text("↓ DOWNLOAD", 9, Muted, true)); download = Text("—", 15, Green, true); down.Children.Add(download);
            downloadChart = new Sparkline { Height = 12, Margin = new Thickness(0, 2, 0, 0) }; down.Children.Add(downloadChart);
            up.Children.Add(Text("↑ UPLOAD", 9, Muted, true)); upload = Text("—", 15, Cyan, true); up.Children.Add(upload);
            uploadChart = new Sparkline { Height = 12, Margin = new Thickness(0, 2, 0, 0), Color = ((SolidColorBrush)Cyan).Color }; up.Children.Add(uploadChart);
            networkGrid.Children.Add(down); Place(networkGrid, up, 0, 1); networkBody.Children.Add(networkGrid);
            var storagePanel = CreatePanel(new Thickness(0));
            var storageBody = new StackPanel(); storagePanel.Child = storageBody; details.Children.Add(storagePanel);
            storageBody.Children.Add(SectionTitle("UNITÀ LOCALI", "STORAGE"));
            diskList = new StackPanel();
            storageBody.Children.Add(new ScrollViewer { Content = diskList, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            memoryDetail = Text("COMMIT  —", 9, Muted, true); memoryDetail.Margin = new Thickness(0, 8, 0, 3); storageBody.Children.Add(memoryDetail);
            uptime = Text("UPTIME  —", 9, Muted, true); storageBody.Children.Add(uptime);
            body.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 8, 0, 6) });
            footer = Text("●  INIZIALIZZAZIONE", 8, Muted, true); body.Children.Add(footer);
            shell.ContextMenu = CreateOptions();
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
            metric.Detail = Text("In attesa…", 9, Muted, true); stack.Children.Add(metric.Detail);
            metric.Chart = new Sparkline { Height = 12, Margin = new Thickness(0, 4, 0, 0), Color = ((SolidColorBrush)accent).Color }; stack.Children.Add(metric.Chart);
            metric.Container.ToolTip = "Grafico degli ultimi 60 campioni. Le interruzioni indicano dati non disponibili.";
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
            menu.Items.Add(new MenuItem { Header = "NEON / IMPOSTAZIONI", IsEnabled = false, FontSize = 10, Padding = new Thickness(8, 4, 8, 5) });
            var templates = new MenuItem { Header = "Template colori", Tag = "themes", Icon = MenuGlyph("M1,1H6V6H1Z M10,1H15V6H10Z M1,10H6V15H1Z M10,10H15V15H10Z") };
            foreach (var palette in ThemePalette.All)
                templates.Items.Add(ThemeMenuItem(palette));
            menu.Items.Add(templates);
            var opacityMenu = new MenuItem { Header = "Sfondo · " + (preferences.Background * 100).ToString("0") + "%", Tag = "opacity", Icon = MenuGlyph("M8,1 A7,7 0 1 0 8,15 A7,7 0 1 0 8,1 M8,1V15") };
            foreach (double opacity in new[] { 0.0, 0.35, 0.60, 0.85 })
            {
                double choice = opacity;
                string label = opacity == 0 ? "Trasparente" : (opacity * 100).ToString("0") + "% di opacità";
                var item = Menu(label, delegate { preferences.Background = choice; ApplyPreferences(); SavePreferences(); }, Math.Abs(preferences.Background - choice) < 0.01, true);
                item.Icon = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), BorderBrush = Muted, BorderThickness = new Thickness(1), Background = Green, Opacity = Math.Max(0.15, choice) };
                opacityMenu.Items.Add(item);
            }
            menu.Items.Add(opacityMenu);
            menu.Items.Add(new Separator { Style = (Style)menu.Resources[typeof(Separator)] });
            var desktopItem = Menu("Sempre sul desktop", delegate { }, true); desktopItem.Tag = "desktop"; desktopItem.IsEnabled = false; desktopItem.Icon = MenuGlyph("M1,2H15V12H1Z M8,12V15 M4,15H12"); menu.Items.Add(desktopItem);
            var compact = Menu("Modalità compatta", ToggleCompact, preferences.Compact, true); compact.Tag = "compact"; compact.Icon = MenuGlyph("M1,2H15V7H1Z M1,10H15V15H1Z"); menu.Items.Add(compact);
            menu.Items.Add(new Separator { Style = (Style)menu.Resources[typeof(Separator)] });
            var info = Menu("Informazioni sui sensori", ShowSensorInfo, false); info.Icon = MenuGlyph("M8,1 A7,7 0 1 0 8,15 A7,7 0 1 0 8,1 M8,7V12 M8,4V4.5"); menu.Items.Add(info);
            var visibility = Menu(IsVisible ? "Nascondi widget" : "Mostra widget", delegate { if (IsVisible) Hide(); else Reveal(); }, false); visibility.Icon = MenuGlyph("M1,8 Q8,-2 15,8 Q8,18 1,8 M8,5 A3,3 0 1 0 8,11 A3,3 0 1 0 8,5"); menu.Items.Add(visibility);
            var exit = Menu("Esci da NEON", delegate { Close(); }, false); exit.Icon = MenuGlyph("M8,1V8 M4,3 A6,6 0 1 0 12,3"); menu.Items.Add(exit);
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
        public FrameworkElement MenuPreview()
        {
            var grid = new Grid { Background = Brush("#111A21"), Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var main = CreateOptions(); main.Margin = new Thickness(12); main.VerticalAlignment = VerticalAlignment.Top;
            var themes = CreateMenuChrome(); themes.MinWidth = 220; themes.Margin = new Thickness(0, 12, 12, 12); themes.VerticalAlignment = VerticalAlignment.Top;
            themes.Items.Add(new MenuItem { Header = "TEMPLATE COLORI", IsEnabled = false, FontSize = 10, Padding = new Thickness(8, 4, 8, 5) });
            foreach (var palette in ThemePalette.All) themes.Items.Add(ThemeMenuItem(palette));
            grid.Children.Add(new Image { Source = App.RenderElement(main), Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Top });
            Place(grid, new Image { Source = App.RenderElement(themes), Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Top }, 0, 1); return grid;
        }
        private void ToggleCompact() { preferences.Compact = !preferences.Compact; ApplyPreferences(); SavePreferences(); }
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
            themeLabel.Text = palette.Id.ToUpperInvariant() + "  /  LIVE 1s";
            Topmost = false; details.Visibility = preferences.Compact ? Visibility.Collapsed : Visibility.Visible;
            compactButton.Foreground = preferences.Compact ? Green : Muted;
            shell.ContextMenu = CreateOptions();
            if (desktopLayer != null) desktopLayer.KeepBehind();
        }
        public void SetTheme(string theme) { preferences.Theme = theme; ApplyPreferences(); SavePreferences(); }
        public void CompactPreview()
        {
            if (!preview) throw new InvalidOperationException("Solo per le anteprime.");
            preferences.Compact = true; ApplyPreferences();
        }
        public void VerifyInterface()
        {
            if (!preview) throw new InvalidOperationException("Verifica UI disponibile solo senza salvataggio preferenze.");
            preferences.Compact = false; ApplyPreferences();
            UpdateLayout();
            shell.Measure(new Size(PanelWidth, Double.PositiveInfinity)); double fullHeight = shell.DesiredSize.Height;
            ToggleCompact(); UpdateLayout(); shell.Measure(new Size(PanelWidth, Double.PositiveInfinity));
            if (shell.DesiredSize.Height >= fullHeight) throw new InvalidOperationException("Modalità compatta: altezza non ridotta.");
            if (Topmost || ShowActivated) throw new InvalidOperationException("Il widget può prendere il primo piano.");
            preferences.Background = 0; ApplyPreferences();
            if (((SolidColorBrush)shell.Background).Color.A != 0 || ((SolidColorBrush)cpu.Container.Background).Color.A != 0)
                throw new InvalidOperationException("Trasparenza incompleta.");
            Update(new Snapshot { CoreAvailable = true, Cpu = 20, RamTotal = 100, RamUsed = 50, RamPercent = 50, CpuTemperature = null });
            if (temperature.Value.Text != "N/D" || gpu.Value.Text != "N/D") throw new InvalidOperationException("Sensori mancanti visualizzati come valori reali.");
            Update(new Snapshot { CoreAvailable = true, CpuTemperature = 95, RamTotal = 100, RamUsed = 50, RamPercent = 50 });
            if (!status.Text.Contains("TEMPERATURA ELEVATA")) throw new InvalidOperationException("Avviso termico assente.");
            Update(new Snapshot { CoreAvailable = true, Cpu = 20, RamTotal = 100, RamUsed = 50, RamPercent = 50 });
            if (temperature.Value.Text != "N/D" || status.Text.Contains("TEMPERATURA ELEVATA")) throw new InvalidOperationException("Sensore perso: dato precedente rimasto visibile.");
            Update(new Snapshot { CoreAvailable = false });
            if (ram.Value.Text != "N/D" || !status.Text.Contains("NON DISPONIBILE")) throw new InvalidOperationException("Errore di lettura non segnalato.");
            var multi = new Snapshot { CoreAvailable = true, Disks = new List<DiskSnapshot> {
                new DiskSnapshot { Name = "C:\\", Used = 40, Total = 100, IsSystem = true },
                new DiskSnapshot { Name = "E:\\", Used = 98, Total = 100 }
            } };
            Update(multi);
            if (diskRows.Count != 2 || !diskRows.ContainsKey("E:\\") || !status.Text.Contains("E:\\")) throw new InvalidOperationException("Seconda unità o suo avviso assente.");
            preferences.Compact = false; ApplyPreferences(); UpdateLayout();
            if (ActualHeight > viewport.MaxHeight + 1 || ActualWidth >= 480 || !details.IsVisible)
                throw new InvalidOperationException("Il pannello completo non rispetta metà altezza desktop.");
            double desktopLimit = viewport.MaxHeight;
            viewport.MaxHeight = 300; UpdateLayout();
            var diskBounds = diskList.TransformToAncestor(viewport).TransformBounds(new Rect(diskList.RenderSize));
            if (ActualHeight > 301 || diskBounds.Bottom > viewport.ActualHeight + 1)
                throw new InvalidOperationException("Monitor piccolo: unità tagliate o altezza eccessiva.");
            viewport.MaxHeight = desktopLimit; UpdateLayout();
            multi.Disks[1].Total = 0; multi.Disks[1].Error = "Unità bloccata"; Update(multi);
            if (!diskRows["E:\\"].Label.Text.Contains("N/D") || diskRows["E:\\"].Fill.Width != 0) throw new InvalidOperationException("Unità non accessibile mostrata con dati precedenti.");
            multi.Disks.RemoveAt(1); Update(multi);
            if (diskRows.Count != 1) throw new InvalidOperationException("Unità rimossa ancora visibile.");
            foreach (var theme in ThemePalette.All)
            {
                SetTheme(theme.Id);
                if (Green.Color != Brush(theme.Primary).Color || cpu.Chart.Color != Green.Color || !themeLabel.Text.Contains(theme.Id.ToUpperInvariant())) throw new InvalidOperationException("Tema non applicato: " + theme.Id);
            }
            var restored = new JavaScriptSerializer().Deserialize<Preferences>(new JavaScriptSerializer().Serialize(preferences));
            if (restored.Theme != preferences.Theme) throw new InvalidOperationException("Tema non conservato nelle preferenze.");
            SetTheme("tema-inesistente"); if (preferences.Theme != "matrix") throw new InvalidOperationException("Tema sconosciuto senza fallback.");
            var options = CreateOptions();
            options.ApplyTemplate(); options.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
            var themes = options.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Tag) == "themes");
            var cyberpunk = themes.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Tag) == "cyberpunk");
            cyberpunk.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (preferences.Theme != "cyberpunk") throw new InvalidOperationException("Selezione del tema dal menu non funzionante.");
            options = CreateOptions();
            var opacityMenu = options.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Tag) == "opacity");
            ((MenuItem)opacityMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (Math.Abs(preferences.Background - 0.60) > 0.001) throw new InvalidOperationException("Selezione opacità dal menu non funzionante.");
            options = CreateOptions();
            var desktopItem = options.Items.OfType<MenuItem>().First(item => Convert.ToString(item.Tag) == "desktop");
            if (desktopItem.IsEnabled || !desktopItem.IsChecked || Topmost) throw new InvalidOperationException("Modalità desktop non applicata.");
            var legacy = new JavaScriptSerializer().Deserialize<Preferences>("{\"Pinned\":true,\"Theme\":\"ice\"}");
            if (legacy.Theme != "ice") throw new InvalidOperationException("Preferenze precedenti non compatibili.");
            SetTheme("matrix");
        }
        private void ShowSensorInfo()
        {
            MessageBox.Show(this, "CPU, RAM, disco e rete sono letti direttamente da Windows.\n\nPer le temperature CPU e i sensori AMD/Intel: avvia Libre Hardware Monitor e abilita il suo provider WMI. Alcuni sensori richiedono di eseguirlo come amministratore. NEON resta un'app senza privilegi elevati.\n\nPer NVIDIA, NEON prova anche nvidia-smi, se presente nel driver.\n\nN/D significa che il dato non è disponibile. Le temperature sono il massimo tra i sensori del dispositivo; non sono stime.\n\nI dati restano sul PC. Nessun account o servizio cloud.", "NEON // Sensori", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task Refresh()
        {
            if (reading || shuttingDown) return;
            if (desktopLayer != null) desktopLayer.KeepBehind();
            reading = true;
            try { var sample = await Task.Run(() => telemetry.Read()); if (!shuttingDown) Update(sample); }
            catch (Exception ex) { footer.Text = "●  LETTURA NON DISPONIBILE"; footer.ToolTip = ex.Message; }
            finally { reading = false; }
        }
        public void Update(Snapshot s)
        {
            Latest = s;
            time.Text = s.Time.ToString("HH:mm:ss");
            cpuModel.Text = s.CpuName; cpuModel.ToolTip = s.CpuName;
            gpuModel.Text = s.GpuName; gpuModel.ToolTip = s.GpuName;
            cpu.Container.ToolTip = s.CpuName + "\nGrafico degli ultimi 60 campioni.";
            gpu.Container.ToolTip = s.GpuName + "\nGrafico degli ultimi 60 campioni.";
            cpu.Value.Text = Percent(s.Cpu); cpu.Detail.Text = Environment.ProcessorCount + " THREAD LOGICI"; cpu.Chart.Push(s.Cpu);
            ram.Value.Text = s.CoreAvailable ? s.RamPercent.ToString("0") + "%" : "N/D";
            ram.Detail.Text = s.CoreAvailable ? GiB(s.RamUsed) + " / " + GiB(s.RamTotal) + " GiB" : "Lettura non disponibile"; ram.Chart.Push(s.CoreAvailable ? (double?)s.RamPercent : null);
            gpu.Value.Text = Percent(s.Gpu); gpu.Detail.Text = s.GpuMemoryUsed.HasValue && s.GpuMemoryTotal.HasValue ? "VRAM " + GiB(s.GpuMemoryUsed.Value) + "/" + GiB(s.GpuMemoryTotal.Value) + " GiB" : s.Gpu.HasValue ? "UTILIZZO CORE" : "Sensore non esposto"; gpu.Chart.Push(s.Gpu);
            temperature.Value.Text = Temperature(s.CpuTemperature); temperature.Detail.Text = "GPU " + Temperature(s.GpuTemperature); temperature.Chart.Scale = 110; temperature.Chart.Push(s.CpuTemperature);
            temperature.Container.ToolTip = "CPU: massimo dei sensori CPU. GPU: massimo dei sensori GPU. Aggiornamento ogni 5 secondi.\n" + s.SensorSource + "\n" + s.SensorNote + (s.FanRpm.HasValue ? "\nVentola più veloce: " + s.FanRpm.Value.ToString("0") + " RPM" : "");
            download.Text = Speed(s.Download); upload.Text = Speed(s.Upload);
            networkPeak = Math.Max(1024, Math.Max(Math.Max(s.Download, s.Upload), networkPeak * 0.98));
            downloadChart.Scale = uploadChart.Scale = networkPeak; downloadChart.Push(s.Download); uploadChart.Push(s.Upload);
            UpdateDisks(s.Disks);
            memoryDetail.Text = s.CoreAvailable ? "COMMIT  " + GiB(s.CommitUsed) + " / " + GiB(s.CommitTotal) + " GiB" : "COMMIT  N/D";
            memoryDetail.ToolTip = "Memoria impegnata dal sistema / limite di commit (RAM + file di paging).";
            uptime.Text = "UPTIME  " + (int)s.Uptime.TotalDays + "g " + s.Uptime.Hours.ToString("00") + "h " + s.Uptime.Minutes.ToString("00") + "m";
            uptime.ToolTip = "Tempo dall'avvio del kernel Windows; Avvio rapido può conservarlo tra spegnimenti.";
            bool hot = s.CpuTemperature >= 90 || s.GpuTemperature >= 85;
            bool pressure = s.RamPercent >= 90 || (s.Cpu.HasValue && s.Cpu >= 90);
            var fullDisk = s.Disks.FirstOrDefault(d => d.Total > 0 && d.Used / d.Total >= 0.95);
            bool full = fullDisk != null;
            status.Text = !s.CoreAvailable ? "●  LETTURA SISTEMA NON DISPONIBILE" : hot ? "●  TEMPERATURA ELEVATA" : full ? "●  DISCO QUASI PIENO  " + fullDisk.Name : pressure ? "●  CARICO ELEVATO" : "●  SISTEMA OPERATIVO";
            status.Foreground = hot || pressure || full || !s.CoreAvailable ? Amber : Green; status.ToolTip = status.Text;
            temperature.Value.Foreground = s.CpuTemperature >= 90 ? Amber : White;
            footer.Text = s.Error != null ? "●  ALCUNE LETTURE NON DISPONIBILI" : s.CpuTemperature.HasValue ? "●  SENSORI CONNESSI   /   STORICO 60 CAMPIONI" : "○  TEMP CPU N/D   /   INFO SENSORI NEL MENU";
            footer.ToolTip = s.Error ?? (s.SensorSource + "\n" + s.SensorNote);
            if (tray != null) tray.Text = "NEON | CPU " + Percent(s.Cpu) + " | RAM " + s.RamPercent.ToString("0") + "%";
        }
        private double networkPeak = 1024;
        private void UpdateDisks(List<DiskSnapshot> disks)
        {
            string layout = String.Join("|", disks.Select(d => d.Name));
            if (layout != diskLayout || diskList.Children.Count == 0)
            {
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
                if (disks.Count == 0) diskList.Children.Add(Text("Nessuna unità fissa disponibile", 11, Muted, false));
            }
            foreach (var disk in disks)
            {
                var row = diskRows[disk.Name]; row.Data = disk;
                row.Label.Text = disk.Name + (disk.Total > 0 ? "  " + (100 * disk.Used / disk.Total).ToString("0") + "%" : "  N/D") + (disk.IsSystem ? "  SYS" : "");
                row.Detail.Text = disk.Total > 0 ? GiB(disk.Used) + " / " + GiB(disk.Total) + " GiB" : "Non disponibile";
                row.Label.ToolTip = disk.Error ?? (disk.Label + (disk.IsSystem ? " · Unità di sistema" : ""));
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
        private static string Percent(double? value) { return value.HasValue ? value.Value.ToString("0") + "%" : "N/D"; }
        private static string Temperature(double? value) { return value.HasValue ? value.Value.ToString("0") + "°C" : "N/D"; }
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
            try
            {
                var p = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(settingsPath));
                if (p == null || Double.IsNaN(p.Background) || Double.IsInfinity(p.Background) || Double.IsNaN(p.Left) || Double.IsInfinity(p.Left) || Double.IsNaN(p.Top) || Double.IsInfinity(p.Top)) return new Preferences();
                p.Background = Math.Max(0, Math.Min(0.95, p.Background)); return p;
            }
            catch { return new Preferences(); }
        }
        private void SavePreferences()
        {
            if (preview) return;
            try
            {
                preferences.Left = Left; preferences.Top = Top;
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllText(settingsPath, new JavaScriptSerializer().Serialize(preferences));
            }
            catch { /* A read-only portable folder must not stop monitoring. */ }
        }
        private void RestorePosition()
        {
            var area = SystemParameters.WorkArea;
            viewport.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
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
                if (screenArea.Contains(new Point(savedLeft + width / 2, savedTop + 20))) { reachable = true; area = screenArea; break; }
            }
            double left = reachable ? savedLeft : area.Right - width - 16;
            double top = reachable ? savedTop : area.Top + 16;
            Left = Math.Max(area.Left, Math.Min(left, area.Right - width));
            Top = Math.Max(area.Top, Math.Min(top, area.Bottom - height));
        }
        private void FitToDesktop()
        {
            var source = PresentationSource.FromVisual(this);
            if (source == null) { viewport.MaxHeight = SystemParameters.WorkArea.Height / 2; return; }
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            // Screen bounds are physical pixels; WPF layout uses device-independent units.
            viewport.MaxHeight = screen.WorkingArea.Height * source.CompositionTarget.TransformFromDevice.M22 / 2;
        }
    }
}
