using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Operaciones sobre el <see cref="SaveFile"/> en memoria para "liberar" un Pokémon (quitarlo del
/// equipo o de una caja) y para deshacerlo. Solo toca el save; el estado de sesión (ediciones,
/// creaciones pendientes) lo reacomoda quien llama (ver PokemonReleaseService en la UI).
///
/// Reglas (confirmadas contra el código de PKHeX.Core):
///  · Equipo: el save guarda un contador + slots contiguos (nunca hay huecos). Se borra con
///    <see cref="SaveFile.DeletePartySlot"/>, que sube a los siguientes. Escribir un Pokémon vacío en un
///    slot intermedio truncaría el equipo (PartyCount = índice) y perdería a los de atrás.
///  · Cajas Gen 3+: admiten huecos; liberar deja el slot vacío y no mueve nada.
///  · Cajas Gen 1/2: el juego las guarda como lista corrida (PKHeX las vuelve a empaquetar al
///    escribir), así que se compactan: los siguientes suben un lugar, igual que en el juego.
///  · Let's Go (SAV7b) no se soporta todavía: equipo y cajas comparten un único almacenamiento con
///    punteros y un Pokémon inicial protegido, y no hay forma de comprobarlo sin un save real.
/// </summary>
public static class SlotReleaseOps
{
    private static readonly EntityImportSettings NoImport = EntityImportSettings.None;

    /// <summary>True si liberar en esta caja corre a los siguientes (listas corridas: Gen 1 y 2).</summary>
    public static bool BoxCompacts(SaveFile save) => save is SAV1 or SAV2;

    /// <summary>False si este tipo de save todavía no admite liberar Pokémon.</summary>
    public static bool IsSupported(SaveFile save) => save is not SAV7b;

    /// <summary>True si el slot de caja está protegido contra sobrescritura (inicial, bloqueado...).</summary>
    public static bool IsProtected(SaveFile save, int box, int slot)
    {
        try { return save.IsBoxSlotOverwriteProtected(box, slot); }
        catch { return false; }
    }

    /// <summary>Cantidad de Pokémon en el equipo del save.</summary>
    public static int PartyCount(SaveFile save) => Math.Clamp(save.PartyCount, 0, 6);

    /// <summary>Cantidad de Pokémon "del prefijo" de una caja compacta (hasta el último ocupado).</summary>
    public static int BoxPrefixCount(SaveFile save, int box)
    {
        int last = -1;
        for (int i = 0; i < save.BoxSlotCount; i++)
            if (save.GetBoxSlotAtIndex(box, i).Species != 0) last = i;
        return last + 1;
    }

    /// <summary>Quita el Pokémon del slot. Devuelve el Pokémon quitado (clon) o null si no había.</summary>
    public static PKM? Remove(SaveFile save, bool isParty, int box, int slot)
    {
        if (isParty)
        {
            if ((uint)slot >= (uint)PartyCount(save)) return null;
            var pk = save.GetPartySlotAtIndex(slot);
            if (pk.Species == 0) return null;
            var clone = pk.Clone();
            save.DeletePartySlot(slot);
            return clone;
        }

        var original = save.GetBoxSlotAtIndex(box, slot);
        if (original.Species == 0) return null;
        var removed = original.Clone();

        if (!BoxCompacts(save))
        {
            save.SetBoxSlotAtIndex(save.BlankPKM, box, slot, NoImport);
            return removed;
        }

        // Caja de lista corrida: los siguientes suben un lugar y el último queda vacío.
        int count = BoxPrefixCount(save, box);
        for (int i = slot; i < count - 1; i++)
            save.SetBoxSlotAtIndex(save.GetBoxSlotAtIndex(box, i + 1), box, i, NoImport);
        save.SetBoxSlotAtIndex(save.BlankPKM, box, count - 1, NoImport);
        return removed;
    }

    /// <summary>
    /// ¿Se puede volver a poner un Pokémon en el slot? Para equipo y cajas compactas hace falta lugar al
    /// final (<paramref name="extraOccupied"/> = Pokémon pendientes de crear que ocupan lugar aparte del save);
    /// para cajas con huecos, el slot tiene que seguir vacío.
    /// </summary>
    public static bool CanInsert(SaveFile save, bool isParty, int box, int slot, int extraOccupied)
    {
        if (isParty)
            return PartyCount(save) + extraOccupied < 6 && slot <= PartyCount(save);
        if (!BoxCompacts(save))
            return save.GetBoxSlotAtIndex(box, slot).Species == 0 && extraOccupied == 0;
        return BoxPrefixCount(save, box) + extraOccupied < save.BoxSlotCount && slot <= BoxPrefixCount(save, box);
    }

    /// <summary>Inversa de <see cref="Remove"/>: vuelve a poner el Pokémon en su slot, bajando a los siguientes
    /// si el contenedor es compacto. Llamar solo si <see cref="CanInsert"/> dio true.</summary>
    public static void Insert(SaveFile save, bool isParty, int box, int slot, PKM pkm)
    {
        var pk = pkm.Clone();
        if (isParty)
        {
            for (int i = PartyCount(save) - 1; i >= slot; i--)
                save.SetPartySlotAtIndex(save.GetPartySlotAtIndex(i), i + 1, NoImport);
            save.SetPartySlotAtIndex(pk, slot, NoImport);
            return;
        }

        if (BoxCompacts(save))
        {
            for (int i = BoxPrefixCount(save, box) - 1; i >= slot; i--)
                save.SetBoxSlotAtIndex(save.GetBoxSlotAtIndex(box, i), box, i + 1, NoImport);
        }
        save.SetBoxSlotAtIndex(pk, box, slot, NoImport);
    }
}
