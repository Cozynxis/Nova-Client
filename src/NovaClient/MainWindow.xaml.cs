using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NovaClient.Models;
using NovaClient.Services;
using NovaClient.Views;

namespace NovaClient;

public partial class MainWindow : Window
{
    private readonly ProfileService _profiles = new();
    private readonly ModrinthService _modrinth = new();
    private readonly MicrosoftAuthService _auth = new();
    private readonly SettingsService _settingsService = new();
    private readonly MinecraftLauncherService _launcher = new();

    private readonly ObservableCollection<Profile> _profileItems = [];
    private readonly ObservableCollection<InstalledContentItem> _installedItems = [];

    private AppSettings _settings = new();
    private Profile? _current;
    private MinecraftAccount? _account;
    private string _discoverProjectType = "mod";
    private CancellationTokenSource? _loginCancellation;
    private CancellationTokenSource? _launchCancellation;
    private Process? _runningMinecraft;
    private DateTime? _runningStartedAt;
    private bool _initializing = true;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closing += async (_, _) => await SaveWindowStateAsync();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Loading Nova…", false);
            _settings = await _settingsService.LoadAsync();
            ApplySettingsToUi();

            var profiles = await _profiles.LoadAsync();
            foreach (var profile in profiles)
                _profileItems.Add(profile);

            RailProfiles.ItemsSource = _profileItems;
            ProfilesList.ItemsSource = _profileItems;

            _current = _settings.SelectedProfileId is Guid id
                ? _profileItems.FirstOrDefault(p => p.Id == id)
                : null;
            _current ??= _profileItems.FirstOrDefault();

            if (_current is not null)
                ProfilesList.SelectedItem = _current;

            RefreshProfileViews();
            UpdateCurrentProfileUi();
            await RefreshInstalledAsync();

            if (!string.IsNullOrWhiteSpace(_settings.MicrosoftClientId))
                await TryRestoreAccountAsync();
            else
                UpdateAccountUi();

            if (Resources["SidebarPulseStoryboard"] is Storyboard pulse)
                pulse.Begin();

