using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace Neon
{
    internal sealed class StorageDetailsWindow : Window
    {
        private readonly Border frame, rowsArea;
        private readonly Viewbox viewport;
        private readonly TextBlock subtitle, status, warning, pathLabel, selection, pageLabel;
        private readonly Button upButton, previous, next;
        private readonly StackPanel rows;
        private readonly List<TextBlock> labels = new List<TextBlock>();
        private DesktopLayer desktopLayer;
        private StorageScanResult result;
        private StorageFolder current;
        private string volumeId;
        private bool scanActive, rendering;
        private int page;
        private double fontScale = 1;
        internal FrameworkElement Visual { get { return viewport; } }
        internal string CurrentPath { get { return current == null ? null : current.Path; } }
        internal int PageCount { get; private set; }
        internal int VisibleItemCount { get; private set; }
        internal void SetPreviewSize(double width, double height)
        { viewport.Width = width; viewport.Height = height; frame.Height = 350 * height / width; }
        private static Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        private TextBlock Label(string value, double size, string resource)
        {
            var text = new TextBlock { Text = value, FontSize = size * fontScale, Tag = size,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            text.SetResourceReference(TextBlock.ForegroundProperty, resource); labels.Add(text); return text;
        }
        private static Button Button(string value, Action action)
        { var button = new Button { Content = value }; button.Click += delegate { action(); }; return button; }

        internal StorageDetailsWindow(Preferences preferences)
        {
            fontScale = preferences.FontScale;
            Title = "NEON // Storage details"; Icon = BrandIcon.Window();
            Width = 320; Height = 470; MinWidth = 100; MinHeight = 100;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = false; ShowActivated = false; Topmost = false; UseLayoutRounding = true;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; FontFamily = new FontFamily("Segoe UI"); FontSize = 11 * fontScale;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SourceInitialized += delegate { desktopLayer = new DesktopLayer(this); };
            IsVisibleChanged += delegate { KeepOnDesktop(); };
            Closed += delegate { if (desktopLayer != null) desktopLayer.Dispose(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Neon.SettingsStyles"))
                Resources = (ResourceDictionary)XamlReader.Load(stream);
            Resources.MergedDictionaries.Add(ControlStyles.Load()); Resources[typeof(Button)] = Resources["NeonButton"];
            frame = new Border { Width = 350, Height = 470, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10) };
            frame.SetResourceReference(Border.BackgroundProperty, "SettingsSurface");
            frame.SetResourceReference(Border.BorderBrushProperty, "SettingsLine");
            viewport = new Viewbox { Child = frame, Stretch = Stretch.Fill }; Content = viewport;
            viewport.SizeChanged += delegate {
                if (viewport.ActualWidth > 0 && viewport.ActualHeight > 0)
                    frame.Height = 350 * viewport.ActualHeight / viewport.ActualWidth;
            };
            var layout = new DockPanel(); frame.Child = layout;
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var close = Button("×", Close); close.Style = (Style)Resources["NeonIconButton"];
            DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            var heading = new StackPanel(); heading.Children.Add(Label("NEON / STORAGE", 9, "SettingsAccent"));
            heading.Children.Add(Label("Storage details", 20, "SettingsText")); header.Children.Add(heading);
            heading.MouseLeftButtonDown += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
            DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
            subtitle = Label("", 10, "SettingsMuted"); subtitle.Margin = new Thickness(0, 0, 0, 4);
            DockPanel.SetDock(subtitle, Dock.Top); layout.Children.Add(subtitle);
            status = Label("No scan yet.", 10, "SettingsMuted");
            DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
            warning = Label("", 10, "SettingsWarning"); warning.Margin = new Thickness(0, 2, 0, 5);
            DockPanel.SetDock(warning, Dock.Top); layout.Children.Add(warning);
            var navigation = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            navigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navigation.ColumnDefinitions.Add(new ColumnDefinition());
            upButton = Button("← Su", NavigateUp); upButton.Margin = new Thickness(0, 0, 6, 0);
            navigation.Children.Add(upButton);
            pathLabel = Label("", 10, "SettingsText"); pathLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pathLabel, 1); navigation.Children.Add(pathLabel);
            DockPanel.SetDock(navigation, Dock.Top); layout.Children.Add(navigation);
            selection = Label("Logical sizes are approximate.", 10, "SettingsMuted");
            selection.TextWrapping = TextWrapping.Wrap; selection.Height = 44 * fontScale;
            selection.Margin = new Thickness(0, 5, 0, 0); selection.ClipToBounds = true;
            DockPanel.SetDock(selection, Dock.Bottom); layout.Children.Add(selection);
            var pager = new Grid { Height = 28 };
            pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pager.ColumnDefinitions.Add(new ColumnDefinition());
            pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            previous = Button("‹ Precedente", PreviousPage); next = Button("Successiva ›", NextPage);
            pageLabel = Label("1 / 1", 10, "SettingsMuted"); pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
            pageLabel.VerticalAlignment = VerticalAlignment.Center;
            pager.Children.Add(previous); Grid.SetColumn(pageLabel, 1); pager.Children.Add(pageLabel);
            Grid.SetColumn(next, 2); pager.Children.Add(next);
            DockPanel.SetDock(pager, Dock.Bottom); layout.Children.Add(pager);
            rows = new StackPanel();
            rowsArea = new Border { Child = rows, ClipToBounds = true };
            rowsArea.SizeChanged += delegate { RenderRows(); };
            layout.Children.Add(rowsArea);
            ApplyAppearance(preferences); RenderRows();
        }
        internal void ApplyAppearance(Preferences preferences)
        {
            fontScale = preferences.FontScale; FontSize = 11 * fontScale;
            foreach (var label in labels) label.FontSize = (double)label.Tag * fontScale;
            selection.Height = 44 * fontScale;
            RenderRows();
            var palette = ThemePalette.Find(preferences.Theme);
            Resources["SettingsText"] = Brush(palette.Text); Resources["SettingsMuted"] = Brush(palette.Muted);
            Resources["SettingsAccent"] = Brush(palette.Primary); Resources["SettingsLine"] = Brush(palette.Line);
            Resources["SettingsWarning"] = Brush(palette.Warning);
            Resources["ControlText"] = Resources["SettingsText"]; Resources["ControlLine"] = Resources["SettingsLine"];
            Resources["ControlAccent"] = Resources["SettingsAccent"];
            var surface = ((SolidColorBrush)Brush(palette.Surface)).Color;
            surface.A = (byte)(255 * preferences.Background);
            Resources["SettingsSurface"] = new SolidColorBrush(surface);
            var accent = (SolidColorBrush)Brush(palette.Primary);
            Resources["ControlSurface"] = new SolidColorBrush(accent.Color) { Opacity = preferences.Background * 0.14 };
            Resources["SettingsButton"] = Resources["ControlSurface"];
            Resources["SettingsSelected"] = new SolidColorBrush(accent.Color) { Opacity = preferences.Background * 0.24 };
        }

        internal void KeepOnDesktop() { if (desktopLayer != null) desktopLayer.KeepBehind(); }
        internal void SetState(StorageVolume volume, StorageScanResult scan, StorageProgress progress, bool scanning)
        {
            bool changed = result != scan || volumeId != (volume == null ? null : volume.Id) || scanActive != scanning;
            result = scan; volumeId = volume == null ? null : volume.Id; scanActive = scanning;
            if (changed) { current = scan == null ? null : scan.RootFolder; page = 0; DescribeFolder(); }
            subtitle.Text = volume == null ? "Select a volume in Storage." : volume.Display;
            status.Text = scanning ? "Scanning… " + (progress == null ? "" : progress.Files.ToString("N0") + " files") :
                scan == null ? (volume != null && volume.Error != null ? volume.Error : "No scan yet.") :
                (scan.State == StorageScanState.Partial ? "Completed" : StorageFormat.State(scan.State)) +
                    " · " + scan.Files.ToString("N0") + " files";
            status.ToolTip = scan == null ? null : scan.Folders.ToString("N0") + " folders · " + scan.Files.ToString("N0") + " files\n" + scan.Error;
            warning.Text = StoragePresentation.Warning(scan);
            warning.ToolTip = scan == null ? null : StorageFormat.ExclusionSummary(scan.Inaccessible, scan.Reparse);
            if (changed) RenderRows();
        }
        internal void NavigateTo(StorageFolder folder)
        {
            if (folder == null || result == null) return;
            current = folder; page = 0; DescribeFolder(); RenderRows();
        }
        internal void NavigateUp() { if (current != null && current.Parent != null) NavigateTo(current.Parent); }
        internal void NextPage() { page++; RenderRows(); }
        internal void PreviousPage() { page--; RenderRows(); }
        private void DescribeFolder()
        {
            string description = current == null ? "Logical sizes are approximate." :
                current.Path + "\n" + StorageFormat.Size(current.Bytes) + " logical · approximate" +
                (current.Incomplete ? " · incomplete" : current.Inaccessible > 0 ? " · partial" : "") +
                (String.IsNullOrEmpty(current.AppName) ? "" : "\nRegistered app: " + current.AppName);
            selection.Text = description; selection.ToolTip = description + (current == null ? "" :
                "\n" + StorageFormat.ExclusionSummary(current.Inaccessible, current.Reparse));
        }
        private void RenderRows()
        {
            if (rows == null || rendering) return;
            rendering = true;
            try
            {
                // Row labels are transient; avoid retaining them after changing page.
                foreach (var button in rows.Children.OfType<Button>())
                    if (button.Content is Grid)
                        foreach (var text in ((Grid)button.Content).Children.OfType<TextBlock>()) labels.Remove(text);
                rows.Children.Clear();
                upButton.IsEnabled = current != null && current.Parent != null;
                pathLabel.Text = current == null ? "" : current.Path; pathLabel.ToolTip = pathLabel.Text;
                var items = StoragePresentation.Items(current);
                double rowHeight = 30 * fontScale;
                int size = StoragePresentation.PageSize(rowsArea.ActualHeight, rowHeight);
                PageCount = StoragePresentation.Pages(items.Count, size);
                page = Math.Max(0, Math.Min(page, PageCount - 1));
                previous.IsEnabled = page > 0; next.IsEnabled = page + 1 < PageCount;
                pageLabel.Text = (page + 1) + " / " + PageCount;
                pageLabel.ToolTip = current == null ? null :
                    Math.Max(0, current.DirectFolderCount - current.Folders.Count) + " folders and " +
                    Math.Max(0, current.DirectFileCount - current.Files.Count) + " files not retained";
                VisibleItemCount = 0;
                if (items.Count == 0)
                {
                    rows.Children.Add(new TextBlock { Text = current == null ?
                        (scanActive ? "Scanning… Results appear after it stops." : "Start a scan from Storage.") :
                        "Nessun contenuto misurato maggiore di 0 B", FontSize = 10 * fontScale,
                        TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Resources["SettingsMuted"] });
                    return;
                }
                foreach (var item in items.Skip(page * size).Take(size))
                {
                    var selected = item; var folder = item as StorageFolder;
                    var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                    row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var icon = Label(folder == null ? "▤" : "▸", 12, "SettingsAccent"); row.Children.Add(icon);
                    var name = Label(Path.GetFileName(item.Path.TrimEnd((char)92)) + (folder != null &&
                        (folder.Incomplete || folder.Inaccessible > 0) ? " · partial" : ""), 11, "SettingsText");
                    Grid.SetColumn(name, 1); row.Children.Add(name);
                    var sizeLabel = Label(StorageFormat.Size(item.Bytes), 10, "SettingsAccent"); sizeLabel.Margin = new Thickness(6, 0, 0, 0);
                    Grid.SetColumn(sizeLabel, 2); row.Children.Add(sizeLabel);
                    var button = Button("", delegate {
                        var target = selected as StorageFolder;
                        if (target != null) NavigateTo(target);
                        else { selection.Text = selected.Path + "\n" + StorageFormat.Size(selected.Bytes) + " logical · approximate"; selection.ToolTip = selection.Text; }
                    });
                    button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    button.MinHeight = 0; button.Padding = new Thickness(5, 2, 5, 2);
                    button.Height = rowHeight - 3; button.Margin = new Thickness(0, 0, 0, 3); button.ToolTip = item.Path;
                    rows.Children.Add(button); VisibleItemCount++;
                }
            }
            finally { rendering = false; }
        }
    }

    internal static class StorageFormat
    {
        internal static string ExclusionSummary(long inaccessible, long reparse)
        {
            if (inaccessible + reparse == 0) return "";
            return inaccessible + " inaccessible · " + reparse + " links skipped. Measured sizes exclude these paths.";
        }
        internal static string State(StorageScanState state)
        {
            switch (state)
            {
                case StorageScanState.Complete: return "Completed";
                case StorageScanState.Partial: return "Completed with exclusions";
                case StorageScanState.Canceled: return "Canceled";
                case StorageScanState.Unavailable: return "Volume unavailable";
                default: return "Scan failed";
            }
        }
        internal static string Size(long bytes)
        {
            if (bytes >= 1073741824) return (bytes / 1073741824.0).ToString("0.0") + " GiB";
            if (bytes >= 1048576) return (bytes / 1048576.0).ToString("0.0") + " MiB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0.0") + " KiB";
            return bytes + " B";
        }
    }
}
