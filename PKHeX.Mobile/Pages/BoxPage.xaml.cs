using PKHeX.Core;
using PKHeX.Drawing.Mobile.Sprites;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKHeX.Mobile.Pages;

public partial class BoxPage : ContentPage
{
    private const int Columns = 6;
    private const int Rows = 5;

    private readonly ISpriteRenderer _sprites = new PlaceholderSpriteRenderer();
    private SaveFile? _sav;
    private PKM[] _currentBox = [];
    private int _boxIndex;

    public BoxPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _sav = App.ActiveSave;
        if (_sav is null)
            return;

        _boxIndex = 0;
        LoadBox(_boxIndex);
    }

    private void LoadBox(int box)
    {
        if (_sav is null)
            return;

        _currentBox = _sav.GetBoxData(box);
        BoxNameLabel.Text = _sav is IBoxDetailName named
            ? named.GetBoxName(box)
            : $"Box {box + 1}";

        BoxCanvas.InvalidateSurface();
    }

    private void OnPrevBox(object sender, EventArgs e)
    {
        if (_sav is null || _boxIndex <= 0)
            return;
        LoadBox(--_boxIndex);
    }

    private void OnNextBox(object sender, EventArgs e)
    {
        if (_sav is null || _boxIndex >= _sav.BoxCount - 1)
            return;
        LoadBox(++_boxIndex);
    }

    private void OnBoxPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (_currentBox.Length == 0)
            return;

        float slotW = (float)e.Info.Width / Columns;
        float slotH = (float)e.Info.Height / Rows;
        const float pad = 3f;

        for (int i = 0; i < _currentBox.Length; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            float x = col * slotW;
            float y = row * slotH;

            var pk = _currentBox[i];

            // Slot background
            using var bgPaint = new SKPaint
            {
                Color = new SKColor(240, 240, 240, 180),
                IsAntialias = true,
            };
            canvas.DrawRoundRect(x + pad, y + pad, slotW - pad * 2, slotH - pad * 2, 6, 6, bgPaint);

            if (pk.Species == 0)
                continue;

            // Draw sprite scaled into slot
            using var sprite = _sprites.GetSprite(pk);
            var dest = SKRect.Create(x + pad, y + pad, slotW - pad * 2, slotH - pad * 2);
            canvas.DrawBitmap(sprite, dest);
        }
    }

    private async void OnBoxTapped(object sender, TappedEventArgs e)
    {
        if (_sav is null || sender is not View view)
            return;

        var point = e.GetPosition(view);
        if (point is null)
            return;

        float slotW = (float)view.Width / Columns;
        float slotH = (float)view.Height / Rows;
        int col = (int)(point.Value.X / slotW);
        int row = (int)(point.Value.Y / slotH);
        int index = row * Columns + col;

        if ((uint)index >= (uint)_currentBox.Length)
            return;

        var pk = _currentBox[index];
        if (pk.Species == 0)
            return;

        var name = GameInfo.GetStrings("en").Species[pk.Species];
        var info = $"#{pk.Species:000} {name}\n" +
                   $"Level {pk.CurrentLevel}\n" +
                   $"OT: {pk.OriginalTrainerName}\n" +
                   (pk.IsShiny ? "★ Shiny\n" : "") +
                   (pk.IsEgg ? "🥚 Egg" : "");

        await DisplayAlert(name, info, "OK");
    }
}
