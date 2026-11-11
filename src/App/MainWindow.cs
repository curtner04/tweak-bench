using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TweakBench.Core;

namespace TweakBench.App
{
    /// <summary>
    /// The application window: sidebar (search, navigation, administrator status, Restart, menu), page header, scrolling page,
    /// the pending-changes bar and the status line. It owns the shared <see cref="TweakModel"/> and the pages.
    /// </summary>
    public sealed class AppWindow
    {
        const double ContentMax = 960, Gutter = 28, ScrollBarWidth = 12;

        public readonly Window Window;
        public readonly UiContext Ctx;
        public readonly TweakModel Model;
        public readonly List<PageBase> Pages = new List<PageBase>();
        readonly SearchPage searchPage;
        readonly Dictionary<string, RadioButton> navButtons = new Dictionary<string, RadioButton>();
        readonly Dictionary<string, Border> navCounts = new Dictionary<string, Border>();
        readonly bool demo;

        TextBox searchBox;
        Button searchClear;
        TextBlock titleText, subtitleText, statusText, pendingText, versionText;
        ContentControl actionsHost, contentHost;
        ScrollViewer scroll;
        Border banner, pendingBar, wrongUserBanner, chip;
        TextBlock chipText;
        TextBlock chipIcon;
        Button restartButton;
        Popup menu;
        PageBase current, lastRegular;
        bool suppressNav, suppressSearch, allowClose, leaving;
        DateTime lastRefresh = DateTime.Now;

        /// <summary>Screenshot mode: the renderer reads the statuses itself (synchronously).</summary>
        public bool SkipAutoRefresh;

        public AppWindow(ISystem system, AppSettings settings, bool demo)
        {
            this.demo = demo;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                Window = (Window)XamlReader.Load(stream);
            Theme.Apply(Window.Resources, settings.Theme);

            var history = HistoryStore.Load();
            Ctx = new UiContext
            {
                Window = Window, System = system, Engine = new Engine(system, history), History = history, Settings = settings,
                Profiles = new ProfileStore(), IsDemo = demo
            };
            Ctx.StatusLine = SetStatus;
            Ctx.Navigate = Navigate;
            Ctx.RefreshTweaks = RefreshTweaks;
            Ctx.RunAsAdmin = RunAsAdmin;
            Ctx.SaveSettings = SaveSettings;

            // Staged choices from before a Restart or the switch to administrator come back; they are saved again only on the next one.
            Model = new TweakModel(Ctx);
            Model.Load(settings);
            settings.PendingOn = new List<string>();
            settings.PendingOff = new List<string>();
            Model.Changed += OnModelChanged;
            Model.StatusesRefreshed += OnStatusesRefreshed;

            Pages.Add(new StartPage(Ctx, Model, OpenReview));
            foreach (Category c in Catalog.Categories) Pages.Add(new CategoryPage(Ctx, Model, c));
            Pages.Add(new AppsPage(Ctx));
            Pages.Add(new InstallPage(Ctx));
            Pages.Add(new FeaturesPage(Ctx));
            Pages.Add(new RepairPage(Ctx));
            Pages.Add(new HistoryPage(Ctx, Model, OpenReview));
            searchPage = new SearchPage(Ctx, Model);

            Window.Title = demo ? "Tweak Bench (demo)" : "Tweak Bench";
            LoadIcon();
            ApplyGeometry(settings);
            BuildShell();
            Ctx.BusyChanged += UpdateBusy;

            Window.SourceInitialized += (s, e) => Theme.ApplyTitleBar(Window);
            Window.Loaded += (s, e) => OnLoaded();
            Window.Closing += OnClosing;
            Window.Activated += (s, e) => { if (!demo && !Ctx.IsBusy && (DateTime.Now - lastRefresh).TotalSeconds > 90) RefreshTweaks(); };
            Window.PreviewKeyDown += OnPreviewKeyDown;
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
            Window.Closed += (s, e) => SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        }

