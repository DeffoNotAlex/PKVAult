using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PKHeX.Core;

namespace PKHeX.Mobile.Models;

public class ItemRow : INotifyPropertyChanged
{
    private readonly InventoryItem _item;
    private readonly int _max;

    public string Name { get; }
    public InventoryType PouchType { get; }
    public bool IsKeyItem { get; }
    public bool IsEditable => !IsKeyItem;
    public int ItemIndex => _item.Index;

    private int _count;
    public int Count
    {
        get => _count;
        set
        {
            int clamped = Math.Clamp(value, 0, _max);
            if (clamped == _count) return;
            _count    = clamped;
            _item.Count = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountText));
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

    public ItemRow(string name, InventoryItem item, InventoryType pouchType, int max, bool isKeyItem)
    {
        Name      = name;
        PouchType = pouchType;
        IsKeyItem = isKeyItem;
        _item     = item;
        _max      = Math.Max(max, 1);
        _count    = item.Count;

        IncrementCommand = new Command(() => Count++);
        DecrementCommand = new Command(() => Count--);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
