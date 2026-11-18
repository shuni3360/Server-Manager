using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ServerFavorites;

public sealed class Favorite
{
    public string Game { get; set; } = "";
    public string Address { get; set; } = "";
    public string Alias { get; set; } = "";
    public bool Pinned { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string Map { get; set; } = "—";
    [System.Text.Json.Serialization.JsonIgnore] public string Players { get; set; } = "—";
    [System.Text.Json.Serialization.JsonIgnore] public string Latency { get; set; } = "—";
    [System.Text.Json.Serialization.JsonIgnore] public bool QueryFailed { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public long QueryVersion { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] internal string Language { get; set; } = "zh-TW";
    [System.Text.Json.Serialization.JsonIgnore] public string MapDisplay =>
        !QueryFailed ? Map : Ping == null ? Languages.Text(Language, "MapNoResponse") : Languages.Text(Language, "MapStale", Map);
    [System.Text.Json.Serialization.JsonIgnore] public int? PlayerCount { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public long? Ping { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayName =>
        Alias.Length > 0 ? Alias : Name.Length > 0 ? Name : Address;
}

public sealed class Preferences
{
    public List<string> Games { get; set; } = ["CS2", "CS:GO"];
    public string SelectedGame { get; set; } = "CS2";
    public string Language { get; set; } = "zh-TW";
    public List<Favorite> Favorites { get; set; } = [];
    // Keep v0.1's JSON property names for existing group and favorite data.
    public Dictionary<string, SortPreference> Sorts { get; set; } = new(StringComparer.Ordinal);
    public List<SidebarEntry>? Sidebar { get; set; }
    public List<ColumnLayout>? Columns { get; set; }
}

public sealed record SortPreference(string Column, bool Descending);
public sealed record OnlinePlayer(string Name, int Score, string Duration);


public partial class MainWindow : Window
{
    private readonly string settingsPath;
    private Preferences preferences = new();
    private CancellationTokenSource? refresh;
    private bool ready;
    private bool rebuildingList;
    private long queryVersion;
    private CancellationTokenSource? singleRefresh;
    private Favorite? detailsItem;
    private CancellationTokenSource? detailsRequest;
    private const string ConnectPrefix = "steam://connect/";

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public MainWindow() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ServerFavorites", "favorites.json")) { }

    internal MainWindow(string settingsPath)
    {
        this.settingsPath = settingsPath;
        InitializeComponent();
        UseDarkCaption(this);
        PreviewMouseDown += (_, _) =>
        {
            if (LanguagePopup.IsOpen && !LanguageButton.IsMouseOver && !LanguageOptions.IsMouseOver)
                LanguagePopup.IsOpen = false;
        };
        Deactivated += (_, _) => LanguagePopup.IsOpen = false;
        ApplyLanguage();
        try
        {
            if (File.Exists(settingsPath))
                preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsPath)) ?? new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, T("LoadError") + "\n" + error.Message, T("AppTitle"));
            Loaded += (_, _) => Close();
            return;
        }
        preferences.Sidebar ??= preferences.Games.Select(SidebarEntry.Group).ToList();
        ApplyLanguage();
        InitializeColumns();
        ready = true;
        RenderGames();
        ShowFavorites();
        Loaded += async (_, _) => await Refresh();
        Closing += (_, _) => SaveColumnsIfPending();
        Closed += (_, _) =>
        {
            StopPlayerScroll();
            refresh?.Cancel();
            detailsRequest?.Cancel();
            singleRefresh?.Cancel();
        };
    }

    private string T(string key, params object[] values) => Languages.Text(preferences.Language, key, values);

    private void ApplyLanguage()
    {
        preferences.Language = Languages.Normalize(preferences.Language);
        foreach (string key in Languages.Keys) Resources[key] = T(key);
        Resources["AddServerAction"] = "＋ " + T("AddServer");
        PlayerNameColumn.Header = T("PlayerName");
        LanguageLabel.Text = Languages.Choices.Single(choice => choice.Code == preferences.Language).Label;
        foreach (var favorite in preferences.Favorites) favorite.Language = preferences.Language;
    }

    private void ToggleLanguageMenu(object sender, RoutedEventArgs args)
    {
        if (LanguagePopup.IsOpen)
        {
            LanguagePopup.IsOpen = false;
            return;
        }
        LanguageOptions.Children.Clear();
        Button? activeOption = null;
        foreach (var choice in Languages.Choices)
        {
            bool selected = choice.Code == preferences.Language;
            var entry = new Button
            {
                Content = (selected ? "✓  " : "     ") + choice.Label,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brush(selected ? "#383647" : "#272830"),
                BorderBrush = Brushes.Transparent,
                Style = (Style)FindResource(typeof(Button))
            };
            if (selected) activeOption = entry;
            entry.Click += (_, _) =>
            {
                LanguagePopup.IsOpen = false;
                LanguageButton.Focus();
                if (choice.Code == preferences.Language) return;
                preferences.Language = choice.Code;
                ApplyLanguage();
                RenderGames();
                ShowFavorites();
                if (refresh != null && Servers.HasItems) Status.Text = T("Refreshing");
                Save();
            };
            LanguageOptions.Children.Add(entry);
        }
        LanguagePopup.IsOpen = true;
        activeOption?.Focus();
    }

    private void LanguagePopupKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return;
        LanguagePopup.IsOpen = false;
        LanguageButton.Focus();
        args.Handled = true;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            string temporary = settingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, settingsPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status.Text = T("SaveError", error.Message);
        }
    }

    private void ShowFavorites()
    {
        if (!ready) return;
        var selected = Servers.SelectedItem as Favorite;
        string group = preferences.SelectedGame;
        string filter = Search.Text.Trim();
        var items = preferences.Favorites.Where(item => item.Game == group).ToList();
        AddServerButton.IsEnabled = preferences.Games.Count > 0;
        GroupTitle.Text = group.Length == 0 ? T("Groups") : group;
        GroupTitle.ToolTip = group;
        ServerCount.Text = T(items.Count == 1 ? "CountOne" : "Count", items.Count);
        var list = OrderFavorites(items.Where(item =>
            item.Alias.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            item.Address.Contains(filter, StringComparison.OrdinalIgnoreCase)),
            preferences.Sorts.GetValueOrDefault(group)).ToList();
        rebuildingList = true;
        try
        {
            Servers.ItemsSource = list;
            if (selected != null && list.Contains(selected)) Servers.SelectedItem = selected;
        }
        finally { rebuildingList = false; }
        JoinButton.IsEnabled = Servers.SelectedItem is Favorite;
        if (detailsItem != null && Servers.SelectedItem == null) CloseDetails();
        if (preferences.Games.Count == 0) Status.Text = T("CreateGroupFirst");
        else if (list.Count == 0) Status.Text = filter.Length == 0 ? T("NoFavorites") : T("NoMatches");
        else if (refresh == null) Status.Text = "";
        UpdateSortHeaders();
        UpdateDetailsSummary();
    }

    private static IEnumerable<Favorite> OrderFavorites(IEnumerable<Favorite> items, SortPreference? sort)
    {
        // Pinned rows remain together at the top, with the same ordering inside each section.
        var ordered = items.OrderByDescending(item => item.Pinned);
        return sort?.Column switch
        {
            "Name" => ThenBy(ordered, item => item.DisplayName, sort.Descending, Comparer<string>.Create(NaturalNameComparison)),
            "Map" => ThenBy(ordered.ThenBy(item => item.Ping == null), item => item.Map, sort.Descending, StringComparer.CurrentCultureIgnoreCase),
            "Players" => ThenBy(ordered.ThenBy(item => item.PlayerCount == null), item => item.PlayerCount, sort.Descending),
            "Latency" => ThenBy(ordered.ThenBy(item => item.Ping == null), item => item.Ping, sort.Descending),
            _ => ordered
        };
    }

    private static IOrderedEnumerable<Favorite> ThenBy<T>(IOrderedEnumerable<Favorite> items,
        Func<Favorite, T> key, bool descending, IComparer<T>? comparer = null) =>
        descending ? items.ThenByDescending(key, comparer) : items.ThenBy(key, comparer);

    private void SortClick(object sender, RoutedEventArgs args)
    {
        if (args.OriginalSource is not GridViewColumnHeader { Tag: string column }) return;
        string group = preferences.SelectedGame;
        var previous = preferences.Sorts.GetValueOrDefault(group);
        preferences.Sorts[group] = new SortPreference(column, previous?.Column == column && !previous.Descending);
        ShowFavorites();
        Save();
    }

    private void UpdateSortHeaders()
    {
        var sort = preferences.Sorts.GetValueOrDefault(preferences.SelectedGame);
        foreach (var (header, label) in new[] { (NameHeader, T("Name")), (MapHeader, T("Map")),
                     (PlayersHeader, T("Players")), (LatencyHeader, T("Latency")) })
        {
            if ((string)header.Tag != sort?.Column)
            {
                header.Content = label;
                continue;
            }
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = label });
            content.Children.Add(new TextBlock { Text = sort.Descending ? "▼" : "▲",
                Foreground = Brush("#ACA1CF"), Margin = new Thickness(5, 0, 0, 0) });
            header.Content = content;
        }
    }

    private void SearchChanged(object sender, TextChangedEventArgs args)
    {
        if (!ready) return;
        ShowFavorites();
    }

    private void ResizeDetailsPane(object sender, SizeChangedEventArgs args)
    {
        if (DetailsPane != null)
            DetailsPane.Width = Math.Clamp(args.NewSize.Width - 535, 225, 314);
    }

    private const double PlayerScrollBarWidth = 7;
    private const double PlayerWheelDistance = 104;
    private const double PlayerWheelDurationMs = 160;
    private ScrollViewer? playerScrollViewer;
    private bool playerScrollAnimating;
    private double playerScrollStart;
    private double playerScrollTarget;
    private long playerScrollStartedAt;

    private void InitializePlayerScroll(object sender, RoutedEventArgs args)
    {
        playerScrollViewer = NarrowVerticalScrollBar(PlayerList);
    }

    private void InitializeServerScroll(object sender, RoutedEventArgs args) => NarrowVerticalScrollBar(Servers);

    private ScrollViewer? NarrowVerticalScrollBar(ListView list)
    {
        list.ApplyTemplate();
        var viewer = FindScrollViewer(list);
        viewer?.ApplyTemplate();
        if (viewer?.Template.FindName("PART_VerticalScrollBar", viewer) is ScrollBar bar)
        {
            bar.Style = (Style)FindResource("DarkScrollBar");
            bar.MinWidth = 0;
            bar.Width = PlayerScrollBarWidth;
            bar.MaxWidth = PlayerScrollBarWidth;
        }
        return viewer;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } descendant) return descendant;
        }
        return null;
    }

    private void ResizePlayerColumn(object sender, SizeChangedEventArgs args)
    {
        if (PlayerNameColumn != null)
            PlayerNameColumn.Width = Math.Max(0, args.NewSize.Width - PlayerScrollBarWidth);
    }

    private void ScrollPlayersWithWheel(object sender, MouseWheelEventArgs args)
    {
        if (playerScrollViewer is not { } viewer || viewer.ScrollableHeight <= 0 || args.Delta == 0) return;
        double from = playerScrollAnimating ? playerScrollTarget : viewer.VerticalOffset;
        playerScrollTarget = Math.Clamp(from - PlayerWheelDistance * args.Delta / Mouse.MouseWheelDeltaForOneLine,
            0, viewer.ScrollableHeight);
        args.Handled = true;

        if (!SystemParameters.ClientAreaAnimation)
        {
            StopPlayerScroll();
            viewer.ScrollToVerticalOffset(playerScrollTarget);
            return;
        }

        playerScrollStart = viewer.VerticalOffset;
        playerScrollStartedAt = Stopwatch.GetTimestamp();
        if (playerScrollAnimating) return;
        playerScrollAnimating = true;
        CompositionTarget.Rendering += AnimatePlayerScroll;
    }

    private void AnimatePlayerScroll(object? sender, EventArgs args)
    {
        if (playerScrollViewer is not { } viewer || !viewer.IsVisible)
        {
            StopPlayerScroll();
            return;
        }

        double elapsed = Stopwatch.GetElapsedTime(playerScrollStartedAt).TotalMilliseconds;
        double progress = Math.Min(1, elapsed / PlayerWheelDurationMs);
        double eased = 1 - Math.Pow(1 - progress, 3);
        double target = Math.Min(playerScrollTarget, viewer.ScrollableHeight);
        viewer.ScrollToVerticalOffset(playerScrollStart + (target - playerScrollStart) * eased);
        if (progress >= 1) StopPlayerScroll();
    }

    private void StopPlayerScrollOnInput(object sender, RoutedEventArgs args) => StopPlayerScroll();

    private void StopPlayerScroll()
    {
        if (!playerScrollAnimating) return;
        CompositionTarget.Rendering -= AnimatePlayerScroll;
        playerScrollAnimating = false;
    }

    private async void SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!ready || rebuildingList) return;
        JoinButton.IsEnabled = Servers.SelectedItem is Favorite;
        if (DetailsPane.Visibility != Visibility.Visible) return;
        if (Servers.SelectedItem is Favorite item)
        {
            if (detailsItem != item) await ShowDetails(item);
        }
        else CloseDetails();
    }

    private void SelectRow(object sender, MouseButtonEventArgs args) => ((ListViewItem)sender).IsSelected = true;
    private void RowDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || ((ListViewItem)sender).Content is not Favorite item) return;
        args.Handled = true;
        Join(item);
    }

    private void JoinClick(object sender, RoutedEventArgs args)
    {
        if (Servers.SelectedItem is Favorite item) Join(item);
    }

    private void Join(Favorite item)
    {
        if (!TryAddress(item.Address, out string address, out _, out _)) return;
        try
        {
            Process.Start(new ProcessStartInfo(ConnectPrefix + address) { UseShellExecute = true })?.Dispose();
            Status.Text = T("SteamRequest", item.DisplayName);
        }
        catch (Win32Exception error)
        {
            Status.Text = T("SteamError", error.Message);
        }
    }

    private void EditGroup(string? original, SidebarEntry? category = null)
    {
        var dialog = new EditDialog(this, original == null ? T(category == null ? "AddGroup" : "AddGroupHere") : T("RenameGroup"),
            [(T("GroupName"), original ?? "")], original == null ? T("Add") : T("Save"));
        dialog.Validate = () =>
        {
            string value = dialog.Value(0);
            if (value.Length == 0) return T("GroupRequired");
            if (preferences.Games.Any(group => group != original &&
                string.Equals(group, value, StringComparison.OrdinalIgnoreCase))) return T("GroupDuplicate");
            return null;
        };
        if (dialog.ShowDialog() != true) return;
        string name = dialog.Value(0);
        if (original == null)
        {
            refresh?.Cancel();
            CloseDetails();
            preferences.Games.Add(name);
            if (category == null) SidebarEntries.Add(SidebarEntry.Group(name));
            else
            {
                category.Groups!.Add(name);
                category.Expanded = true;
            }
            preferences.SelectedGame = name;
        }
        else
        {
            SidebarEntry? parent = ParentOf(original);
            if (parent == null) SidebarEntries.First(entry => entry.Groups == null && entry.Name == original).Name = name;
            else parent.Groups![parent.Groups.IndexOf(original)] = name;
            preferences.Games[preferences.Games.IndexOf(original)] = name;
            foreach (var item in preferences.Favorites.Where(item => item.Game == original)) item.Game = name;
            if (preferences.SelectedGame == original) preferences.SelectedGame = name;
            if (preferences.Sorts.Remove(original, out var sort)) preferences.Sorts[name] = sort;
        }
        RenderGames();
        ShowFavorites();
        Save();
    }

    private async Task DeleteGroup(string group)
    {
        int count = preferences.Favorites.Count(item => item.Game == group);
        string message = count == 0 ? T("DeleteEmptyGroup", group)
            : T(count == 1 ? "DeleteGroupWithOneServer" : "DeleteGroupWithServers", group, count);
        var dialog = new EditDialog(this, T("DeleteGroup"), [], T("Delete"), message: message);
        if (dialog.ShowDialog() != true) return;

        int index = preferences.Games.IndexOf(group);
        if (index < 0) return;
        bool selected = preferences.SelectedGame == group;
        RemoveGroupFromSidebar(group);
        preferences.Games.RemoveAt(index);
        preferences.Favorites.RemoveAll(item => item.Game == group);
        preferences.Sorts.Remove(group);
        if (selected)
        {
            refresh?.Cancel();
            CloseDetails();
            preferences.SelectedGame = preferences.Games.Count == 0 ? ""
                : preferences.Games[Math.Min(index, preferences.Games.Count - 1)];
        }
        RenderGames();
        ShowFavorites();
        Save();
        if (selected && preferences.Games.Count > 0) await Refresh();
    }

    private void ServerMenuOpening(object sender, ContextMenuEventArgs args)
    {
        bool keyboard = args.CursorLeft == -1 && args.CursorTop == -1;
        var item = keyboard ? Servers.SelectedItem as Favorite
            : Ancestor<ListViewItem>(args.OriginalSource)?.Content as Favorite;
        if (item == null)
        {
            args.Handled = true;
            return;
        }

        Servers.SelectedItem = item;
        var menu = Servers.ContextMenu;
        foreach (var menuItem in menu.Items.OfType<MenuItem>())
            if (menuItem.Tag is string key) menuItem.Header = T(key);
        PinMenu.Header = item.Pinned ? T("Unpin") : T("Pin");
        MoveMenu.Items.Clear();
        foreach (string group in preferences.Games.Where(group => group != item.Game))
        {
            var entry = new MenuItem { Header = group, Style = (Style)FindResource("DarkMenuItem") };
            entry.IsEnabled = CanMoveFavorite(item, group);
            entry.Click += (_, _) => MoveFavorite(item, group);
            MoveMenu.Items.Add(entry);
        }
        MoveMenu.IsEnabled = MoveMenu.HasItems;
    }

    private async void AddServer(object sender, RoutedEventArgs args) => await EditFavorite(null);

    private async void EditServer(object sender, RoutedEventArgs args)
    {
        if (Servers.SelectedItem is Favorite item) await EditFavorite(item);
    }

    private async Task EditFavorite(Favorite? item)
    {
        var dialog = new EditDialog(this, item == null ? T("AddServer") : T("EditServer"),
            [(T("Address"), item?.Address ?? ""), (T("Alias"), item?.Alias ?? "")],
            item == null ? T("Add") : T("Save"), firstFieldHint: T("AddressHint"));
        dialog.NormalizePastedInput(0, NormalizeAddressInput);
        string address = "";
        dialog.Validate = () =>
        {
            if (!TryAddress(NormalizeAddressInput(dialog.Value(0)), out address, out _, out _))
                return T("AddressInvalid");
            return preferences.Favorites.Any(existing => existing != item &&
                existing.Game == preferences.SelectedGame &&
                string.Equals(existing.Address, address, StringComparison.OrdinalIgnoreCase))
                ? T("DuplicateFavorite") : null;
        };
        if (dialog.ShowDialog() != true) return;
        if (item == null)
        {
            item = new Favorite { Game = preferences.SelectedGame, Language = preferences.Language };
            preferences.Favorites.Add(item);
        }
        if (item.Address != address)
        {
            item.Name = "";
            item.Map = item.Players = item.Latency = "—";
            item.Ping = null;
            item.PlayerCount = null;
            item.QueryFailed = false;
            if (detailsItem == item) CloseDetails();
        }
        item.Address = address;
        item.Alias = dialog.Value(1);
        ShowFavorites();
        Servers.SelectedItem = item;
        Save();
        await Refresh();
    }

    private void TogglePin(object sender, RoutedEventArgs args)
    {
        if (Servers.SelectedItem is not Favorite item) return;
        item.Pinned = !item.Pinned;
        ShowFavorites();
        Save();
    }

    private void CopyAddress(object sender, RoutedEventArgs args)
    {
        if (Servers.SelectedItem is Favorite item) Copy(item.Address);
    }

    private void CopyLink(object sender, RoutedEventArgs args)
    {
        if (Servers.SelectedItem is Favorite item && TryAddress(item.Address, out string address, out _, out _))
            Copy(ConnectPrefix + address);
    }

    private void RemoveServer(object sender, RoutedEventArgs args)
    {
        if (Servers.SelectedItem is not Favorite item) return;
        var dialog = new EditDialog(this, T("RemoveFavorite"), [], T("Remove"),
            message: T("RemovePrompt", item.DisplayName));
        if (dialog.ShowDialog() != true) return;
        preferences.Favorites.Remove(item);
        if (detailsItem == item) CloseDetails();
        ShowFavorites();
        Save();
    }

    private async void RefreshClick(object sender, RoutedEventArgs args) =>
        await Task.WhenAll(Refresh(), LoadPlayers());

    private async void ViewDetails(object sender, RoutedEventArgs args)
    {
        if (Servers.SelectedItem is Favorite item) await ShowDetails(item);
    }

    private async Task ShowDetails(Favorite item)
    {
        singleRefresh?.Cancel();
        detailsItem = item;
        DetailsPane.Visibility = Visibility.Visible;
        JoinButton.Visibility = Visibility.Collapsed;
        UpdateDetailsSummary();
        await LoadPlayers();
    }

    private void UpdateDetailsSummary()
    {
        if (detailsItem is not Favorite item) return;
        DetailsName.Text = item.DisplayName;
        DetailsName.ToolTip = item.DisplayName;
        DetailsOriginal.Text = item.Name;
        DetailsOriginal.ToolTip = item.Name;
        DetailsOriginal.Visibility = item.Name.Length > 0 && item.Name != item.DisplayName
            ? Visibility.Visible : Visibility.Collapsed;
        DetailsAddress.Text = item.Address;
        DetailsAddress.ToolTip = item.Address;
        DetailsMap.Text = item.Map;
        DetailsMap.ToolTip = item.Map;
        DetailsCount.Text = item.Players;
        DetailsLatency.Text = item.Latency;
        DetailsQueryStatus.Text = item.Ping == null ? T("NoResponse") : T("StaleData");
        DetailsQueryStatus.Visibility = item.QueryFailed ? Visibility.Visible : Visibility.Collapsed;
        UpdatePlayerStatus();
    }

    private void CloseDetailsClick(object sender, RoutedEventArgs args) => CloseDetails();
    private void CloseDetails()
    {
        detailsRequest?.Cancel();
        singleRefresh?.Cancel();
        detailsItem = null;
        DetailsPane.Visibility = Visibility.Collapsed;
        JoinButton.Visibility = Visibility.Visible;
    }

    private void CopyDetailsAddress(object sender, RoutedEventArgs args)
    {
        if (detailsItem is Favorite item) Copy(item.Address);
    }

    private void JoinDetails(object sender, RoutedEventArgs args)
    {
        if (detailsItem is Favorite item) Join(item);
    }

    private async void RefreshDetailsClick(object sender, RoutedEventArgs args)
    {
        if (detailsItem is not Favorite item) return;
        singleRefresh?.Cancel();
        using var request = new CancellationTokenSource();
        singleRefresh = request;
        long version = ++queryVersion;
        try
        {
            await Task.WhenAll(RefreshFavorite(item, version, request.Token), LoadPlayers());
            if (!request.IsCancellationRequested) ShowFavorites();
        }
        finally
        {
            if (singleRefresh == request) singleRefresh = null;
        }
    }

    private async Task LoadPlayers()
    {
        if (detailsItem is not Favorite item) return;
        detailsRequest?.Cancel();
        using var request = new CancellationTokenSource();
        detailsRequest = request;
        StopPlayerScroll();
        PlayerList.ItemsSource = null;
        playerScrollViewer?.ScrollToTop();
        UpdatePlayerStatus();
        try
        {
            var players = await QueryPlayers(item.Address, request.Token);
            if (request.IsCancellationRequested || detailsItem != item) return;
            PlayerList.ItemsSource = players;
        }
        finally
        {
            if (detailsRequest == request)
            {
                detailsRequest = null;
                UpdatePlayerStatus();
            }
        }
    }

    private void UpdatePlayerStatus()
    {
        if (detailsItem is not Favorite item) return;
        PlayerStatus.Text = detailsRequest != null ? T("LoadingPlayers")
            : PlayerList.ItemsSource is not List<OnlinePlayer> players ? T("PlayerListUnavailable")
            : players.Count > 0 ? ""
            : !item.QueryFailed && item.PlayerCount > 0 ? T("PlayerListUnavailable") : T("NoPlayers");
    }

    private async Task Refresh()
    {
        refresh?.Cancel();
        using var request = new CancellationTokenSource();
        refresh = request;
        long version = ++queryVersion;
        var items = preferences.Favorites.Where(item => item.Game == preferences.SelectedGame).ToList();
        if (items.Count > 0) Status.Text = T("Refreshing");
        using var slots = new SemaphoreSlim(8);
        try
        {
            await Task.WhenAll(items.Select(async item =>
            {
                await slots.WaitAsync(request.Token);
                try { await RefreshFavorite(item, version, request.Token); }
                finally { slots.Release(); }
            }));
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (refresh == request)
            {
                refresh = null;
                ShowFavorites();
            }
        }
    }

    private async Task RefreshFavorite(Favorite item, long version, CancellationToken cancellationToken)
    {
        string address = item.Address;
        var result = await Query(address, cancellationToken);
        if (cancellationToken.IsCancellationRequested || item.Address != address || item.QueryVersion > version) return;
        item.QueryVersion = version;
        if (result is { } info) ApplyInfo(item, info);
        else item.QueryFailed = true;
    }

    private static void ApplyInfo(Favorite item, (string Name, string Map, int Count, int Capacity, long Latency) info)
    {
        item.QueryFailed = false;
        item.Name = info.Name;
        item.Map = info.Map;
        item.PlayerCount = info.Count;
        item.Players = info.Capacity == 0 ? $"{info.Count}/—" : $"{info.Count}/{info.Capacity}";
        item.Ping = info.Latency;
        item.Latency = $"{info.Latency} ms";
    }

    private static string NormalizeAddressInput(string input)
    {
        const string command = "connect";
        string address = input.Trim();
        return address.Length > command.Length && address.StartsWith(command, StringComparison.OrdinalIgnoreCase) &&
            char.IsWhiteSpace(address[command.Length])
            ? address[command.Length..].TrimStart()
            : address;
    }

    private static bool TryAddress(string input, out string address, out string host, out int port)
    {
        address = host = "";
        port = 0;
        int separator = input.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(input[(separator + 1)..], out port) || port is < 1 or > 65535) return false;
        string name = input[..separator];
        if (name.StartsWith('[') && name.EndsWith(']') &&
            IPAddress.TryParse(name[1..^1], out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 &&
            !name.Contains('%'))
        {
            host = ip.ToString();
            address = $"[{host}]:{port}";
            return true;
        }
        if (name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '.') ||
            Uri.CheckHostName(name) == UriHostNameType.Unknown) return false;
        host = name;
        address = $"{name}:{port}";
        return true;
    }

    private static async Task<(string Name, string Map, int Count, int Capacity, long Latency)?> Query(
        string address, CancellationToken cancellationToken)
    {
        byte[] query = new byte[] { 255, 255, 255, 255, 0x54 }
            .Concat(Encoding.ASCII.GetBytes("Source Engine Query\0")).ToArray();
        var response = await Exchange(address, query, 0x49, cancellationToken);
        return response is { } value ? ParseInfo(value.Data, value.Latency) : null;
    }

    private static (string Name, string Map, int Count, int Capacity, long Latency)? ParseInfo(byte[] data, long latency)
    {
        try
        {
            int offset = 6;
            string name = ReadString(data, ref offset);
            string map = ReadString(data, ref offset);
            ReadString(data, ref offset);
            ReadString(data, ref offset);
            if (offset + 4 > data.Length) return null;
            return (name, map, data[offset + 2], data[offset + 3], latency);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return null; }
    }

    private static async Task<List<OnlinePlayer>?> QueryPlayers(string address, CancellationToken cancellationToken)
    {
        byte[] query = [255, 255, 255, 255, 0x55, 255, 255, 255, 255];
        var response = await Exchange(address, query, 0x44, cancellationToken);
        return response is { } value ? ParsePlayers(value.Data) : null;
    }

    private static List<OnlinePlayer>? ParsePlayers(byte[] data)
    {
        try
        {
            if (data.Length < 6) return null;
            var players = new List<OnlinePlayer>();
            int offset = 6;
            for (int index = 0; index < data[5]; index++)
            {
                offset++; // Player index.
                string name = ReadString(data, ref offset);
                if (offset + 8 > data.Length) return null;
                int score = BitConverter.ToInt32(data, offset);
                float duration = BitConverter.ToSingle(data, offset + 4);
                offset += 8;
                string time = "—";
                if (float.IsFinite(duration) && duration >= 0 && duration < int.MaxValue)
                {
                    int seconds = (int)duration;
                    time = $"{seconds / 60} 分 {seconds % 60:00} 秒";
                }
                players.Add(new OnlinePlayer(name, score, time));
            }
            return players;
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return null; }
    }

    private static async Task<(byte[] Data, long Latency)?> Exchange(string address, byte[] query,
        byte responseType, CancellationToken cancellationToken)
    {
        if (!TryAddress(address, out _, out string host, out int port)) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            var ip = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault();
            if (ip == null) return null;
            using var client = new UdpClient(ip.AddressFamily);
            client.Connect(ip, port);
            var watch = Stopwatch.StartNew();
            await client.SendAsync(query.AsMemory(), timeout.Token);
            byte[] data = (await client.ReceiveAsync(timeout.Token)).Buffer;
            if (data.Length == 9 && BitConverter.ToInt32(data, 0) == -1 && data[4] == 0x41)
            {
                byte[] followup = query.Take(responseType == 0x44 ? 5 : query.Length)
                    .Concat(data.Skip(5)).ToArray();
                watch.Restart();
                await client.SendAsync(followup.AsMemory(), timeout.Token);
                data = (await client.ReceiveAsync(timeout.Token)).Buffer;
            }
            long latency = watch.ElapsedMilliseconds;
            if (data.Length >= 12 && BitConverter.ToInt32(data, 0) == -2)
                data = await ReadSplitResponse(client, data, timeout.Token) ?? [];
            if (data.Length < 6 || BitConverter.ToInt32(data, 0) != -1 || data[4] != responseType) return null;
            return (data, latency);
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> ReadSplitResponse(UdpClient client, byte[] first, CancellationToken token)
    {
        uint id = BitConverter.ToUInt32(first, 4);
        int count = first[8];
        // Source can also send bzip2-compressed replies; report those as unavailable.
        if ((id & 0x80000000) != 0 || count is < 1 or > 32) return null;
        var parts = new byte[count][];
        byte[] packet = first;
        for (int remaining = count; remaining > 0;)
        {
            if (packet.Length >= 12 && BitConverter.ToInt32(packet, 0) == -2 &&
                BitConverter.ToUInt32(packet, 4) == id && packet[8] == count &&
                packet[9] < count && parts[packet[9]] == null)
            {
                parts[packet[9]] = packet[12..];
                remaining--;
            }
            if (remaining > 0) packet = (await client.ReceiveAsync(token)).Buffer;
        }
        return parts.SelectMany(part => part).ToArray();
    }

    private static string ReadString(byte[] bytes, ref int offset)
    {
        int end = Array.IndexOf(bytes, (byte)0, offset);
        if (end < 0) throw new InvalidDataException();
        string value = Encoding.UTF8.GetString(bytes, offset, end - offset);
        offset = end + 1;
        return value;
    }

    internal static void UseDarkCaption(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            int enabled = 1;
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        };
    }

    internal static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}