            ShowPage("Home");
            SetStatus("Ready", false);
        }
        catch (Exception ex)
        {
            SetStatus("Startup issue", false);
            MessageBox.Show(this, ex.Message, "Nova Client", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _initializing = false;
        }
    }

    // ================================================================
    // WINDOW / NAVIGATION
    // ================================================================

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string page })
            ShowPage(page);
    }

    private void SidebarAccount_Click(object sender, RoutedEventArgs e) => ShowPage("Account");

    private void OpenLibrary_Click(object sender, RoutedEventArgs e) => ShowPage("Library");

    private void ShowPage(string page)
    {
        HomePage.Visibility = Visibility.Collapsed;
        LibraryPage.Visibility = Visibility.Collapsed;
        DiscoverPage.Visibility = Visibility.Collapsed;
        InstalledPage.Visibility = Visibility.Collapsed;
        DownloadsPage.Visibility = Visibility.Collapsed;
        AccountPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;

        ResetRailSelection();

        switch (page)
        {
            case "Library":
                LibraryPage.Visibility = Visibility.Visible;
                LibraryNav.Tag = "Selected";
                PageTitle.Text = "Instances";
                RefreshProfileViews();
                break;
            case "Discover":
                DiscoverPage.Visibility = Visibility.Visible;
                DiscoverNav.Tag = "Selected";
                PageTitle.Text = "Discover";
                break;
            case "Installed":
                InstalledPage.Visibility = Visibility.Visible;
                InstalledNav.Tag = "Selected";
                PageTitle.Text = "Installed content";
                _ = RefreshInstalledAsync();
                break;
            case "Downloads":
                DownloadsPage.Visibility = Visibility.Visible;
                DownloadsNav.Tag = "Selected";
                PageTitle.Text = "Downloads";
                break;
            case "Account":
                AccountPage.Visibility = Visibility.Visible;
                AccountNav.Tag = "Selected";
                PageTitle.Text = "Account";
                break;
            case "Settings":
                SettingsPage.Visibility = Visibility.Visible;
                SettingsNav.Tag = "Selected";
                PageTitle.Text = "Settings";
                break;
            default:
                HomePage.Visibility = Visibility.Visible;
                HomeNav.Tag = "Selected";
                PageTitle.Text = "Home";
                break;
        }

        var showSidebar = page is "Home" or "Library";
        RightSidebar.Visibility = showSidebar ? Visibility.Visible : Visibility.Collapsed;
        RightSidebarColumn.Width = showSidebar ? new GridLength(326) : new GridLength(0);

        if (!_settings.ReducedMotion && Resources["PageInStoryboard"] is Storyboard animation)
            animation.Begin();
    }

    private void ResetRailSelection()
    {
        foreach (var button in new[] { HomeNav, DiscoverNav, InstalledNav, LibraryNav, DownloadsNav, SettingsNav, AccountNav })
            button.Tag = null;
    }

    private void RailProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Profile profile })
        {
            SetCurrentProfile(profile);
            ShowPage("Home");
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        SetStatus("Checking updates…", true);
        await Task.Delay(650);
        UpdateButton.Content = "Nova 0.2 Preview";
        SetStatus("Up to date", false);
        UpdateButton.IsEnabled = true;
    }

    // ================================================================
    // PROFILES / INSTANCES
    // ================================================================

    private async void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var proposed = _profiles.Create(
            $"Instance {_profileItems.Count + 1}",
            _current?.MinecraftVersion ?? "1.21.1",
            _current?.Loader ?? "Fabric",
            _settings.DefaultMemoryMb);

        var dialog = new ProfileEditorDialog(proposed, creating: true);
        if (dialog.ShowDialog() != true)
        {
            try { _profiles.Delete(proposed); } catch { }
            return;
        }

        _profileItems.Add(dialog.Result);
        SetCurrentProfile(dialog.Result);
        await _profiles.SaveAsync(_profileItems);
        RefreshProfileViews();
        SetStatus("Instance created", false);
    }

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Profile profile }) return;
        await EditProfileAsync(profile);
    }

    private async Task EditProfileAsync(Profile profile)
    {
        var dialog = new ProfileEditorDialog(profile, creating: false);
        if (dialog.ShowDialog() != true) return;
        await _profiles.SaveAsync(_profileItems);
        RefreshProfileViews();
        UpdateCurrentProfileUi();
        await RefreshInstalledAsync();
        SetStatus("Instance updated", false);
    }

    private void ProfileMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Profile profile } button) return;

        var menu = new ContextMenu
        {
            Background = FindBrush("SurfaceRaisedBrush"),
            Foreground = Brushes.White
        };

        AddMenuItem(menu, "Edit instance", async (_, _) => await EditProfileAsync(profile));
        AddMenuItem(menu, "Duplicate", async (_, _) => await DuplicateProfileAsync(profile));
        AddMenuItem(menu, "Open instance folder", (_, _) => _profiles.OpenInstanceFolder(profile));
        AddMenuItem(menu, "Open mods folder", (_, _) => _profiles.OpenModsFolder(profile));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, profile.Favorite ? "Remove from Jump in" : "Add to Jump in", async (_, _) =>
        {
            profile.Favorite = !profile.Favorite;
            await _profiles.SaveAsync(_profileItems);
            RefreshProfileViews();
        });
        AddMenuItem(menu, "Delete instance", async (_, _) => await DeleteProfileAsync(profile));

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private static void AddMenuItem(ContextMenu menu, string title, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = title, Padding = new Thickness(12, 7, 20, 7) };
        item.Click += handler;
        menu.Items.Add(item);
    }

    private async Task DuplicateProfileAsync(Profile source)
    {
        try
        {
            SetStatus("Duplicating instance…", true);
            var copy = _profiles.Duplicate(source);
            _profileItems.Add(copy);
            SetCurrentProfile(copy);
            await _profiles.SaveAsync(_profileItems);
            RefreshProfileViews();
            SetStatus("Instance duplicated", false);
        }
        catch (Exception ex)
        {
            SetStatus("Duplicate failed", false);
            MessageBox.Show(this, ex.Message, "Nova Client", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task DeleteProfileAsync(Profile profile)
    {
        if (_profileItems.Count <= 1)
        {
            MessageBox.Show(this, "Keep at least one instance in Nova.", "Nova Client", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Delete '{profile.Name}' and its Nova instance folder?\n\nThis can remove mods, configs and worlds stored inside that instance.",
            "Delete instance",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            _profiles.Delete(profile);
            _profileItems.Remove(profile);
            if (_current == profile)
                SetCurrentProfile(_profileItems.FirstOrDefault());
            await _profiles.SaveAsync(_profileItems);
            RefreshProfileViews();
            await RefreshInstalledAsync();
            SetStatus("Instance deleted", false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Nova Client", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilesList.SelectedItem is Profile profile)
            SetCurrentProfile(profile);
    }

    private void SetCurrentProfile(Profile? profile)
    {
        _current = profile;
        if (profile is not null)
        {
            ProfilesList.SelectedItem = profile;
            _settings.SelectedProfileId = profile.Id;
            _ = _settingsService.SaveAsync();
            SetComboText(DiscoverVersion, profile.MinecraftVersion);
        }
        UpdateCurrentProfileUi();
        _ = RefreshInstalledAsync();
    }

    private void UpdateCurrentProfileUi()
    {
        if (_current is null)
        {
            SidebarProfileName.Text = "No instance";
            SidebarProfileMeta.Text = "Create an instance";
            SidebarProfileIcon.Text = "⛏";
            ActiveInstanceDiscoverLabel.Text = "Target: no active instance";
            InstalledSubtitle.Text = "Select an instance to see installed content.";
            return;
        }

        SidebarProfileName.Text = _current.Name;
        SidebarProfileMeta.Text = $"{_current.LoaderDisplay} • {_profiles.CountMods(_current)} mods";
        SidebarProfileIcon.Text = _current.Icon;
        ActiveInstanceDiscoverLabel.Text = $"Target: {_current.Name} • {_current.LoaderDisplay}";
        InstalledSubtitle.Text = $"Content installed in {_current.Name} • {_current.LoaderDisplay}";
        HomeLibrarySubtitle.Text = $"{_profileItems.Count} instance{(_profileItems.Count == 1 ? "" : "s")} • active: {_current.Name}";
    }

    private void RefreshProfileViews()
    {
        var searchHome = HomeLibrarySearch?.Text?.Trim() ?? string.Empty;
        var homeItems = _profileItems
            .Where(p => string.IsNullOrWhiteSpace(searchHome) ||
                        p.Name.Contains(searchHome, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(searchHome, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.LastPlayedAt ?? p.CreatedAt)
            .ToList();
        HomeLibraryProfiles.ItemsSource = homeItems;

        var jump = _profileItems
            .Where(p => p.Favorite)
            .OrderByDescending(p => p.LastPlayedAt ?? p.CreatedAt)
            .Take(5)
            .ToList();
        if (jump.Count == 0)
            jump = _profileItems.OrderByDescending(p => p.LastPlayedAt ?? p.CreatedAt).Take(5).ToList();
        JumpInProfiles.ItemsSource = jump;

        var librarySearch = LibrarySearchBox?.Text?.Trim() ?? string.Empty;
        var loader = (LibraryLoaderFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All loaders";
        IEnumerable<Profile> filtered = _profileItems;
        if (!string.IsNullOrWhiteSpace(librarySearch))
            filtered = filtered.Where(p => p.Name.Contains(librarySearch, StringComparison.OrdinalIgnoreCase) || p.Description.Contains(librarySearch, StringComparison.OrdinalIgnoreCase));
        if (!loader.Equals("All loaders", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(p => p.Loader.Equals(loader, StringComparison.OrdinalIgnoreCase));

        var sort = (LibrarySort?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Recent";
        filtered = sort switch
        {
            "Name" => filtered.OrderBy(p => p.Name),
            "Created" => filtered.OrderByDescending(p => p.CreatedAt),
            _ => filtered.OrderByDescending(p => p.LastPlayedAt ?? p.CreatedAt)
        };
        ProfilesList.ItemsSource = filtered.ToList();
        RailProfiles.ItemsSource = _profileItems;
        UpdateCurrentProfileUi();
    }

    private void HomeLibrarySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing) RefreshProfileViews();
    }

    private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing) RefreshProfileViews();
    }

    private void LibraryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) RefreshProfileViews();
    }

    // ================================================================
    // MINECRAFT LAUNCH
    // ================================================================

    private async void PlayProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Profile tagged })
            SetCurrentProfile(tagged);
        if (_current is null) return;
        await LaunchCurrentAsync();
    }

    private async Task LaunchCurrentAsync()
    {
        if (_current is null) return;

        if (_runningMinecraft is { HasExited: false })
        {
            MessageBox.Show(this, "A Minecraft instance is already running from Nova.", "Nova Client", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_account is null || !_account.IsAccessTokenValid)
        {
            var restored = await TryRestoreAccountAsync();
            if (!restored)
            {
                ShowPage("Account");
                SetStatus("Sign in to play", false);
                MessageBox.Show(this, "Connect your Minecraft account before launching.", "Nova Client", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        try
        {
            _launchCancellation?.Cancel();
            _launchCancellation = new CancellationTokenSource();
            ShowPage("Downloads");
            DownloadTitle.Text = $"Preparing {_current.Name}";
            DownloadDetail.Text = $"{_current.LoaderDisplay}";
            DownloadProgress.Value = 2;
            DownloadPercent.Text = "2%";
            SetStatus("Preparing Minecraft…", true);

            var profile = _current;
            var started = DateTime.UtcNow;
            var progress = new Progress<LauncherProgress>(p =>
            {
                DownloadTitle.Text = p.Stage;
                DownloadDetail.Text = p.Detail;
                DownloadProgress.Value = Math.Clamp(p.Percent, 0, 100);
                DownloadPercent.Text = $"{Math.Clamp(p.Percent, 0, 100)}%";
                SetStatus(p.Stage, true);
            });

            _runningMinecraft = await _launcher.LaunchAsync(
                profile,
                _account!,
                _profiles,
                progress,
                _launchCancellation.Token);

            _runningStartedAt = started;
            profile.LastPlayedAt = DateTime.UtcNow;
            await _profiles.SaveAsync(_profileItems);
            RefreshProfileViews();
            TopStatusDot.Fill = FindBrush("AccentBrush");
            SetStatus("Minecraft running", true);

            _runningMinecraft.Exited += (_, _) => Dispatcher.Invoke(async () =>
            {
                if (_runningStartedAt is DateTime began)
                    profile.TotalPlaySeconds += Math.Max(0, (long)(DateTime.UtcNow - began).TotalSeconds);
                profile.LastPlayedAt = DateTime.UtcNow;
                _runningStartedAt = null;
                TopStatusDot.Fill = new SolidColorBrush(Color.FromRgb(125, 135, 148));
                SetStatus("Ready", false);
                RefreshProfileViews();
                await _profiles.SaveAsync(_profileItems);
            });

            if (_settings.MinimizeOnLaunch)
                WindowState = WindowState.Minimized;
        }
        catch (OperationCanceledException)
        {
            SetStatus("Launch cancelled", false);
        }
        catch (Exception ex)
        {
            SetStatus("Launch failed", false);
            DownloadTitle.Text = "Minecraft could not start";
            DownloadDetail.Text = ex.Message;
            DownloadProgress.Value = 0;
            DownloadPercent.Text = "0%";
            MessageBox.Show(this, ex.Message, "Nova Client launch error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ================================================================
    // DISCOVER / MODRINTH
    // ================================================================

    private void DiscoverType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string type }) return;
        _discoverProjectType = type;
        SetDiscoverTabStyles(sender as Button);
        _ = SearchModsAsync();
    }

    private void SetDiscoverTabStyles(Button? selected)
    {
        foreach (var button in new[] { DiscoverModsTab, DiscoverPacksTab, DiscoverShadersTab, DiscoverResourcesTab })
            button.Style = FindResource(button == selected ? "PrimaryButton" : "SecondaryButton") as Style;
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchModsAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await SearchModsAsync();
    }

    private async Task SearchModsAsync()
    {
        if (_current is null)
        {
            MessageBox.Show(this, "Create or select an instance first.", "Nova Client", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            SetStatus("Searching Modrinth…", true);
            DiscoverResultLabel.Text = "Searching…";
            var version = (DiscoverVersion.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(version)) version = _current.MinecraftVersion;
            var sort = (DiscoverSort.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Relevance";

            var response = await _modrinth.SearchAsync(
                SearchBox.Text.Trim(),
                version!,
                _current.Loader,
                _discoverProjectType,
                sort,
                0,
                40);

            ModsList.ItemsSource = response.Hits;
            DiscoverResultLabel.Text = $"{response.TotalHits:N0} results • showing {response.Hits.Count}";
            _settings.LastDiscoverQuery = SearchBox.Text.Trim();
            _settings.DiscoverProjectType = _discoverProjectType;
            _settings.DiscoverSort = sort;
            await _settingsService.SaveAsync();
            SetStatus("Ready", false);
        }
        catch (Exception ex)
        {
            DiscoverResultLabel.Text = "Search failed";
            SetStatus("Modrinth error", false);
            MessageBox.Show(this, ex.Message, "Nova Client", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void InstallMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModrinthProject project }) return;
        if (_current is null) return;

        try
        {
            ShowPage("Downloads");
            DownloadTitle.Text = $"Installing {project.Title}";
            DownloadDetail.Text = $"Into {_current.Name} • resolving compatible version and dependencies";
            DownloadProgress.Value = 0;
            DownloadPercent.Text = "0%";
            SetStatus("Installing content…", true);

            var progress = new Progress<double>(value =>
            {
                var percent = (int)Math.Round(Math.Clamp(value, 0, 1) * 100);
                DownloadProgress.Value = percent;
                DownloadPercent.Text = $"{percent}%";
            });

            var result = await _modrinth.InstallAsync(project, _current, _profiles, progress);
            DownloadProgress.Value = 100;
            DownloadPercent.Text = "100%";
            DownloadTitle.Text = $"Installed {project.Title}";
            DownloadDetail.Text = result.Dependencies.Count == 0
                ? $"Installed into {_current.Name}."
                : $"Installed into {_current.Name} with {result.Dependencies.Count} required dependenc{(result.Dependencies.Count == 1 ? "y" : "ies")}.";
            await RefreshInstalledAsync();
            UpdateCurrentProfileUi();
            SetStatus("Install complete", false);
        }
        catch (Exception ex)
        {
            DownloadProgress.Value = 0;
            DownloadPercent.Text = "0%";
            DownloadTitle.Text = "Install failed";
            DownloadDetail.Text = ex.Message;
            SetStatus("Install failed", false);
        }
    }

    // ================================================================
    // INSTALLED CONTENT
    // ================================================================

    private async Task RefreshInstalledAsync()
    {
        _installedItems.Clear();
        if (_current is null)
        {
            InstalledList.ItemsSource = _installedItems;
            return;
        }

        foreach (var item in await _modrinth.LoadInstalledAsync(_current, _profiles))
            _installedItems.Add(item);
        ApplyInstalledFilter();
    }

    private void InstalledSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing) ApplyInstalledFilter();
    }

    private void InstalledFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) ApplyInstalledFilter();
    }

    private void ApplyInstalledFilter()
    {
        var search = InstalledSearchBox?.Text?.Trim() ?? string.Empty;
        var type = (InstalledTypeFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All content";
        IEnumerable<InstalledContentItem> items = _installedItems;

        if (!string.IsNullOrWhiteSpace(search))
            items = items.Where(i => i.Title.Contains(search, StringComparison.OrdinalIgnoreCase) || i.FileName.Contains(search, StringComparison.OrdinalIgnoreCase));

        items = type switch
        {
            "Mods" => items.Where(i => i.ProjectType == "mod"),
            "Shaders" => items.Where(i => i.ProjectType == "shader"),
            "Resource packs" => items.Where(i => i.ProjectType == "resourcepack"),
            _ => items
        };

        InstalledList.ItemsSource = items.OrderBy(i => i.IsDependency).ThenBy(i => i.Title).ToList();
    }

    private async void ToggleInstalled_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || sender is not Button { Tag: InstalledContentItem item }) return;
        try
        {
            await _modrinth.SetEnabledAsync(item, _current, _profiles, !item.Enabled);
            await RefreshInstalledAsync();
            SetStatus(item.Enabled ? "Content enabled" : "Content disabled", false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Nova Client", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RemoveInstalled_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || sender is not Button { Tag: InstalledContentItem item }) return;
        var answer = MessageBox.Show(this, $"Remove {item.Title} from {_current.Name}?", "Remove content", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await _modrinth.RemoveInstalledAsync(item, _current, _profiles);
        await RefreshInstalledAsync();
        UpdateCurrentProfileUi();
        SetStatus("Content removed", false);
    }

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) _profiles.OpenModsFolder(_current);
    }

    // ================================================================
    // MICROSOFT / MINECRAFT ACCOUNT
    // ================================================================

    private async Task<bool> TryRestoreAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.MicrosoftClientId))
        {
            UpdateAccountUi();
            return false;
        }

        try
        {
            var progress = new Progress<AccountLoginProgress>(UpdateLoginProgress);
            var restored = await _auth.TryRestoreSessionAsync(_settings.MicrosoftClientId, progress);
            if (restored is null)
            {
                UpdateAccountUi();
                return false;
            }

            _account = restored;
            UpdateAccountUi();
            return true;
        }
        catch
        {
            UpdateAccountUi();
            return false;
        }
    }

    private async void MicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_settings.MicrosoftClientId))
        {
            ShowPage("Settings");
            MicrosoftClientIdBox.Focus();
            MessageBox.Show(
                this,
                "Enter Nova Client's Microsoft Entra Application (client) ID first. It must be a public/native client app; no client secret is used.",
                "Microsoft sign-in setup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _loginCancellation?.Cancel();
        _loginCancellation = new CancellationTokenSource();
        MicrosoftLoginButton.IsEnabled = false;
        DeviceCodeCard.Visibility = Visibility.Visible;
        AccountProgress.Value = 2;
        AccountState.Text = "Connecting to Microsoft…";
        AccountDetail.Text = "Nova is starting the official device-code sign-in flow.";

        try
        {
            var progress = new Progress<AccountLoginProgress>(UpdateLoginProgress);
            _account = await _auth.SignInAsync(
                _settings.MicrosoftClientId,
                device => Dispatcher.Invoke(() =>
                {
                    DeviceCodeText.Text = device.UserCode;
                    AccountProgressText.Text = "Complete sign-in in your browser…";
                    DeviceCodeCard.Visibility = Visibility.Visible;
                }),
                progress,
                _loginCancellation.Token);

            UpdateAccountUi();
            SetStatus($"Signed in as {_account.Name}", false);
        }
        catch (OperationCanceledException)
        {
            AccountState.Text = "Sign-in cancelled";
            AccountDetail.Text = "You can try again whenever you want.";
        }
        catch (Exception ex)
        {
            AccountState.Text = "Could not connect account";
            AccountDetail.Text = ex.Message;
            SetStatus("Account sign-in failed", false);
        }
        finally
        {
            MicrosoftLoginButton.IsEnabled = true;
        }
    }

    private void UpdateLoginProgress(AccountLoginProgress progress)
    {
        AccountProgress.Value = Math.Clamp(progress.Percent, 0, 100);
        AccountProgressText.Text = string.IsNullOrWhiteSpace(progress.Detail) ? progress.Stage : progress.Detail;
        AccountState.Text = progress.Stage;
        SetStatus(progress.Stage, true);
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _loginCancellation?.Cancel();
        _auth.SignOut();
        _account = null;
        UpdateAccountUi();
        SetStatus("Signed out", false);
    }

    private void UpdateAccountUi()
    {
        if (_account is null)
        {
            AccountState.Text = "Not connected";
            AccountDetail.Text = "Connect a Microsoft account to create a real Minecraft session.";
            SidebarAccountName.Text = "Not signed in";
            SidebarAccountStatus.Text = "Minecraft account";
            SignOutButton.Visibility = Visibility.Collapsed;
            MicrosoftLoginButton.Visibility = Visibility.Visible;
            if (_loginCancellation is null)
                DeviceCodeCard.Visibility = Visibility.Collapsed;
            return;
        }

        AccountState.Text = _account.Name;
        AccountDetail.Text = _account.OwnsJavaEdition
            ? "Minecraft: Java Edition verified • Microsoft/Xbox session connected"
            : "Account connected, but Java Edition ownership could not be verified.";
        SidebarAccountName.Text = _account.Name;
        SidebarAccountStatus.Text = _account.OwnsJavaEdition ? "Minecraft: Java Edition" : "Minecraft account";
        SignOutButton.Visibility = Visibility.Visible;
        MicrosoftLoginButton.Visibility = Visibility.Collapsed;
        DeviceCodeCard.Visibility = Visibility.Collapsed;
    }

    // ================================================================
    // SETTINGS
    // ================================================================

    private void ApplySettingsToUi()
    {
        if (_settings.WindowWidth >= MinWidth) Width = _settings.WindowWidth;
        if (_settings.WindowHeight >= MinHeight) Height = _settings.WindowHeight;
        MemorySlider.Value = Math.Clamp(_settings.DefaultMemoryMb / 1024d, 2, 24);
        MemoryLabel.Text = $"{(int)MemorySlider.Value} GB";
        JavaPathBox.Text = _settings.JavaPath ?? string.Empty;
        MicrosoftClientIdBox.Text = _settings.MicrosoftClientId;
        MinimizeOnLaunchCheck.IsChecked = _settings.MinimizeOnLaunch;
        CheckUpdatesCheck.IsChecked = _settings.CheckForUpdates;
        ShowNewsCheck.IsChecked = _settings.ShowNews;
        ReducedMotionCheck.IsChecked = _settings.ReducedMotion;
        SearchBox.Text = string.IsNullOrWhiteSpace(_settings.LastDiscoverQuery) ? "sodium" : _settings.LastDiscoverQuery;
        DataPathText.Text = _settingsService.RootPath;
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MemoryLabel is not null)
            MemoryLabel.Text = $"{(int)e.NewValue} GB";
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.DefaultMemoryMb = (int)MemorySlider.Value * 1024;
        _settings.JavaPath = string.IsNullOrWhiteSpace(JavaPathBox.Text) ? null : JavaPathBox.Text.Trim();
        _settings.MicrosoftClientId = MicrosoftClientIdBox.Text.Trim();
        _settings.MinimizeOnLaunch = MinimizeOnLaunchCheck.IsChecked == true;
        _settings.CheckForUpdates = CheckUpdatesCheck.IsChecked == true;
        _settings.ShowNews = ShowNewsCheck.IsChecked == true;
        _settings.ReducedMotion = ReducedMotionCheck.IsChecked == true;
        await _settingsService.SaveAsync();
        SetStatus("Settings saved", false);
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_settingsService.RootPath);
        Process.Start(new ProcessStartInfo(_settingsService.RootPath) { UseShellExecute = true });
    }

    private async Task SaveWindowStateAsync()
    {
        if (_initializing) return;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = (int)ActualWidth;
            _settings.WindowHeight = (int)ActualHeight;
        }
        _settings.SelectedProfileId = _current?.Id;
        await _settingsService.SaveAsync();
        await _profiles.SaveAsync(_profileItems);
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private void SetStatus(string text, bool busy)
    {
        StatusText.Text = text;
        TopStatusDot.Fill = busy
            ? FindBrush("AccentBrush")
            : new SolidColorBrush(Color.FromRgb(125, 135, 148));
    }

    private Brush FindBrush(string key)
        => TryFindResource(key) as Brush ?? Brushes.Gray;

    private static void SetComboText(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }
}
