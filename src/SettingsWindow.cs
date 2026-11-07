using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace Neon
{
    internal sealed class SettingsWindow : Window
    {
        private readonly Func<Preferences, bool> apply;
        private readonly Preferences draft;
        private Preferences saved;
        private readonly Func<Preferences, bool> save;
        private readonly IStartupRegistration startup;
        private CheckBox launchAtLogin;
        private TextBlock startupStatus;
        private Button windowsStartup;
        private bool startupLoading, startupDirty;
        private StartupSnapshot startupState;
        private bool loading, accepted;
        private readonly Dictionary<string, Button> themes = new Dictionary<string, Button>();
        private Slider widgetSize, fontSize, opacity, refresh;
        private TextBlock status;
        private Border frame;
        private DesktopLayer desktopLayer;
        public FrameworkElement Visual { get { return frame; } }
        private static Brush Color(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        private TextBlock Label(string text, double size, string color)
        { var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }; label.SetResourceReference(TextBlock.ForegroundProperty, color == "#70FFA7" ? "SettingsAccent" : color == "#E6F3EC" ? "SettingsText" : "SettingsMuted"); return label; }

        public SettingsWindow(Preferences current, Func<Preferences, bool> applySettings, Func<Preferences, bool> saveSettings = null, IStartupRegistration startupRegistration = null)
        {
            startup = startupRegistration ?? new PreviewStartupRegistration();
            draft = current.Copy(); draft.Normalize(); saved = draft.Copy(); apply = applySettings; save = saveSettings ?? applySettings;
            Title = "NEON // Personalize"; Icon = BrandIcon.Window(); Width = 320; Height = 470;
            MinWidth = 300; MinHeight = Math.Min(330, SystemParameters.WorkArea.Height - 24);
            MaxHeight = SystemParameters.WorkArea.Height - 24;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false; ShowActivated = false; Topmost = false; UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SourceInitialized += delegate { desktopLayer = new DesktopLayer(this); };
            IsVisibleChanged += delegate { KeepOnDesktop(); };
            Closed += delegate { if (desktopLayer != null) desktopLayer.Dispose(); };
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResizeWithGrip; FontFamily = new FontFamily("Segoe UI"); FontSize = 11;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Neon.SettingsStyles"))
                Resources = (ResourceDictionary)XamlReader.Load(stream);
            Resources.MergedDictionaries.Add(ControlStyles.Load());
            Resources[typeof(Button)] = Resources["NeonButton"];
            Resources[typeof(DesktopSlider)] = Resources[typeof(Slider)];
            frame = new Border { Background = Color("#0D1712"), BorderBrush = Color("#3B624B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10) };
            Content = frame;
            frame.SetResourceReference(Border.BackgroundProperty, "SettingsSurface"); frame.SetResourceReference(Border.BorderBrushProperty, "SettingsLine");
            Closing += delegate { if (!accepted) apply(saved.Copy()); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
            var layout = new DockPanel(); frame.Child = layout;
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var close = Button("×", delegate { Close(); }); close.ToolTip = "Close personalization";
            close.Style = (Style)Resources["NeonIconButton"]; close.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            var heading = new StackPanel(); heading.Children.Add(Label("NEON / PERSONALIZE", 9, "#70FFA7"));
            var title = Label("Personalize", 20, "#E6F3EC"); heading.Children.Add(title);
            header.Children.Add(heading);
            heading.MouseLeftButtonDown += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); };
            DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
            var footer = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            status = Label("Live preview · Save to keep changes.", 10, "#9FBAAB"); status.MinHeight = 18; footer.Children.Add(status);
            var buttons = new Grid(); buttons.ColumnDefinitions.Add(new ColumnDefinition()); buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var reset = Button("Reset", delegate { LoadValues(new Preferences()); launchAtLogin.IsChecked = false; PreviewDraft(); }); reset.Width = 60; reset.HorizontalAlignment = HorizontalAlignment.Left; buttons.Children.Add(reset);
            var applyButton = Button("Cancel", delegate { Close(); }); applyButton.Width = 60; Grid.SetColumn(applyButton, 1); applyButton.Margin = new Thickness(8, 0, 0, 0); buttons.Children.Add(applyButton);
            var done = Button("Save", delegate { ApplyDraft(true); }); done.Width = 60; done.IsDefault = true; done.SetResourceReference(Control.BackgroundProperty, "SettingsSelected"); Grid.SetColumn(done, 2); done.Margin = new Thickness(8, 0, 0, 0); buttons.Children.Add(done);
            footer.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
            var form = new StackPanel();

            var themeLabel = Label("COLOR THEME", 9, "#E6F3EC"); themeLabel.Margin = new Thickness(0, 0, 0, 3); form.Children.Add(themeLabel);
            var paletteGrid = new Grid(); for (int i = 0; i < 3; i++) paletteGrid.ColumnDefinitions.Add(new ColumnDefinition());
            paletteGrid.RowDefinitions.Add(new RowDefinition()); paletteGrid.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < ThemePalette.All.Length; i++)
            {
                var palette = ThemePalette.All[i]; string id = palette.Id;
                var button = Button("", delegate { draft.Theme = id; PreviewDraft(); }); button.Margin = new Thickness(i % 3 == 0 ? 0 : 5, 0, 0, 3);
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(3), Background = Color(palette.Primary), Margin = new Thickness(0, 0, 4, 0) });
                content.Children.Add(Label(palette.Name.Split('·')[0].Trim(), 11, "#E6F3EC")); button.Content = content;
                Grid.SetColumn(button, i % 3); Grid.SetRow(button, i / 3); paletteGrid.Children.Add(button); themes[id] = button;
            }
            form.Children.Add(paletteGrid);
            widgetSize = Setting(form, "Widget size", "Relative to the current default widget size.", 60, 150, 5, "%");
            fontSize = Setting(form, "Text size", "Adjust the widget's typography independently.", 80, 140, 5, "%");
            opacity = Setting(form, "Background opacity", "0% is transparent. Text and charts remain visible.", 0, 95, 5, "%");
            refresh = Setting(form, "Refresh interval", "Temperature providers refresh at most once every 5 seconds.", 0.5, 10, 0.5, " s");
            launchAtLogin = new CheckBox { Content = "Launch at Windows login", Margin = new Thickness(0, 6, 0, 0) };
            launchAtLogin.SetResourceReference(Control.ForegroundProperty, "SettingsText");
            launchAtLogin.Checked += StartupSelectionChanged; launchAtLogin.Unchecked += StartupSelectionChanged;
            form.Children.Add(launchAtLogin);
            startupStatus = Label("", 10, "#9FBAAB"); startupStatus.Margin = new Thickness(0, 2, 0, 0); form.Children.Add(startupStatus);
            windowsStartup = Button("Open Windows startup settings", delegate
            {
                try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
                catch (Exception ex) { ShowSaveStatus(false, "Windows startup settings could not be opened. " + ex.Message); }
            });
            windowsStartup.HorizontalAlignment = HorizontalAlignment.Left; form.Children.Add(windowsStartup);
            layout.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) });
            LoadValues(draft); UpdateThemeButtons(); RefreshStartupState();
        }
        private void StartupSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (startupLoading) return;
            startupDirty = startupState != null && (launchAtLogin.IsChecked == true) != startupState.Registered;
            ShowSaveStatus(true, "Startup changes apply when you save.");
        }
        internal void RefreshStartupState()
        {
            startupState = startup.Read();
            startupStatus.Text = startupState.Description;
            startupStatus.ToolTip = startupState.ConfiguredCommand;
            startupStatus.SetResourceReference(TextBlock.ForegroundProperty,
                startupState.Error != null || startupState.Registered && (startupState.Approval != StartupApproval.Allowed || !startupState.MatchesExecutable) ? "SettingsWarning" : "SettingsMuted");
            launchAtLogin.IsEnabled = startupState.Error == null;
            if (!startupDirty)
            {
                startupLoading = true; launchAtLogin.IsChecked = startupState.Error == null ? (bool?)startupState.Registered : null; startupLoading = false;
            }
            windowsStartup.Visibility = startupState.Error != null || startupState.Registered && startupState.Approval != StartupApproval.Allowed ? Visibility.Visible : Visibility.Collapsed;
        }
        private static Button Button(string text, Action action)
        { var button = new Button { Content = text }; button.Click += delegate { action(); }; return button; }
        private Slider Setting(Panel parent, string title, string hint, double min, double max, double step, string suffix)
        {
            var section = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            var row = new DockPanel(); var value = Label("", 11, "#70FFA7"); DockPanel.SetDock(value, Dock.Right); row.Children.Add(value); row.Children.Add(Label(title, 11, "#E6F3EC")); section.Children.Add(row);
            var slider = new DesktopSlider { Minimum = min, Maximum = max, TickFrequency = step, SmallChange = step, LargeChange = step * 2, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true };
            slider.ValueChanged += delegate { value.Text = slider.Value.ToString("0.#") + suffix; PreviewDraft(); };
            value.Text = slider.Value.ToString("0.#") + suffix; section.Children.Add(slider);
            slider.ToolTip = hint; parent.Children.Add(section); return slider;
        }
        private void LoadValues(Preferences value)
        {
            loading = true;
            widgetSize.Value = value.WidgetScale * 100; fontSize.Value = value.FontScale * 100;
            opacity.Value = value.Background * 100; refresh.Value = value.RefreshSeconds;
            draft.Theme = value.Theme; draft.Background = value.Background;
            UpdateThemeButtons(); loading = false;
        }
        private void UpdateThemeButtons()
        {
            ApplyTheme();
            foreach (var pair in themes)
            {
                pair.Value.SetResourceReference(Control.BorderBrushProperty, pair.Key == draft.Theme ? "SettingsAccent" : "SettingsLine");
                pair.Value.SetResourceReference(Control.BackgroundProperty, pair.Key == draft.Theme ? "SettingsSelected" : "SettingsButton");
            }
        }
        private void ApplyDraft(bool close)
        {
            draft.WidgetScale = widgetSize.Value / 100; draft.FontScale = fontSize.Value / 100;
            draft.Background = opacity.Value / 100; draft.RefreshSeconds = refresh.Value;
            draft.Normalize(); RefreshStartupState();
            var previousStartup = startupState; bool changed = false, success = false;
            try
            {
                if (previousStartup.Error != null) throw new InvalidOperationException(previousStartup.Error);
                changed = startup.SetEnabled(launchAtLogin.IsChecked == true);
                success = save(draft.Copy());
                if (!success) throw new InvalidOperationException("Applied for this session. Check that the app folder is writable.");
                startupDirty = false;
                ShowSaveStatus(true, "Settings saved.");
            }
            catch (Exception ex)
            {
                success = false; string error = ex.Message;
                if (changed)
                {
                    try { startup.Restore(previousStartup); }
                    catch (Exception restoreError) { error += " The previous startup registration could not be restored. " + restoreError.Message; }
                }
                ShowSaveStatus(false, "Could not save settings. " + error);
            }
            RefreshStartupState();
            if (success) saved = draft.Copy();
            if (close && success) { accepted = true; Close(); }
        }
        private void ShowSaveStatus(bool success, string message)
        {
            status.SetResourceReference(TextBlock.ForegroundProperty, success ? "SettingsAccent" : "SettingsWarning");
            status.Text = message;
        }
        private void PreviewDraft()
        {
            if (loading || refresh == null || status == null) return;
            draft.WidgetScale = widgetSize.Value / 100; draft.FontScale = fontSize.Value / 100;
            draft.Background = opacity.Value / 100; draft.RefreshSeconds = refresh.Value;
            draft.Normalize(); UpdateThemeButtons(); apply(draft.Copy());
            status.SetResourceReference(TextBlock.ForegroundProperty, "SettingsMuted");
            status.Text = "Live preview · Save to keep changes.";
        }
        private void ApplyTheme()
        {
            var palette = ThemePalette.Find(draft.Theme);
            Resources["SettingsText"] = Color(palette.Text); Resources["SettingsMuted"] = Color(palette.Muted);
            Resources["SettingsAccent"] = Color(palette.Primary); Resources["SettingsLine"] = Color(palette.Line);
            Resources["SettingsWarning"] = Color(palette.Warning);
            Resources["ControlText"] = Resources["SettingsText"]; Resources["ControlLine"] = Resources["SettingsLine"];
            Resources["ControlAccent"] = Resources["SettingsAccent"];
            var surface = ((SolidColorBrush)Color(palette.Surface)).Color;
            surface.A = (byte)(255 * draft.Background);
            Resources["SettingsSurface"] = new SolidColorBrush(surface);
            var accent = ((SolidColorBrush)Color(palette.Primary)).Color;
            var button = (SolidColorBrush)Blend(surface, accent, 0.08); button.Opacity = draft.Background * 0.14;
            var selected = (SolidColorBrush)Blend(surface, accent, 0.24); selected.Opacity = draft.Background * 0.24;
            Resources["ControlSurface"] = button; Resources["SettingsButton"] = button; Resources["SettingsSelected"] = selected;
        }
        private static Brush Blend(System.Windows.Media.Color a, System.Windows.Media.Color b, double ratio)
        {
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(a.R + (b.R - a.R) * ratio), (byte)(a.G + (b.G - a.G) * ratio), (byte)(a.B + (b.B - a.B) * ratio)));
        }
        internal void KeepOnDesktop()
        { if (desktopLayer != null) desktopLayer.KeepBehind(); }
        internal void SyncSavedPreferences(Preferences value)
        {
            saved = value.Copy(); LoadValues(value);
        }
        internal void VerifyControls()
        {
            var values = new Preferences { WidgetScale = 0.85, FontScale = 1.2, Background = 0.35, RefreshSeconds = 2, Theme = "ice" };
            LoadValues(values); PreviewDraft(); ApplyDraft(false);
            if (!status.Text.Contains("saved")) throw new InvalidOperationException("Settings controls failed to apply values.");
        }
        internal void VerifySaveFailure()
        {
            ApplyDraft(true);
            if (!status.Text.Contains("this session")) throw new InvalidOperationException("Save failure was not explained.");
        }
        internal void VerifyLivePreview()
        {
            widgetSize.Value = 90; fontSize.Value = 110; opacity.Value = 45; refresh.Value = 3;
            themes["cyberpunk"].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var expected = (System.Windows.Media.Color)ColorConverter.ConvertFromString(ThemePalette.Find("cyberpunk").Surface);
            expected.A = (byte)(255 * draft.Background);
            if (((SolidColorBrush)frame.Background).Color != expected) throw new InvalidOperationException("Settings theme did not follow live preview.");
        }
        internal void VerifySharedOpacity()
        {
            foreach (double percent in new[] { 0.0, 35.0, 60.0, 95.0 })
            {
                opacity.Value = percent;
                if (((SolidColorBrush)frame.Background).Color.A != (byte)(255 * percent / 100) || frame.Opacity != 1 || ((SolidColorBrush)Resources["SettingsText"]).Color.A != 255)
                    throw new InvalidOperationException("Settings background opacity does not match the widget preference.");
            }
        }
    }
}
