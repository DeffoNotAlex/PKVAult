using Microsoft.Maui.Controls.Shapes;
using PKHeX.Core;
using PKHeX.Mobile.Models;
using static PKHeX.Mobile.Services.ThemeService;

namespace PKHeX.Mobile.Pages;

// Items tab logic: bag display, pocket navigation, item editing.
public partial class GamePage
{
    // ──────────────────────────────────────────────
    //  Items tab
    // ──────────────────────────────────────────────

    private void OnBoxesTabTapped(object? sender, TappedEventArgs e) => SwitchToBoxesTab();
    private void OnItemsTabTapped(object? sender, TappedEventArgs e) => SwitchToItemsTab();
    private void OnItemsMenuClicked(object? sender, EventArgs e)     => SwitchToItemsTab();
    private void OnItemsCloseTapped(object? sender, TappedEventArgs e) => SwitchToBoxesTab();

    private void SwitchToItemsTab()
    {
        if (_sav is null) return;
        _itemsTabActive      = true;
        ItemsPanel.IsVisible = true;
        UpdateTabHighlight();
        BuildPocketTabs();
    }

    private void SwitchToBoxesTab()
    {
        CommitItems();
        _itemsTabActive      = false;
        _itemEditMode        = false;
        ItemsPanel.IsVisible = false;
        UpdateTabHighlight();
    }

    private void CommitItems()
    {
        if (_bag is null || _sav is null) return;
        try { _bag.CopyTo(_sav); } catch { }
    }

    private void UpdateTabHighlight()
    {
        bool light = Current == PkTheme.Light;
        var activeBg  = Color.FromArgb(light ? "#DCE8FF" : "#182242");
        var inactiveBg = Color.FromArgb(light ? "#F4F6FB" : "#0C1120");
        var activeStroke   = Color.FromArgb("#3B8BFF");
        var inactiveStroke = Color.FromArgb(light ? "#E0E4EC" : "#1AFFFFFF");
        var activeText   = Color.FromArgb(light ? "#1A3A8F" : "#90B8FF");
        var inactiveText = Color.FromArgb(light ? "#555E78" : "#778BAA");

        BoxesTab.BackgroundColor = _itemsTabActive ? inactiveBg : activeBg;
        BoxesTab.Stroke          = _itemsTabActive ? inactiveStroke : activeStroke;
        if (BoxesTab.Content is Label bLabel)
            bLabel.TextColor = _itemsTabActive ? inactiveText : activeText;

        ItemsTab.BackgroundColor = _itemsTabActive ? activeBg : inactiveBg;
        ItemsTab.Stroke          = _itemsTabActive ? activeStroke : inactiveStroke;
        if (ItemsTab.Content is Label iLabel)
            iLabel.TextColor = _itemsTabActive ? activeText : inactiveText;
    }

