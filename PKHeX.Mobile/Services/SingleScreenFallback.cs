using PKHeX.Core;

namespace PKHeX.Mobile.Services;

/// <summary>
/// No-op implementation for single-screen devices.
/// GamePage owns its top row directly on single-screen hardware.
/// </summary>
public sealed class SingleScreenFallback : ISecondaryDisplay
{
    public bool IsAvailable => false;
    public void Show() { }
    public void Hide() { }
    public void UpdateTrainer(SaveFile sav, string boxName, int filled, int total) { }
    public void UpdateBoxInfo(string boxName, int filled, int total) { }
    public void UpdatePokemon(PKM pk) { }
    public void ClearPokemon() { }
}
