using PKHeX.Core;

namespace Exxeguttor.UI.ViewModels;

/// <summary>
/// Identifica de forma estable un slot de Pokémon (caja o equipo) para poder cachear ediciones
/// de sesión en <see cref="PokemonEditorViewModel"/> — el PKM que llega desde
/// <see cref="Exxeguttor.App.Services.PokemonService"/> es una instancia nueva en cada
/// selección (se re-lee del SaveFile), así que no sirve como clave por sí solo.
///
/// También se reusa como clave "sentinel" para el entrenador (<see cref="ForTrainer"/>) y para
/// cada pouch de la mochila (<see cref="ForBag"/>) — mismo mecanismo de RecordEdit/CapturePristine
/// de <see cref="Exxeguttor.UI.Services.EditSessionService"/> en los tres casos, sin duplicar
/// máquina de estados.
/// </summary>
public readonly record struct PokemonSlotKey(bool IsParty, int BoxIndex, int SlotIndex)
{
    public static PokemonSlotKey ForBox(int boxIndex, int slotIndex) => new(false, boxIndex, slotIndex);
    public static PokemonSlotKey ForParty(int slotIndex) => new(true, 0, slotIndex);

    /// <summary>Clave "sentinel" para trackear ediciones del entrenador con el mismo
    /// mecanismo de RecordEdit/CapturePristine que ya usan los Pokémon — (false, -1, -1)
    /// nunca colisiona con un slot real (ForBox siempre usa índices >= 0).</summary>
    public static PokemonSlotKey ForTrainer() => new(false, -1, -1);

    /// <summary>Clave "sentinel" por pouch de mochila — (true, -2 ó -3, (int)type) nunca
    /// colisiona con ForParty (BoxIndex siempre 0 ahí) ni con ForTrainer (IsParty false).
    /// BoxIndex -2 = origen Mochila, -3 = origen PC — SÍ hace falta esta dimensión aparte
    /// (a diferencia de lo que se pensaba antes): desde la corrección de sesión
    /// "categorización de Mochila" ambos orígenes pueden tener una tile del mismo
    /// InventoryType (ej. "MTs/MOs" en Mochila Y en PC simultáneamente, cada uno con su propio
    /// pouch físico real) — sin este discriminador, editar cantidad en una tile de PC pisaría
    /// silenciosamente la edición pendiente de la tile homónima de Mochila, y
    /// EditApplyService.ApplyBagPouch no tendría forma de saber a qué pouch físico escribir.
    /// El nombre de propiedad usado en RecordEdit para cada ítem dentro de este pouch es el
    /// ItemId (de pokemon.db, que coincide con el índice de PKHeX.Core — ver
    /// PokemonEditorViewModel.LoadItemLists) convertido a string.</summary>
    public static PokemonSlotKey ForBag(InventoryType type, bool alternateStorage = false) =>
        new(true, alternateStorage ? -3 : -2, (int)type);

    public bool IsBag => IsParty && (BoxIndex == -2 || BoxIndex == -3);
    public bool IsBagAlternateStorage => BoxIndex == -3;
    public InventoryType BagPouchType => (InventoryType)SlotIndex;
}
