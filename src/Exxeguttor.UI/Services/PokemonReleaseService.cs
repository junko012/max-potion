using Exxeguttor.App.Services;
using Exxeguttor.UI.ViewModels;
using PKHeX.Core;

namespace Exxeguttor.UI.Services;

/// <summary>
/// "Liberar" Pokémon (quitarlos de una caja o del equipo) con deshacer. El save en memoria se modifica
/// al liberar (ver <see cref="SlotReleaseOps"/>), así lo que ve la UI es exactamente lo que se exportará;
/// cada liberación queda registrada en <see cref="EditSessionService"/> para poder deshacerla, y
/// "Descartar ediciones" las deshace todas (el save en memoria ya no es el original sin tocar).
///
/// Las claves de sesión (ediciones, creaciones pendientes) son por posición; al compactar (equipo, cajas
/// Gen 1/2) se corren junto con los Pokémon para que cada edición siga apuntando al mismo Pokémon.
/// </summary>
public sealed class PokemonReleaseService
{
    private readonly SaveFileService _saveService;
    private readonly EditSessionService _session;

    public PokemonReleaseService(SaveFileService saveService, EditSessionService session)
    {
        _saveService = saveService;
        _session = session;
    }

    public enum Block { None, Unsupported, Protected, LastPartyMember, Empty }

    /// <summary>Pokémon del slot: la creación pendiente si la hay, si no el del save (null si está vacío).</summary>
    public PKM? Get(PokemonSlotKey key)
    {
        if (_session.GetPendingCreation(key) is { } created) return created;
        if (_saveService.CurrentSave is not { } save) return null;
        try
        {
            PKM? pk = null;
            if (key.IsParty)
            {
                if (key.SlotIndex >= 0 && key.SlotIndex < SlotReleaseOps.PartyCount(save))
                    pk = save.GetPartySlotAtIndex(key.SlotIndex);
            }
            else if (key.BoxIndex >= 0 && key.BoxIndex < save.BoxCount && key.SlotIndex >= 0 && key.SlotIndex < save.BoxSlotCount)
            {
                pk = save.GetBoxSlotAtIndex(key.BoxIndex, key.SlotIndex);
            }
            return pk != null && pk.Species != 0 ? pk : null;
        }
        catch { return null; }
    }

    /// <summary>Pokémon que ocupan el equipo ahora: los del save más las creaciones pendientes.</summary>
    private int PartyOccupied(SaveFile save)
    {
        int n = 0;
        for (int i = 0; i < 6; i++)
            if (i < SlotReleaseOps.PartyCount(save) || _session.HasPendingCreation(PokemonSlotKey.ForParty(i))) n++;
        return n;
    }

    public Block GetBlock(PokemonSlotKey key)
    {
        if (_saveService.CurrentSave is not { } save) return Block.Unsupported;
        if (!SlotReleaseOps.IsSupported(save)) return Block.Unsupported;
        if (Get(key) == null) return Block.Empty;
        if (key.IsParty && PartyOccupied(save) <= 1) return Block.LastPartyMember;
        if (!key.IsParty && !_session.HasPendingCreation(key) && SlotReleaseOps.IsProtected(save, key.BoxIndex, key.SlotIndex))
            return Block.Protected;
        return Block.None;
    }

    /// <summary>True si el equipo tiene un solo Pokémon (el "−" se ve deshabilitado en él).</summary>
    public bool IsLastPartyMember(PokemonSlotKey key) => GetBlock(key) == Block.LastPartyMember;

    /// <summary>Libera el Pokémon del slot. Devuelve el registro, o null si no se pudo.</summary>
    public ReleaseRecord? Release(PokemonSlotKey key)
    {
        if (GetBlock(key) != Block.None) return null;
        var save = _saveService.CurrentSave!;
        var pkm = Get(key)!;
        var created = _session.HasPendingCreation(key);
        var compacts = key.IsParty || SlotReleaseOps.BoxCompacts(save);
        var name = DisplayName(pkm);
        var where = Where(key);

        PKM? original = null;
        if (!created)
        {
            // Primero el save: si falla, no se tocó nada de la sesión.
            original = SlotReleaseOps.Remove(save, key.IsParty, key.BoxIndex, key.SlotIndex);
            if (original == null) return null;
        }

        var state = _session.TakeSlotState(key);
        if (compacts)
            _session.ShiftSlotKeys(key.IsParty, key.BoxIndex, key.SlotIndex + 1, -1);

        var record = new ReleaseRecord(key, original, state, compacts, name, where);
        _session.PushRelease(record);
        return record;
    }

    /// <summary>Último Pokémon liberado (para el aviso "Deshacer"), o null.</summary>
    public ReleaseRecord? LastRelease => _session.PeekRelease();

