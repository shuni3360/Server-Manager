using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ServerFavorites;

public sealed class SidebarEntry
{
    public string Name { get; set; } = "";
    // null identifies a top-level group; a category has a list even when empty.
    public List<string>? Groups { get; set; }
    public bool Expanded { get; set; }
    internal static SidebarEntry Group(string name) => new() { Name = name };
}

public partial class MainWindow
{
    private List<SidebarEntry> SidebarEntries => preferences.Sidebar!;
    private SidebarEntry? ParentOf(string group) => SidebarEntries.FirstOrDefault(entry => entry.Groups?.Contains(group) == true);

    private sealed record SidebarRow(string Name, SidebarEntry? Category, bool IsCategory);
    private SidebarRow? pendingSidebarDrag;
    private Favorite? pendingServerDrag;
    private Point dragOrigin;
    private DispatcherTimer? hoverExpandTimer;
    private SidebarEntry? hoverCategory;
    private Button? dropHighlight;

    private void RenderGames()
    {
        ClearDropFeedback();
        Sidebar.ContextMenu = SidebarMenu(
            ("AddCategory", () => EditCategory(null)),
            ("AddGroup", () => EditGroup(null)));
        Games.Children.Clear();
        foreach (var entry in SidebarEntries)
        {
            if (entry.Groups == null) RenderGroup(entry.Name, null);
            else
            {
                var button = SidebarButton(entry.Name, new(entry.Name, entry, true));
                button.Background = Brush("#24252E");
                button.FontWeight = FontWeights.SemiBold;
                var heading = new DockPanel();
                var arrow = new TextBlock
                {
                    Text = entry.Expanded ? "▾" : "▸", Foreground = Brush("#ACA1CF"),
                    Margin = new Thickness(0, 0, 6, 0)
                };
                heading.Children.Add(arrow);
                heading.Children.Add(new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                button.Content = heading;
                button.ContextMenu = SidebarMenu(
                    ("AddGroupHere", () => EditGroup(null, entry)),
                    ("RenameCategory", () => EditCategory(entry)),
                    ("DeleteCategory", () => DeleteCategory(entry)));
                button.Click += (_, _) => ToggleCategory(entry, !entry.Expanded);
                if (entry.Expanded)
                    foreach (string group in entry.Groups) RenderGroup(group, entry);
            }
        }
    }

    private Button SidebarButton(string name, SidebarRow row)
    {
        var button = new Button
        {
            Tag = row, Content = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(row.Category != null && !row.IsCategory ? 14 : 0, 0, 0, 5),
            Padding = new Thickness(9, 7, 9, 7), Background = Brush("#17181E"),
            BorderBrush = Brushes.Transparent
        };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        Games.Children.Add(button);
        return button;
    }

    private void RenderGroup(string group, SidebarEntry? parent)
    {
        var button = SidebarButton(group, new(group, parent, false));
        if (group == preferences.SelectedGame) button.Background = Brush("#333142");
        button.ContextMenu = SidebarMenu(
            ("RenameGroup", () => EditGroup(group)),
            ("DeleteGroup", async () => await DeleteGroup(group)));
        button.Click += async (_, _) =>
        {
            if (preferences.SelectedGame == group) return;
            CloseDetails();
            preferences.SelectedGame = group;
            RenderGames();
            ShowFavorites();
            Save();
            await Refresh();
        };
    }

    private ContextMenu SidebarMenu(params (string Key, Action Execute)[] actions)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("DarkMenu") };
        foreach (var (key, execute) in actions)
        {
            var item = new MenuItem { Header = T(key), Style = (Style)FindResource("DarkMenuItem") };
            if (key.StartsWith("Delete", StringComparison.Ordinal)) item.Foreground = Brush("#F4AEA7");
            item.Click += (_, _) => execute();
            menu.Items.Add(item);
        }
        return menu;
    }

    private void EditCategory(SidebarEntry? entry)
    {
        var dialog = new EditDialog(this, T(entry == null ? "AddCategory" : "RenameCategory"),
            [(T("CategoryName"), entry?.Name ?? "")], T(entry == null ? "Add" : "Save"));
        dialog.Validate = () =>
        {
            string name = dialog.Value(0);
            if (name.Length == 0) return T("CategoryRequired");
            return SidebarEntries.Any(category => category != entry && category.Groups != null &&
                string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)) ? T("CategoryDuplicate") : null;
        };
        if (dialog.ShowDialog() != true) return;
        if (entry == null) SidebarEntries.Add(new() { Name = dialog.Value(0), Groups = [], Expanded = true });
        else entry.Name = dialog.Value(0);
        RenderGames();
        Save();
    }

    private void DeleteCategory(SidebarEntry entry)
    {
        var dialog = new EditDialog(this, T("DeleteCategory"), [], T("Delete"), message: T("DeleteCategoryPrompt", entry.Name));
        if (dialog.ShowDialog() != true) return;
        int index = SidebarEntries.IndexOf(entry);
        SidebarEntries.RemoveAt(index);
        SidebarEntries.InsertRange(index, entry.Groups!.Select(SidebarEntry.Group));
        RenderGames();
        Save();
    }

    private void ToggleCategory(SidebarEntry entry, bool expanded)
    {
        entry.Expanded = expanded;
        RenderGames();
        Save();
    }

    private void RemoveGroupFromSidebar(string group)
    {
        if (ParentOf(group) is { } parent) parent.Groups!.Remove(group);
        else SidebarEntries.RemoveAll(entry => entry.Groups == null && entry.Name == group);
    }

    private bool CanMoveFavorite(Favorite favorite, string group) =>
        favorite.Game != group && preferences.Games.Contains(group) && preferences.Favorites.Contains(favorite) &&
        !preferences.Favorites.Any(other => other.Game == group &&
            string.Equals(other.Address, favorite.Address, StringComparison.OrdinalIgnoreCase));

    private void MoveFavorite(Favorite favorite, string group)
    {
        if (!CanMoveFavorite(favorite, group)) return;
        favorite.Game = group;
        if (detailsItem == favorite) CloseDetails();
        ShowFavorites();
        Save();
    }

    private static T? Ancestor<T>(object source) where T : DependencyObject
    {
        for (var element = source as DependencyObject; element != null; element = element switch
        {
            Visual => VisualTreeHelper.GetParent(element),
            FrameworkContentElement content => content.Parent,
            _ => null
        })
            if (element is T target) return target;
        return null;
    }

    private void SidebarMouseDown(object sender, MouseButtonEventArgs args)
    {
        pendingSidebarDrag = Ancestor<Button>(args.OriginalSource)?.Tag as SidebarRow;
        dragOrigin = args.GetPosition(this);
    }

    private void ServerMouseDown(object sender, MouseButtonEventArgs args)
    {
        pendingServerDrag = Ancestor<ListViewItem>(args.OriginalSource)?.Content as Favorite;
        dragOrigin = args.GetPosition(this);
    }

    private bool DragThreshold(MouseEventArgs args)
    {
        Vector distance = args.GetPosition(this) - dragOrigin;
        return args.LeftButton == MouseButtonState.Pressed &&
            (Math.Abs(distance.X) >= SystemParameters.MinimumHorizontalDragDistance ||
             Math.Abs(distance.Y) >= SystemParameters.MinimumVerticalDragDistance);
    }

    private void SidebarMouseMove(object sender, MouseEventArgs args)
    {
        if (pendingSidebarDrag is not { } source || !DragThreshold(args)) return;
        pendingSidebarDrag = null;
        args.Handled = true;
        Mouse.Capture(null);
        StartSidebarDrag(Sidebar, source);
    }

    private void ServerMouseMove(object sender, MouseEventArgs args)
    {
        if (pendingServerDrag is not { } source || !DragThreshold(args)) return;
        pendingServerDrag = null;
        args.Handled = true;
        StartSidebarDrag(Servers, source);
    }

    private void StartSidebarDrag(DependencyObject origin, object source)
    {
        try { DragDrop.DoDragDrop(origin, source, DragDropEffects.Move); }
        finally { ClearDropFeedback(); }
    }

    private enum DropSlot { Before, Inside, After }
    private sealed record SidebarDropTarget(Button? Button, SidebarRow? Row, DropSlot Slot);

    private SidebarDropTarget TargetOf(DragEventArgs args, SidebarRow? source)
    {
        var button = Ancestor<Button>(args.OriginalSource);
        if (button?.Tag is not SidebarRow row) return new(null, null, DropSlot.After);
        double fraction = args.GetPosition(button).Y / button.ActualHeight;
        var slot = row.IsCategory && source is { IsCategory: false } && fraction is >= .25 and <= .75
            ? DropSlot.Inside : fraction < .5 ? DropSlot.Before : DropSlot.After;
        return new(button, row, slot);
    }

    private static T? DragData<T>(DragEventArgs args) where T : class => args.Data.GetData(typeof(T)) as T;

    private static bool CanMoveSidebarRow(SidebarRow source, SidebarDropTarget target) =>
        target.Row != source && (!source.IsCategory || target.Row is not { IsCategory: false, Category: not null });

    private void SidebarDragOver(object sender, DragEventArgs args)
    {
        var source = DragData<SidebarRow>(args);
        var favorite = DragData<Favorite>(args);
        var target = TargetOf(args, source);
        bool allowed = source != null ? CanMoveSidebarRow(source, target)
            : favorite != null && target.Row is { IsCategory: false } group && CanMoveFavorite(favorite, group.Name);
        var expandable = target.Row is { IsCategory: true, Category: { Expanded: false } category } &&
            (favorite != null || source is { IsCategory: false } && target.Slot == DropSlot.Inside) ? category : null;
        SetHoverCategory(expandable);
        dropHighlight?.SetCurrentValue(BorderBrushProperty, Brushes.Transparent);
        dropHighlight = allowed || expandable != null ? target.Button : null;
        dropHighlight?.SetCurrentValue(BorderBrushProperty, Brush("#ACA1CF"));
        args.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        args.Handled = true;
    }

    private void SetHoverCategory(SidebarEntry? entry)
    {
        if (hoverCategory == entry) return;
        hoverExpandTimer?.Stop();
        hoverCategory = entry;
        if (entry == null) return;
        if (hoverExpandTimer == null)
        {
            hoverExpandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            hoverExpandTimer.Tick += (_, _) =>
            {
                if (hoverCategory is { } current) ToggleCategory(current, true);
            };
        }
        hoverExpandTimer.Start();
    }

    private void ClearDropFeedback()
    {
        SetHoverCategory(null);
        dropHighlight?.SetCurrentValue(BorderBrushProperty, Brushes.Transparent);
        dropHighlight = null;
    }

    private void SidebarDragLeave(object sender, DragEventArgs args) => ClearDropFeedback();

    private void SidebarDrop(object sender, DragEventArgs args)
    {
        var source = DragData<SidebarRow>(args);
        var favorite = DragData<Favorite>(args);
        var target = TargetOf(args, source);
        ClearDropFeedback();
        args.Effects = DragDropEffects.None;
        if (source != null && CanMoveSidebarRow(source, target))
        {
            MoveSidebarRow(source, target);
            args.Effects = DragDropEffects.Move;
        }
        else if (favorite != null && target.Row is { IsCategory: false } group && CanMoveFavorite(favorite, group.Name))
        {
            MoveFavorite(favorite, group.Name);
            args.Effects = DragDropEffects.Move;
        }
        args.Handled = true;
    }

    private void MoveSidebarRow(SidebarRow source, SidebarDropTarget target)
    {
        var row = target.Row;
        var entry = source.IsCategory ? source.Category!
            : SidebarEntries.FirstOrDefault(item => item.Groups == null && item.Name == source.Name) ?? SidebarEntry.Group(source.Name);
        if (source.IsCategory) SidebarEntries.Remove(entry);
        else RemoveGroupFromSidebar(source.Name);

        if (target.Slot == DropSlot.Inside)
        {
            row!.Category!.Groups!.Add(source.Name);
            row.Category.Expanded = true;
        }
        else if (row is { IsCategory: false, Category: not null })
        {
            var groups = row.Category.Groups!;
            groups.Insert(groups.IndexOf(row.Name) + (target.Slot == DropSlot.After ? 1 : 0), source.Name);
        }
        else if (row == null) SidebarEntries.Add(entry);
        else
        {
            int index = row.IsCategory ? SidebarEntries.IndexOf(row.Category!)
                : SidebarEntries.FindIndex(item => item.Groups == null && item.Name == row.Name);
            SidebarEntries.Insert(index + (target.Slot == DropSlot.After ? 1 : 0), entry);
        }
        // Keep the compatibility list in the same order for older readers and the Move menu.
        preferences.Games = SidebarEntries.SelectMany(item => item.Groups ?? [item.Name]).ToList();
        RenderGames();
        Save();
    }
}