    private void BuildPocketTabs()
    {
        PocketTabBar.Children.Clear();
        _pocketTabBorders.Clear();
        _itemRows = [];
        _itemCursor = -1;

        _bag = _sav?.Inventory;
        if (_bag is null || _bag.Pouches.Count == 0) return;

        for (int i = 0; i < _bag.Pouches.Count; i++)
        {
            int captured = i;
            var btn = new Border
            {
                StrokeShape     = new RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 1,
                Padding         = new Thickness(12, 6),
                Margin          = new Thickness(0),
            };
            btn.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => LoadPouch(captured))
            });
            btn.Content = new Label
            {
                Text       = PouchDisplayName(_bag.Pouches[captured].Type),
                FontFamily = "NunitoBold",
                FontSize   = 11,
                VerticalOptions = LayoutOptions.Center,
            };
            PocketTabBar.Children.Add(btn);
            _pocketTabBorders.Add(btn);
        }

        LoadPouch(0);
    }

    private static string PouchDisplayName(InventoryType t) => t switch
    {
        InventoryType.Balls       => "Balls",
        InventoryType.Medicine    => "Medicine",
        InventoryType.TMHMs       => "TM/HM",
        InventoryType.Berries     => "Berries",
        InventoryType.KeyItems    => "Key Items",
        InventoryType.BattleItems => "Battle",
        InventoryType.Items       => "Items",
        InventoryType.ZCrystals   => "Z-Crystals",
        InventoryType.Candy       => "Candy",
        InventoryType.Treasure    => "Treasure",
        InventoryType.Ingredients => "Ingredients",
        InventoryType.MegaStones  => "Mega Stones",
        InventoryType.PCItems     => "PC",
        _                          => t.ToString(),
    };

    private void LoadPouch(int pouchIndex)
    {
        if (_bag is null || pouchIndex < 0 || pouchIndex >= _bag.Pouches.Count) return;

        // Deselect previous cursor
        if (_itemCursor >= 0 && _itemCursor < _itemRows.Count)
            _itemRows[_itemCursor].IsSelected = false;

        _activePouchIndex = pouchIndex;
        _itemCursor       = -1;
        _itemEditMode     = false;

        var pouch  = _bag.Pouches[pouchIndex];
        bool isKey = pouch.Type == InventoryType.KeyItems;
        var names  = _strings.itemlist;

        // Always build from the full legal item list so unowned items are visible.
        // - Gen 9 (fixed-slot): every legal ID already has a pre-populated slot in Items[];
        //   all will be found in existingByIndex with Count=0 if unowned.
        // - Gen 6-8 (free-slot, PouchDataSize > legal count): owned items occupy slots,
        //   unowned items get a lazy-assigned free slot on first increment.
        // - Gen 1-5 (cramped, PouchDataSize < legal count): same lazy-assign path.
        var validIds = _bag.Info.GetItems(pouch.Type).ToArray();
        var existingByIndex = pouch.Items
            .Where(it => it.Index > 0)
            .ToDictionary(it => it.Index, it => it);

        _allItemRows = validIds
            .Where(id => id > 0)
            .Select(id =>
            {
                string nm  = id < names.Length ? names[id] : $"#{id}";
                int    max = _bag.GetMaxCount(pouch.Type, id);
                return existingByIndex.TryGetValue(id, out var slot)
                    ? new ItemRow(nm, slot, pouch.Type, max, isKey)
                    : new ItemRow(nm, id, pouch, pouch.Type, max, isKey);
            })
            .ToList();

        // Show free-slot count for Gen 1-8 formats where slots are a limited resource.
        // Gen 9 has no concept of free slots (all items pre-allocated), so hide the indicator.
        int totalSlots = pouch.Items.Length;
        int usedSlots  = pouch.Items.Count(it => it.Index > 0);
        int freeSlots  = totalSlots - usedSlots;
        bool showSlots = totalSlots != validIds.Length; // hidden for Gen 9 exact-match pouches
        ItemsSlotInfoLabel.Text      = showSlots ? $"{freeSlots} free slot{(freeSlots == 1 ? "" : "s")}" : "";
        ItemsSlotInfoLabel.IsVisible = showSlots;

        _itemSearchText = "";
        ItemSearchEntry.Text = "";
        ApplyItemFilter();
        UpdatePocketTabHighlight();
    }

    private void ApplyItemFilter()
    {
        string q = _itemSearchText.Trim();
        _itemRows = string.IsNullOrEmpty(q)
            ? _allItemRows
            : _allItemRows
                .Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        ItemsList.ItemsSource = null;
        ItemsList.ItemsSource = _itemRows;

        // Reset cursor if it's now out of range
        if (_itemCursor >= _itemRows.Count)
        {
            _itemCursor = -1;
            _itemEditMode = false;
        }
    }

    private void OnItemSearchChanged(object sender, TextChangedEventArgs e)
    {
        _itemSearchText = e.NewTextValue ?? "";
        ApplyItemFilter();
    }

    private void UpdatePocketTabHighlight()
    {
        bool light = Current == PkTheme.Light;
        var focusBg     = Color.FromArgb(light ? "#DCE8FF" : "#182242");
        var normalBg    = Color.FromArgb(light ? "#F4F6FB" : "#0C1120");
        var focusStroke  = Color.FromArgb("#3B8BFF");
        var normalStroke = Color.FromArgb(light ? "#E0E4EC" : "#1AFFFFFF");
        var focusText    = Color.FromArgb(light ? "#1A3A8F" : "#90B8FF");
        var normalText   = Color.FromArgb(light ? "#333D55" : "#778BAA");

        for (int i = 0; i < _pocketTabBorders.Count; i++)
        {
            bool active = i == _activePouchIndex;
            _pocketTabBorders[i].BackgroundColor = active ? focusBg : normalBg;
            _pocketTabBorders[i].Stroke          = active ? focusStroke : normalStroke;
            if (_pocketTabBorders[i].Content is Label lbl)
                lbl.TextColor = active ? focusText : normalText;
        }
    }

    private void HandleItemsKey(Android.Views.Keycode keyCode)
    {
        if (_itemEditMode)
        {
            var row = _itemCursor >= 0 && _itemCursor < _itemRows.Count
                ? _itemRows[_itemCursor] : null;
            switch (keyCode)
            {
                case Android.Views.Keycode.DpadLeft:  if (row != null) row.Count--;    break;
                case Android.Views.Keycode.DpadRight: if (row != null) row.Count++;    break;
                case Android.Views.Keycode.ButtonL1:  if (row != null) row.Count -= 10; break;
                case Android.Views.Keycode.ButtonR1:  if (row != null) row.Count += 10; break;
                case Android.Views.Keycode.ButtonA:
                case Android.Views.Keycode.ButtonB:
                    _itemEditMode = false;
                    break;
            }
            return;
        }

        switch (keyCode)
        {
            case Android.Views.Keycode.DpadUp:   MoveItemCursor(-1); break;
            case Android.Views.Keycode.DpadDown: MoveItemCursor(+1); break;
            case Android.Views.Keycode.ButtonL1: CyclePocket(-1);     break;
            case Android.Views.Keycode.ButtonR1: CyclePocket(+1);     break;
            case Android.Views.Keycode.ButtonA:
                if (_itemCursor >= 0 && _itemCursor < _itemRows.Count
                    && !_itemRows[_itemCursor].IsKeyItem)
                    _itemEditMode = true;
                break;
            case Android.Views.Keycode.ButtonB:
                SwitchToBoxesTab();
                break;
        }
    }

    private void MoveItemCursor(int delta)
    {
        if (_itemRows.Count == 0) return;
        if (_itemCursor >= 0 && _itemCursor < _itemRows.Count)
            _itemRows[_itemCursor].IsSelected = false;
        _itemCursor = Math.Clamp(
            _itemCursor < 0 ? (delta > 0 ? 0 : _itemRows.Count - 1) : _itemCursor + delta,
            0, _itemRows.Count - 1);
        _itemRows[_itemCursor].IsSelected = true;
        ItemsList.ScrollTo(_itemRows[_itemCursor], group: null, position: ScrollToPosition.MakeVisible, animate: false);
        Haptic();
    }

    private void CyclePocket(int delta)
    {
        if (_bag is null) return;
        int next = Math.Clamp(_activePouchIndex + delta, 0, _bag.Pouches.Count - 1);
        if (next != _activePouchIndex)
            LoadPouch(next);
    }
}