    /// <summary>Deshace la última liberación. Devuelve false (y la clave de texto del motivo) si no hay lugar.</summary>
    public bool UndoLast(out string? failKey)
    {
        failKey = null;
        if (_session.PeekRelease() is not { } r) { failKey = "Release_UndoNothing"; return false; }
        if (_saveService.CurrentSave is not { } save) { failKey = "Release_UndoNothing"; return false; }
        var key = r.Key;

        if (r.Compacted)
        {
            int extra = CountCreations(save, key);
            if (r.Original != null
                ? !SlotReleaseOps.CanInsert(save, key.IsParty, key.BoxIndex, key.SlotIndex, extra)
                : !HasRoomForCreation(save, key))
            { failKey = "Release_UndoNoRoom"; return false; }
        }
        else if (_session.HasPendingCreation(key)
                 || (r.Original != null && !SlotReleaseOps.CanInsert(save, false, key.BoxIndex, key.SlotIndex, 0))
                 || (r.Original == null && Get(key) != null))
        {
            failKey = "Release_UndoSlotTaken"; return false;
        }

        if (r.Compacted)
            _session.ShiftSlotKeys(key.IsParty, key.BoxIndex, key.SlotIndex, +1);
        if (r.Original != null)
            SlotReleaseOps.Insert(save, key.IsParty, key.BoxIndex, key.SlotIndex, r.Original);
        _session.PutSlotState(key, r.State);
        _session.PopRelease();
        return true;
    }

    /// <summary>
    /// ¿Se puede destildar esta liberación en el modal de exportación? Solo cuando reponer el Pokémon es
    /// puro (caja con huecos, slot todavía vacío, sin creación puesta ahí después, sin ediciones pendientes
    /// del Pokémon que se perderían). En equipo y cajas Gen 1/2 liberar corre a los demás: no se puede excluir.
    /// </summary>
    public bool CanExclude(ReleaseRecord r)
    {
        if (r.Original == null || r.Compacted || r.Key.IsParty) return false;
        if (r.State.Edits is { Count: > 0 }) return false;
        if (_saveService.CurrentSave is not { } save) return false;
        var key = r.Key;
        if (_session.HasPendingCreation(key)) return false;
        return SlotReleaseOps.CanInsert(save, false, key.BoxIndex, key.SlotIndex, 0);
    }

    /// <summary>Repone los Pokémon cuyas liberaciones se destildaron en el modal (antes de aplicar el resto).</summary>
    public void ApplyExclusions()
    {
        if (_saveService.CurrentSave is not { } save) return;
        foreach (var r in _session.Releases.Where(x => !x.Include).ToList())
        {
            if (!CanExclude(r)) continue;
            SlotReleaseOps.Insert(save, false, r.Key.BoxIndex, r.Key.SlotIndex, r.Original!);
            _session.PutSlotState(r.Key, r.State);
            _session.RemoveRelease(r);
        }
    }

    /// <summary>Deshace todas las liberaciones (Descartar ediciones). Devuelve cuántas se deshicieron.</summary>
    public int UndoAll()
    {
        int n = 0;
        while (_session.PeekRelease() != null)
        {
            if (!UndoForced()) break;
            n++;
        }
        return n;
    }

    // Al descartar todo, antes se deshicieron las creaciones/ediciones posteriores (ResetSession las borra
    // después), así que acá no puede faltar lugar: se ignoran las creaciones pendientes en el chequeo.
    private bool UndoForced()
    {
        if (_session.PeekRelease() is not { } r || _saveService.CurrentSave is not { } save) return false;
        var key = r.Key;
        if (r.Original != null)
            SlotReleaseOps.Insert(save, key.IsParty, key.BoxIndex, key.SlotIndex, r.Original);
        _session.PopRelease();
        return true;
    }

    /// <summary>Creaciones pendientes del mismo contenedor (equipo o caja) que ocupan lugar aparte del save.</summary>
    private int CountCreations(SaveFile save, PokemonSlotKey key)
    {
        int n = 0;
        int capacity = key.IsParty ? 6 : save.BoxSlotCount;
        for (int i = 0; i < capacity; i++)
        {
            var k = key.IsParty ? PokemonSlotKey.ForParty(i) : PokemonSlotKey.ForBox(key.BoxIndex, i);
            if (_session.HasPendingCreation(k)) n++;
        }
        return n;
    }

    private bool HasRoomForCreation(SaveFile save, PokemonSlotKey key)
    {
        int occupied = key.IsParty ? PartyOccupied(save)
            : SlotReleaseOps.BoxPrefixCount(save, key.BoxIndex) + CountCreations(save, key);
        int capacity = key.IsParty ? 6 : save.BoxSlotCount;
        return occupied < capacity;
    }

    // ── Textos ───────────────────────────────────────────────────────────────────────────────

    public static string DisplayName(PKM pkm)
    {
        if (pkm.IsEgg) return UiText.T("Release_EggName");
        if (pkm.IsNicknamed && !string.IsNullOrWhiteSpace(pkm.Nickname)) return pkm.Nickname;
        var strings = DataLanguageService.Instance.Strings;
        var species = pkm.Species < strings.Species.Count ? strings.Species[pkm.Species] : $"#{pkm.Species}";
        return FormNameFormatter.Format(species, pkm.Species, pkm.Form);
    }

    public static string Where(PokemonSlotKey key) => key.IsParty
        ? UiText.F("Release_WhereParty", key.SlotIndex + 1)
        : UiText.F("Release_WhereBox", key.BoxIndex + 1, key.SlotIndex + 1);

    /// <summary>Nombre del objeto equipado en el idioma de los datos, o null si no lleva.</summary>
    public static string? HeldItemName(PKM pkm)
    {
        if (pkm.HeldItem <= 0) return null;
        var items = DataLanguageService.Instance.Strings.Item;
        return pkm.HeldItem < items.Count ? items[pkm.HeldItem] : null;
    }
}
