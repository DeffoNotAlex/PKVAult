using PKHeX.Core;

namespace PKHeX.Mobile.Services;

/// <summary>
/// Abstraction for the AYN Thor's second AMOLED display (exposed as an external Android display).
/// On single-screen devices all methods are no-ops — GamePage owns its top row directly.
/// </summary>
public interface ISecondaryDisplay
{
    bool IsAvailable { get; }
    void Show();
    void Hide();
    void UpdateTrainer(SaveFile sav, string boxName, int filled, int total);
    void UpdateBoxInfo(string boxName, int filled, int total);
    void UpdatePokemon(PKM pk);
    void ClearPokemon();
}