        // ------------------------------------------------------------------ building the window

        void ApplyGeometry(AppSettings s)
        {
            Rect work = SystemParameters.WorkArea;
            // The window's fixed minimum size (set in MainWindow.xaml) must never be larger than a small or scaled screen.
            Window.MinWidth = Math.Min(Window.MinWidth, work.Width);
            Window.MinHeight = Math.Min(Window.MinHeight, work.Height);
            Window.Width = Math.Min(s.Width, work.Width);
            Window.Height = Math.Min(s.Height, work.Height);
            if (s.Maximized) Window.WindowState = WindowState.Maximized;
        }

        void LoadIcon()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string ico = Path.Combine(baseDir, @"assets\TB.ico");
                if (File.Exists(ico)) Window.Icon = BitmapFrame.Create(new Uri(ico));
            }
            catch (Exception ex) { AppLog.Warn("Could not load the window icon: " + ex.Message); }
        }

        static BitmapImage LoadLogo()
        {
            try
            {
                var image = new BitmapImage();
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("App.Icon.png"))
                {
                    if (stream == null) return null;
                    var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    image.BeginInit();
                    image.StreamSource = ms;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                }
                return image;
            }
            catch (Exception) { return null; }
        }

        FrameworkElement Centered(UIElement child, Thickness margin)
        {
            var host = new Border { MaxWidth = ContentMax + 2 * Gutter, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0), Padding = margin, Child = child };
            var g = new Grid();
            g.Children.Add(host);
            return g;
        }

        void BuildShell()
        {
            var shell = (Grid)Window.FindName("Shell");
            shell.SetResourceReference(Panel.BackgroundProperty, "Bg");
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            FrameworkElement sidebar = BuildSidebar();
            shell.Children.Add(sidebar);

            var right = new Grid();
            Grid.SetColumn(right, 1);
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // admin banner
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // header
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // page
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // pending bar
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // status
            shell.Children.Add(right);

            // Admin banner
            banner = new Border { Padding = new Thickness(Gutter, 10, Gutter, 10), Visibility = Ctx.IsElevated ? Visibility.Collapsed : Visibility.Visible };
            Ui.Ref(banner, Border.BackgroundProperty, "WarnBg");
            var bannerGrid = new Grid();
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bannerIcon = Ui.Glyph(Icons.Shield, 16, "Warn");
            bannerIcon.Margin = new Thickness(0, 0, 12, 0);
            bannerGrid.Children.Add(bannerIcon);
            var bannerText = Ui.Para("Running without administrator rights. You can change your own settings; system-wide ones are read-only.", "Text", 13);
            bannerText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bannerText, 1);
            bannerGrid.Children.Add(bannerText);
            Button bannerButton = Ui.Button("Run as administrator", RunAsAdmin, "Primary");
            bannerButton.Margin = new Thickness(16, 0, 0, 0);
            bannerButton.Padding = new Thickness(14, 5, 14, 5);
            Grid.SetColumn(bannerButton, 2);
            bannerGrid.Children.Add(bannerButton);
            banner.Child = bannerGrid;

            // Warning when Tweak Bench runs as another Windows account than the one signed in (set after startup, see CheckAccount).
            wrongUserBanner = new Border { Padding = new Thickness(Gutter, 10, Gutter, 10), Visibility = Visibility.Collapsed };
            Ui.Ref(wrongUserBanner, Border.BackgroundProperty, "WarnBg");
            var wrongGrid = new Grid();
            wrongGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wrongGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var wrongIcon = Ui.Glyph(Icons.Warning, 16, "Warn");
            wrongIcon.Margin = new Thickness(0, 0, 12, 0);
            wrongIcon.VerticalAlignment = VerticalAlignment.Top;
            wrongGrid.Children.Add(wrongIcon);
            var wrongText = Ui.Para("", "Text", 13);
            wrongText.Name = "WrongUserText";
            wrongText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(wrongText, 1);
            wrongGrid.Children.Add(wrongText);
            wrongUserBanner.Child = wrongGrid;
            wrongUserBanner.Tag = wrongText;
            var banners = new StackPanel();
            banners.Children.Add(banner);
            banners.Children.Add(wrongUserBanner);
            right.Children.Add(banners);

            // Header
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleText = Ui.Heading("", "H1");
            subtitleText = Ui.Para("", "Muted", 13.5);
            subtitleText.Margin = new Thickness(0, 3, 0, 0);
            subtitleText.MaxWidth = 780;
            subtitleText.HorizontalAlignment = HorizontalAlignment.Left;
            titles.Children.Add(titleText);
            titles.Children.Add(subtitleText);
            header.Children.Add(titles);
            actionsHost = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0), IsTabStop = false };
            Grid.SetColumn(actionsHost, 1);
            header.Children.Add(actionsHost);
            FrameworkElement headerHost = Centered(header, new Thickness(Gutter, 22, Gutter, 12));
            headerHost.Margin = new Thickness(0, 0, ScrollBarWidth, 0);
            Grid.SetRow(headerHost, 1);
            right.Children.Add(headerHost);

            // Page
            contentHost = new ContentControl { IsTabStop = false };
            FrameworkElement pageHost = Centered(contentHost, new Thickness(Gutter, 4, Gutter, 28));
            scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = pageHost, Focusable = false };
            Grid.SetRow(scroll, 2);
            right.Children.Add(scroll);

            // Pending bar
            pendingBar = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(18, 10, 12, 10), Visibility = Visibility.Collapsed };
            Ui.Ref(pendingBar, Border.BackgroundProperty, "Card");
            Ui.Ref(pendingBar, Border.BorderBrushProperty, "Accent");
            pendingBar.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.28, Direction = 270 };
            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var barIcon = Ui.Glyph(Icons.Info, 16, "AccentText");
            barIcon.Margin = new Thickness(0, 0, 12, 0);
            barGrid.Children.Add(barIcon);
            var barTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            pendingText = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold };
            barTexts.Children.Add(pendingText);
            var barSub = Ui.Text("Nothing has changed yet. Review the list first.", "Muted", false, 12);
            barTexts.Children.Add(barSub);
            Grid.SetColumn(barTexts, 1);
            barGrid.Children.Add(barTexts);
            var barButtons = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(barButtons, 2);
            Button discard = Ui.Button("Discard", () => Model.Clear(), "Ghost", "Forget these choices. Nothing on your PC changes.");
            Button review = Ui.Button("Review & apply", OpenReview, "Primary");
            review.Margin = new Thickness(8, 0, 0, 0);
            review.Padding = new Thickness(18, 7, 18, 7);
            barButtons.Children.Add(discard);
            barButtons.Children.Add(review);
            barGrid.Children.Add(barButtons);
            pendingBar.Child = barGrid;
            FrameworkElement pendingHost = Centered(pendingBar, new Thickness(Gutter, 4, Gutter, 12));
            pendingHost.Margin = new Thickness(0, 0, ScrollBarWidth, 0);
            Grid.SetRow(pendingHost, 3);
            right.Children.Add(pendingHost);

            // Status line
            var statusBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(Gutter, 6, Gutter, 7) };
            Ui.Ref(statusBorder, Border.BorderBrushProperty, "Border");
            statusText = Ui.Text("Ready.", "Muted", false, 12);
            statusText.ToolTip = null;
            statusBorder.Child = statusText;
            Grid.SetRow(statusBorder, 4);
            right.Children.Add(statusBorder);
            UpdateAccountState();
        }

        FrameworkElement BuildSidebar()
        {
            var bar = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
            Ui.Ref(bar, Border.BackgroundProperty, "Card");
            Ui.Ref(bar, Border.BorderBrushProperty, "Border");
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bar.Child = grid;

            // Brand
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 18, 14, 14) };
            BitmapImage logo = LoadLogo();
            if (logo != null)
            {
                var img = new Image { Source = logo, Width = 34, Height = 34, Margin = new Thickness(0, 0, 11, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                brand.Children.Add(img);
            }
            else
            {
                var tile = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 11, 0) };
                Ui.Ref(tile, Border.BackgroundProperty, "Accent");
                var g = Ui.Glyph(Icons.Settings, 18, "AccentFg");
                g.HorizontalAlignment = HorizontalAlignment.Center;
                tile.Child = g;
                brand.Children.Add(tile);
            }
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = "Tweak Bench", FontSize = 17, FontWeight = FontWeights.SemiBold });
            versionText = Ui.Text("Version " + Release.Version + (demo ? " (demo)" : ""), "Muted", false, 11.5);
            names.Children.Add(versionText);
            brand.Children.Add(names);
            grid.Children.Add(brand);

            // Search
            var searchGrid = new Grid { Margin = new Thickness(14, 0, 14, 10) };
            searchBox = Ui.Style(new TextBox { Tag = "Search all tweaks" }, "Search");
            System.Windows.Automation.AutomationProperties.SetName(searchBox, "Search all tweaks");
            searchBox.TextChanged += (s, e) => OnSearchChanged();
            searchGrid.Children.Add(searchBox);
            searchClear = Ui.Button(Ui.Glyph(Icons.Cancel, 10), () => { searchBox.Text = ""; searchBox.Focus(); }, "Link", "Clear the search");
            searchClear.HorizontalAlignment = HorizontalAlignment.Right;
            searchClear.VerticalAlignment = VerticalAlignment.Center;
            searchClear.Margin = new Thickness(0, 0, 8, 0);
            searchClear.Visibility = Visibility.Collapsed;
            searchGrid.Children.Add(searchClear);
            Grid.SetRow(searchGrid, 1);
            grid.Children.Add(searchGrid);

            // Navigation
            var nav = new StackPanel { Margin = new Thickness(10, 0, 10, 8) };
            AddNav(nav, "home");
            nav.Children.Add(NavCaption("TWEAKS"));
            foreach (Category c in Catalog.Categories) AddNav(nav, c.Id);
            nav.Children.Add(NavCaption("TOOLS"));
            foreach (string id in new[] { "apps", "install", "features", "repair" }) AddNav(nav, id);
            var gap = new Border { Height = 8 };
            nav.Children.Add(gap);
            AddNav(nav, "history");
            var navScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav, Focusable = false };
            Grid.SetRow(navScroll, 2);
            grid.Children.Add(navScroll);

            // Bottom: administrator status, Restart, menu
            var bottom = new StackPanel { Margin = new Thickness(14, 8, 14, 14) };
            chip = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            chipIcon = Ui.Glyph(Icons.Shield, 12);
            chipIcon.Margin = new Thickness(0, 1, 6, 0);
            chipRow.Children.Add(chipIcon);
            chipText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold };
            chipRow.Children.Add(chipText);
            chip.Child = chipRow;
            bottom.Children.Add(chip);
            UpdateAccountState();

            var buttons = new Grid();
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            restartButton = Ui.IconButton(Icons.Refresh, "Restart", Restart, "Ghost", "Save your choices and reopen Tweak Bench. It waits if something is still running, and never runs an installer.");
            buttons.Children.Add(restartButton);
            Button more = Ui.Button(Ui.Glyph(Icons.More, 16), null, "Ghost", "Changelog, Export / import configuration and Settings");
            more.Margin = new Thickness(8, 0, 0, 0);
            more.Padding = new Thickness(11, 7, 11, 7);
            more.Click += (s, e) => ShowMenu(more);
            Grid.SetColumn(more, 1);
            buttons.Children.Add(more);
            bottom.Children.Add(buttons);
            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            menu = new Popup { PlacementTarget = more, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = -6 };
            menu.Child = BuildMenu();
            grid.Children.Add(menu);
            return bar;
        }

        static TextBlock NavCaption(string text)
        {
            var t = Ui.Text(text, "Muted", true, 11);
            t.Margin = new Thickness(12, 16, 0, 5);
            return t;
        }

        void AddNav(Panel panel, string id)
        {
            PageBase page = Pages.First(p => p.Id == id);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Ui.Glyph(page.Glyph, 15);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(icon);
            var label = new TextBlock { Text = page.Title, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            var count = new Border { CornerRadius = new CornerRadius(9), MinWidth = 18, Padding = new Thickness(6, 0, 6, 1), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
            Ui.Ref(count, Border.BackgroundProperty, "Accent");
            var countText = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Ui.Ref(countText, TextBlock.ForegroundProperty, "AccentFg");
            count.Child = countText;
            Grid.SetColumn(count, 2);
            grid.Children.Add(count);

            var rb = new RadioButton { Content = grid, GroupName = "nav", Tag = id };
            Ui.Style(rb, "NavItem");
            rb.Checked += (s, e) => { if (!suppressNav) Navigate(id); };
            panel.Children.Add(rb);
            navButtons[id] = rb;
            navCounts[id] = count;
        }

        FrameworkElement BuildMenu()
        {
            var stack = new StackPanel { MinWidth = 250 };
            Action<string, string, Action> add = (glyph, text, click) =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var g = Ui.Glyph(glyph, 14);
                g.Width = 26;
                row.Children.Add(g);
                row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
                Button b = Ui.Button(row, () => { menu.IsOpen = false; click(); }, "MenuBtn");
                stack.Children.Add(b);
            };
            add(Icons.Refresh, "Re-read all settings", () => { SetStatus("Reading the current state of every tweak…", false); RefreshTweaks(); });
            add("", "Changelog", ShowChangelog);
            add(Icons.Save, "Export / import configuration", Transfer);
            add(Icons.Settings, "Settings", ShowSettings);
            var border = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(4), Margin = new Thickness(10), Child = stack };
            Ui.Ref(border, Border.BackgroundProperty, "Card2");
            Ui.Ref(border, Border.BorderBrushProperty, "Border");
            border.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 };
            return border;
        }

        void ShowMenu(Button anchor)
        {
            menu.PlacementTarget = anchor;
            menu.IsOpen = !menu.IsOpen;
        }

        // ------------------------------------------------------------------ navigation

        /// <summary>Updates the administrator chip and the "wrong account" warning from Engine.WrongUserReason.</summary>
        void UpdateAccountState()
        {
            string wrong = Ctx.Engine.WrongUserReason;
            bool other = !string.IsNullOrEmpty(wrong);
            string key = other || !Ctx.IsElevated ? "Warn" : "Good";
            Ui.Ref(chip, Border.BackgroundProperty, key + "Bg");
            Ui.Ref(chipIcon, TextBlock.ForegroundProperty, key);
            Ui.Ref(chipText, TextBlock.ForegroundProperty, key);
            chipText.Text = other ? "Administrator (other account)" : Ctx.IsElevated ? "Administrator" : "Standard rights";
            chip.ToolTip = other ? wrong : Ctx.IsElevated ? "Tweak Bench can change system-wide settings." : "Tweak Bench can change your own settings only. Use Run as administrator for the rest.";
            if (wrongUserBanner == null) return;      // the sidebar is built before the banner; BuildShell calls this again
            wrongUserBanner.Visibility = other ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)wrongUserBanner.Tag).Text = other ? wrong : "";
        }

        /// <summary>
        /// Looks (once, off the UI thread) whether Tweak Bench runs as another Windows account than the one signed in, for example after an
        /// administrator sign-in was typed at the Windows prompt. Settings for "your account" would then change the other account.
        /// </summary>
        void CheckAccount()
        {
            if (demo) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                string reason = null;
                try
                {
                    SystemInfo info = SystemInfo.Read(Ctx.System);
                    if (info.ElevatedAsOtherUser) reason = WrongAccountText;
                }
                catch (Exception ex) { AppLog.Warn("Could not check the account: " + ex.Message); }
                Ctx.OnUi(() =>
                {
                    Ctx.Engine.WrongUserReason = reason;
                    UpdateAccountState();
                    Model.Notify();
                    foreach (PageBase page in Pages)
                    {
                        try { page.TweaksChanged(); }
                        catch (Exception ex) { AppLog.Error("Page " + page.Id + " failed in TweaksChanged", ex); }
                    }
                });
            });
        }

        /// <summary>The explanation shown on the banner, the Start page and the disabled cards.</summary>
        public const string WrongAccountText = "Tweak Bench is running as a different Windows account than the one you are signed in with. Settings for your own account (File Explorer, Privacy, Start) would change that other account instead, so they are switched off here. Close Tweak Bench and open it again from the account you are signed in with.";

        void OnLoaded()
        {
            if (Ctx.History.LoadWarning != null) SetStatus(Ctx.History.LoadWarning, true);
            CheckAccount();
            string id = Ctx.Settings.LastPage;
            if (Pages.All(p => p.Id != id)) id = "home";
            Navigate(id);
            if (!SkipAutoRefresh) Model.Refresh(false);
            UpdatePendingBar();
            NotificationPanel.EnsureOnStart(Ctx);
        }

        /// <summary>Selects a page by id ("home", a category id, "apps", "install", "features", "repair", "history").</summary>
        public void Navigate(string id)
        {
            PageBase page = Pages.FirstOrDefault(p => p.Id == id) ?? Pages[0];
            ShowPage(page, true);
        }

        void ShowPage(PageBase page, bool updateNav)
        {
            current = page;
            if (page != searchPage)
            {
                lastRegular = page;
                Ctx.Settings.LastPage = page.Id;
                if (searchBox.Text.Length > 0) { suppressSearch = true; searchBox.Text = ""; suppressSearch = false; searchClear.Visibility = Visibility.Collapsed; }
            }
            if (updateNav) CheckNav(page == searchPage ? null : page.Id);
            FrameworkElement root;
            try { root = page.Root; }
            catch (Exception ex)
            {
                AppLog.Error("Page " + page.Id + " could not be built", ex);
                root = Ui.Note("This page could not be shown: " + ex.Message, "Bad");
            }
            ShowHeader(page);
            UpdatePendingBar();
            contentHost.Content = root;
            scroll.ScrollToTop();
            try { page.Shown(); }
            catch (Exception ex) { AppLog.Error("Page " + page.Id + " failed in Shown", ex); SetStatus("This page had a problem: " + ex.Message, true); }
        }

        void ShowHeader(PageBase page)
        {
            titleText.Text = page.Title;
            subtitleText.Text = page.Subtitle ?? "";
            subtitleText.Visibility = string.IsNullOrEmpty(subtitleText.Text) ? Visibility.Collapsed : Visibility.Visible;
            var withActions = page as IHeaderActions;
            actionsHost.Content = withActions == null ? null : withActions.HeaderActions;
        }

        void CheckNav(string id)
        {
            suppressNav = true;
            try
            {
                foreach (var kv in navButtons) kv.Value.IsChecked = id != null && kv.Key == id;
            }
            finally { suppressNav = false; }
        }

        // ------------------------------------------------------------------ search

        void OnSearchChanged()
        {
            searchClear.Visibility = searchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (suppressSearch) return;
            string text = searchBox.Text.Trim();
            if (text.Length == 0)
            {
                if (current == searchPage) ShowPage(lastRegular ?? Pages[0], true);
                return;
            }
            searchPage.Search(text);
            if (current != searchPage) ShowPage(searchPage, true);
            else ShowHeader(searchPage);
        }

        /// <summary>Shows search results for <paramref name="text"/> as if it was typed in the search box.</summary>
        public void SearchFor(string text)
        {
            searchBox.Text = text;
        }

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { searchBox.Focus(); searchBox.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.Escape && searchBox.IsKeyboardFocused && searchBox.Text.Length > 0) { searchBox.Text = ""; e.Handled = true; }
        }

        // ------------------------------------------------------------------ model events

        void OnModelChanged()
        {
            UpdatePendingBar();
        }

        void OnStatusesRefreshed()
        {
            lastRefresh = DateTime.Now;
            foreach (PageBase page in Pages)
            {
                try { page.TweaksChanged(); }
                catch (Exception ex) { AppLog.Error("Page " + page.Id + " failed in TweaksChanged", ex); }
            }
        }

        void UpdatePendingBar()
        {
            int n = Model.Count;
            // The tool pages (apps, install, features, repair) have their own buttons; the tweak bar would only confuse there.
            bool toolPage = current != null && (current.Id == "apps" || current.Id == "install" || current.Id == "features" || current.Id == "repair");
            pendingBar.Visibility = n > 0 && !toolPage ? Visibility.Visible : Visibility.Collapsed;
            pendingText.Text = n == 1 ? "1 change ready" : n + " changes ready";
            foreach (Category c in Catalog.Categories)
            {
                int k = Model.CountIn(c.Id);
                Border badge;
                if (!navCounts.TryGetValue(c.Id, out badge)) continue;
                badge.Visibility = k > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (k > 0) ((TextBlock)badge.Child).Text = k.ToString();
            }
        }

        void UpdateBusy()
        {
            if (Ctx.IsBusy) SetStatus("Working: " + Ctx.BusyText + "…", false);
            else if (statusText.Text.StartsWith("Working:", StringComparison.Ordinal)) SetStatus("Ready.", false);
        }

        // ------------------------------------------------------------------ actions

        void SetStatus(string text, bool error)
        {
            statusText.Text = text ?? "";
            Ui.Ref(statusText, TextBlock.ForegroundProperty, error ? "Bad" : "Muted");
            statusText.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>Re-reads every tweak's state (in the background) and refreshes the pages.</summary>
        public void RefreshTweaks() { Model.Refresh(false); }

        void SaveSettings()
        {
            try { Ctx.Settings.Save(); }
            catch (Exception ex) { SetStatus("Settings could not be saved: " + ex.Message, true); }
        }

        void OpenReview()
        {
            if (Ctx.IsBusy) { Ui.Info(Window, "Tweak Bench is busy (" + Ctx.BusyText + "). Try again when it has finished."); return; }
            List<PlanItem> plan = Model.Plan();
            if (plan.Count == 0) { SetStatus("Nothing is ready yet. Flip a switch on a tweak first.", false); return; }
            new ReviewDialog(Ctx, Model, plan).ShowDialog();
        }

        void ShowChangelog() { new ChangelogWindow(Ctx).ShowDialog(); }

        void ShowSettings()
        {
            new SettingsDialog(Ctx, () =>
            {
                Theme.Apply(Window.Resources, Ctx.Settings.Theme);
                Theme.ApplyTitleBar(Window);
                Model.Notify();
            }).ShowDialog();
        }

        void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || Ctx.Settings.Theme != "auto") return;
            Window.Dispatcher.BeginInvoke(new Action(() => { Theme.Apply(Window.Resources, "auto"); Theme.ApplyTitleBar(Window); }));
        }

        // ------------------------------------------------------------------ saving, restart, elevation, transfer

        /// <summary>Copies everything that must survive a restart into the settings file: pages' own pending choices, the staged tweaks, the window size.</summary>
        void SaveEverything(bool keepStaged)
        {
            foreach (PageBase page in Pages)
            {
                try { page.SavePending(); }
                catch (Exception ex) { AppLog.Error("Page " + page.Id + " failed in SavePending", ex); }
            }
            if (keepStaged) Model.Save(Ctx.Settings);
            else
            {
                // A normal close forgets every unfinished choice: staged tweaks and the ticked apps to remove or install.
                // Only Restart and the switch to administrator keep them.
                Ctx.Settings.PendingOn = new List<string>();
                Ctx.Settings.PendingOff = new List<string>();
                Ctx.Settings.PendingInstall = new List<string>();
                Ctx.Settings.PendingRemove = new List<string>();
            }
            SaveWindowState();
            SaveSettings();
        }

        void SaveWindowState()
        {
            AppSettings s = Ctx.Settings;
            s.Maximized = Window.WindowState == WindowState.Maximized;
            Rect bounds = Window.WindowState == WindowState.Normal ? new Rect(0, 0, Window.ActualWidth, Window.ActualHeight) : Window.RestoreBounds;
            if (bounds.Width > 0 && bounds.Height > 0) { s.Width = bounds.Width; s.Height = bounds.Height; }
        }

        bool RefuseWhileBusy(string what)
        {
            if (!Ctx.IsBusy) return false;
            Ui.Info(Window, "Tweak Bench is busy (" + Ctx.BusyText + "), so " + what + " has to wait. Try again when it has finished.");
            return true;
        }

        void Restart()
        {
            if (leaving || RefuseWhileBusy("restarting")) return;
            try
            {
                SaveEverything(true);
                using (var child = AppShared.AppRestart.Prepare())
                {
                    allowClose = true;
                    leaving = true;
                    Window.Close();
                }
            }
            catch (Exception ex)
            {
                leaving = false;
                allowClose = false;
                AppLog.Error("Restart failed", ex);
                SetStatus("Restart did not work: " + ex.Message, true);
                Ui.Info(Window, "Tweak Bench could not restart.\n\n" + ex.Message);
            }
        }

        void RunAsAdmin()
        {
            if (Ctx.IsElevated || leaving || RefuseWhileBusy("switching to administrator")) return;
            SaveEverything(true);
            string error;
            if (Program.TryStartElevated(out error))
            {
                allowClose = true;
                leaving = true;
                Window.Close();
                return;
            }
            if (error == null) SetStatus("Windows did not grant administrator rights, so Tweak Bench stays as it is.", false);
            else SetStatus("Could not start with administrator rights: " + error, true);
        }

        void Transfer()
        {
            if (leaving || RefuseWhileBusy("Export / import")) return;
            if (!Ui.Confirm(Window, "Export / import configuration opens in its own window, and Tweak Bench closes while it runs.\n\nYour settings, history and profiles are saved first. Continue?")) return;
            try
            {
                string folder = AppDomain.CurrentDomain.BaseDirectory;
                string helper = AppShared.ConfigurationTransfer.Find(folder, true);
                if (helper == null) throw new IOException("Configuration Transfer is missing. Restore the app's helper from its package.");
                SaveEverything(true);
                AppShared.ConfigurationTransfer.Start(helper, folder, Paths.Data, true);
                allowClose = true;
                leaving = true;
                Window.Close();
            }
            catch (Exception ex)
            {
                AppLog.Error("Export / import failed", ex);
                SetStatus("Export / import could not start: " + ex.Message, true);
                Ui.Info(Window, ex.Message);
            }
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (allowClose) return;
            if (Ctx.IsBusy)
            {
                bool close = MessageBox.Show(Window, "Tweak Bench is still working (" + Ctx.BusyText + "). Closing now could leave a change half-finished.\n\nClose anyway?", "Tweak Bench", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
                if (!close) { e.Cancel = true; return; }
            }
            SaveEverything(false);
        }

        /// <summary>Brings the window to the front (a second launch asks for it). Safe from any thread.</summary>
        public void BringForward()
        {
            Window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
                Window.Activate();
            }));
        }

        /// <summary>Closes without asking or saving (screenshot mode).</summary>
        public void ForceClose()
        {
            allowClose = true;
            Window.Close();
        }
    }
}
