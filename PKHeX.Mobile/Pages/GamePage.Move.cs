using PKHeX.Core;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Pages;

// Move-mode logic: grab, cancel, execute, swap to bank.
public partial class GamePage
{
    // ──────────────────────────────────────────────
    //  Move mode
    // ──────────────────────────────────────────────

    private void EnterMoveMode()
    {
        if (_sav is null) return;
        if (_cursorSlot >= _currentBox.Length) return;
        if (_currentBox[_cursorSlot].Species == 0) return;
        _movePk         = _currentBox[_cursorSlot].Clone();
        _moveSourceBox  = _boxIndex == -1 ? -2 : _boxIndex; // -2 = party, -1 = bank (reserved)
        _moveSourceSlot = _cursorSlot;
        _moveMode       = true;
        BoxCanvas.InvalidateSurface();
    }

    private void CancelMoveMode()
    {
        _moveMode = false;
        _movePk   = null;
        _session.PendingSourceBox = -1; // release bank reference if cancelled
        BoxCanvas.InvalidateSurface();
    }

    private void ExecuteMove()
    {
        if (_movePk is null || _sav is null) return;

        if (_moveSourceBox == -1)
        {
            // Withdraw from bank: convert format if needed, then place in game slot or party.
            var pk = _movePk;
            if (pk.GetType() != _sav.PKMType)
            {
                var converted = EntityConverter.ConvertToType(pk, _sav.PKMType, out var convertResult);
                if (converted is null)
                {
                    _ = DisplayAlertAsync("Incompatible",
                        $"This Pokémon ({pk.GetType().Name}) can't be transferred to this game ({_sav.PKMType.Name}).",
                        "OK");
                    return;
                }
                pk = converted;
            }

            if (_boxIndex == -1)
                _sav.SetPartySlotAtIndex(pk, _cursorSlot);
            else
                _sav.SetBoxSlotAtIndex(pk, _boxIndex, _cursorSlot);

            if (_session.PendingSourceBox >= 0)
            {
                new BankService().ClearSlot(_session.PendingSourceBox, _session.PendingSourceSlot);
                _session.PendingSourceBox = -1;
            }
        }
        else
        {
            // Box-to-box (or party) swap
            var destPk = _currentBox[_cursorSlot];

            if (_boxIndex == -1)
                _sav.SetPartySlotAtIndex(_movePk, _cursorSlot);
            else
                _sav.SetBoxSlotAtIndex(_movePk, _boxIndex, _cursorSlot);

            if (_moveSourceBox == -2)
                _sav.SetPartySlotAtIndex(destPk, _moveSourceSlot);
            else
                _sav.SetBoxSlotAtIndex(destPk, _moveSourceBox, _moveSourceSlot);
        }

        CancelMoveMode();
        DeselectSlot();
        _previewSpecies = -1;
        LoadBox(_boxIndex);
    }

    private async Task SwapToBank(int dir)
    {
        if (_moveMode && _movePk != null)
        {
            _session.PendingMove     = _movePk;
            _session.PendingFromBank = _moveSourceBox == -1;
            if (!_session.PendingFromBank)
            {
                _session.PendingSourceBox  = _moveSourceBox;
                _session.PendingSourceSlot = _moveSourceSlot;
            }
            _moveMode = false;
            _movePk   = null;
        }

        _session.BankSlideDir = dir;
        await Shell.Current.GoToAsync(nameof(BankPage), false);
    }
}
