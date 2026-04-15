using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PKHeX.Core;

namespace PKHeX.Mobile.Models;

public class ItemRow : INotifyPropertyChanged
{
    private InventoryItem? _item;            // null for unslotted free-slot items until first write
    private readonly InventoryPouch? _pouch; // non-null only for free-slot lazy-assignment items
    private readonly ushort _itemId;
    private readonly int _max;

    public string Name { get; }
    public InventoryType PouchType { get; }
    public bool IsKeyItem { get; }
    public bool IsEditable => !IsKeyItem;
    public int ItemIndex => _itemId;
    public bool IsOwned => _count > 0;

    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            int clamped = Math.Clamp(value, 0, _max);
            if (clamped == _count) return;
            bool wasOwned = _count > 0;
            _count = clamped;
            // Lazy slot assignment for free-slot pouches: find an empty slot on first positive count
            if (_item == null && clamped > 0 && _pouch != null)
            {
                var freeSlot = Array.Find(_pouch.Items, it => it.Index == 0);
                if (freeSlot != null)
                {
                    freeSlot.Index = _itemId;
                    _item = freeSlot;
                }
            }
            if (_item != null) _item.Count = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountText));
            if (wasOwned != (_count > 0))
                OnPropertyChanged(nameof(IsOwned));
        }
    }
    public string CountText => $"×{_count}";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public ICommand IncrementCommand { get; }
    public ICommand DecrementCommand { get; }

    // For items with a pre-existing slot (Gen 9 fixed-slot, or free-slot with an owned item)
    public ItemRow(string name, InventoryItem item, InventoryType pouchType, int max, bool isKeyItem)
    {
        Name      = name;
        PouchType = pouchType;
        IsKeyItem = isKeyItem;
        _item     = item;
        _itemId   = (ushort)item.Index;
        _max      = Math.Max(max, 1);
        _count    = item.Count;

        IncrementCommand = new Command(() => Count++);
        DecrementCommand = new Command(() => Count--);
    }

    // For unslotted items in free-slot pouches (Gen 1-5) — slot is assigned lazily on first increment
    public ItemRow(string name, ushort itemId, InventoryPouch pouch, InventoryType pouchType, int max, bool isKeyItem)
    {
        Name      = name;
        PouchType = pouchType;
        IsKeyItem = isKeyItem;
        _item     = null;
        _pouch    = pouch;
        _itemId   = itemId;
        _max      = Math.Max(max, 1);
        _count    = 0;

        IncrementCommand = new Command(() => Count++);
        DecrementCommand = new Command(() => Count--);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