internal sealed class EditDialog : Window
{
    private readonly List<TextBox> fields = [];
    public Func<string?>? Validate { get; set; }
    public string Value(int index) => fields[index].Text.Trim();

    public void NormalizePastedInput(int index, Func<string, string> normalize)
    {
        DataObject.AddPastingHandler(fields[index], (_, args) =>
        {
            if (args.DataObject.GetData(DataFormats.UnicodeText) is not string text) return;
            string normalized = normalize(text);
            if (normalized == text) return;
            args.DataObject = new DataObject(DataFormats.UnicodeText, normalized);
            args.FormatToApply = DataFormats.UnicodeText;
        });
    }

    public EditDialog(Window owner, string title, (string Label, string Value)[] inputs,
        string action, string? firstFieldHint = null, string? message = null)
    {
        Owner = owner;
        Title = title;
        Width = 390;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = owner.Background;
        Foreground = owner.Foreground;
        FontFamily = owner.FontFamily;
        FontSize = owner.FontSize;
        MainWindow.UseDarkCaption(this);
        var panel = new StackPanel { Margin = new Thickness(18, 15, 18, 15) };
        if (message != null) panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var error = new TextBlock
        {
            Foreground = MainWindow.Brush("#FFB2A8"), TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0)
        };
        foreach (var input in inputs)
        {
            bool first = fields.Count == 0;
            panel.Children.Add(new TextBlock { Text = input.Label, Margin = new Thickness(0, first ? 0 : 11, 0, 5) });
            var field = new TextBox { Text = input.Value, MinHeight = 33, Style = (Style)owner.FindResource(typeof(TextBox)) };
            System.Windows.Automation.AutomationProperties.SetName(field, input.Label);
            fields.Add(field);
            panel.Children.Add(field);
            if (!first) continue;
            if (firstFieldHint != null)
                panel.Children.Add(new TextBlock { Text = firstFieldHint, Foreground = MainWindow.Brush("#ADB8C0"),
                    FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(error);
        }
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = owner.FindResource("Cancel"), IsCancel = true,
            Style = (Style)owner.FindResource(typeof(Button)), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = action, IsDefault = true, Style = (Style)owner.FindResource("Primary") };
        save.Click += (_, _) =>
        {
            string? validation = Validate?.Invoke();
            if (validation == null) DialogResult = true;
            else
            {
                error.Text = validation;
                error.Visibility = Visibility.Visible;
                fields[0].Focus();
            }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) =>
        {
            if (fields.Count == 0) cancel.Focus();
            else
            {
                fields[0].Focus();
                fields[0].SelectAll();
            }
        };
    }
}
