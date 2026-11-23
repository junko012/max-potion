using System.Text.RegularExpressions;
using PKHeX.Core;

namespace Exxeguttor.App.Services;

/// <summary>
/// Lectura de la mochila del save actual (módulo "Mochila"). <c>pokemon.db.ItemGameCodes</c> es
/// la fuente de qué categorías existen y qué ítems caen en cada una, para TODA generación por
/// igual — <c>SaveFile.Inventory</c> de PKHeX.Core solo se consulta para leer/ubicar la CANTIDAD
/// poseída real de cada ítem.
///
/// ⚠️ Historial de diseño (sesión "categorización de Mochila", cuatro correcciones seguidas —
/// dejo el resumen porque la causa real tardó en aparecer):
///   1. Primera versión usaba <c>InventoryPouch.CanContain</c> de PKHeX.Core para decidir tanto
///      qué categorías mostrar como qué ítems caen en cada una — poco confiable en Gen1/2/4/5/6.
///   2. Se sacó CanContain, todo pasó a salir de <c>pokemon.db.ItemGameCodes</c> — pero
///      matcheando por <c>ItemId</c> (el ID unificado) contra <c>pouch.Items[].Index</c>.
///      Funcionó para la mayoría de "Objetos" (coincidencia: muchos ítems comunes con ID bajo
///      tienen <c>ItemId == RawItemId</c>), pero falló en silencio para el resto — visible sobre
///      todo en MTs/MOs, donde SIEMPRE difieren.
///   3. Se armó un camino especial solo para MTs/MOs, resolviendo índices y nombres en vivo
///      contra PKHeX.Core (<c>InventoryPouch.GetAllItems()</c> +
///      <c>GameStrings.GetItemStrings(save.Context, save.Version)</c>) — funcionaba, pero
///      quedaba coja la misma clase de bug para el resto de categorías (Piedras evolutivas,
///      Llave, Cebo Bueno, etc. en Gen1/2 — nunca reportado porque no se probó con esos ítems
///      puntuales, pero el mismatch de índice era exactamente el mismo).
///   4. CAUSA REAL encontrada: <c>ItemGameCodes</c> YA tenía la columna <c>RawItemId</c> con el
///      índice correcto por juego desde el hotfix `fix_database` (confirmado contra un save real
///      de Yellow: <c>RawItemId</c> de TM01/HM01 cae exactamente en el rango que PKHeX.Core
///      reporta de verdad) — nunca se había usado esa columna, se usaba <c>ItemId</c> en su
///      lugar. El camino especial de MTs/MOs (punto 3) se sacó por completo: con
///      <see cref="PokemonDatabase.GetItemPouchRows"/> devolviendo <c>RawItemId</c>, un solo
///      camino uniforme alcanza para TODAS las categorías y generaciones — ver
///      <see cref="PokemonDatabase.ItemPouchRow"/> para el detalle de qué es cada columna.
///
/// ⚠️ "Origen" (Mochila vs PC — <paramref name="useAlternateStorage"/> en ambos métodos
/// públicos) es un eje ortogonal a la categoría, equivalente a cómo BoxViewModel navega Cajas:
///   - Mochila: cada categoría busca SU pouch real por <c>Type</c> en <c>save.Inventory</c>,
///     con fallback al pouch genérico <c>Items</c> si esta generación no tiene uno dedicado
///     (ver EditApplyService.ApplyBagPouch, mismo fallback en escritura).
///   - PC: un ÚNICO pouch físico real (<c>PCItems</c>/<c>FreeSpace</c>) sirve a TODAS las
///     categorías — mismas tiles, pero leyendo/escribiendo cantidades del pouch de PC en vez
///     del de Mochila.
///
/// ⚠️ Solo lectura. Las ediciones de cantidad se trackean como pendientes en
/// EditSessionService (Exxeguttor.UI) — nada de esto escribe todavía sobre el SaveFile real
/// (ver EditApplyService.ApplyBagPouch para el camino de escritura — recibe el mismo
/// <c>RawItemId</c> expuesto acá como <c>BagItemInfo.ItemId</c>, sin traducir nada más).
/// </summary>
public class ItemInventoryService
{
    private readonly SaveFileService _saveService;

    public ItemInventoryService(SaveFileService saveService) => _saveService = saveService;

    /// <summary>Categorías del origen pedido, con su conteo de poseídos/legales. Ordenadas por
    /// Type (orden del enum, estable). Si <paramref name="useAlternateStorage"/> es true y esta
    /// generación no tiene ningún pouch de PC real (Gen4+), devuelve vacío (señal para
    /// <c>BagViewModel.ShowStoreNav</c>).</summary>
    public List<BagPouchInfo> GetPouches(bool useAlternateStorage = false)
    {
        var save = _saveService.CurrentSave;
        var result = new List<BagPouchInfo>();
        if (save == null) return result;

        InventoryPouch? alternatePouch = null;
        if (useAlternateStorage)
        {
            alternatePouch = save.Inventory.FirstOrDefault(p => IsAlternateStorage(p.Type));
            if (alternatePouch == null) return result; // esta generación no tiene almacén aparte
        }

        var gameId = PokemonService.MapVersionToGameId(save.Version);
        var rows = PokemonDatabase.Instance.GetItemPouchRows(gameId);
        var fallbackPouch = save.Inventory.FirstOrDefault(p => p.Type == InventoryType.Items);

        foreach (var group in rows.Where(r => r.UserPouchCategory != null).GroupBy(r => r.UserPouchCategory))
        {
            if (!Enum.TryParse<InventoryType>(group.Key, out var type)) continue;

            var realPouch = useAlternateStorage
                ? alternatePouch
                : save.Inventory.FirstOrDefault(p => p.Type == type) ?? fallbackPouch;
            if (realPouch == null) continue; // defensivo — no debería pasar en ninguno de los dos casos

            // RawItemId (no ItemId) — ver comentario de clase, punto 4.
            var rawIdsInTile = group.Select(r => r.RawItemId).ToHashSet();
            var owned = realPouch.Items.Count(i => i.Count > 0 && rawIdsInTile.Contains(i.Index));
            result.Add(new BagPouchInfo(type, realPouch.MaxCount, owned, rawIdsInTile.Count));
        }
        return result.OrderBy(p => (int)p.Type).ToList();
    }

    /// <summary>Todos los ítems de una categoría puntual del origen pedido (poseídos y no
    /// poseídos), para que la UI pueda ofrecer "agregar" un ítem nuevo, no solo editar cantidad
    /// de los que ya están. Quantity siempre refleja el valor real del save — este servicio no
    /// conoce EditSessionService; el ViewModel llamante (BagViewModel) es quien superpone
    /// ediciones pendientes de la sesión sobre este valor real.</summary>
    public List<BagItemInfo> GetItems(InventoryType type, bool useAlternateStorage = false)
    {
        var save = _saveService.CurrentSave;
        var result = new List<BagItemInfo>();
        if (save == null) return result;

        var realPouch = useAlternateStorage
            ? save.Inventory.FirstOrDefault(p => IsAlternateStorage(p.Type))
            : save.Inventory.FirstOrDefault(p => p.Type == type)
                ?? save.Inventory.FirstOrDefault(p => p.Type == InventoryType.Items);
        if (realPouch == null) return result;

        var gameId = PokemonService.MapVersionToGameId(save.Version);
        var typeName = type.ToString();
        var ownedByRawIndex = realPouch.Items
            .Where(i => i.Count > 0)
            .ToDictionary(i => i.Index, i => i.Count);

        // HiddenItems (p. ej. MT sin movimiento): no se ofrecen para agregar, pero si el save ya
        // trae uno se sigue mostrando (así lo que hay en el save nunca queda invisible).
        var hidden = PokemonDatabase.Instance.GetHiddenItemIds(gameId);
        var rowsInTile = PokemonDatabase.Instance.GetItemPouchRows(gameId)
            .Where(r => r.UserPouchCategory == typeName)
            .Where(r => !hidden.Contains(r.ItemId) || ownedByRawIndex.ContainsKey(r.RawItemId))
            .ToList();
        if (rowsInTile.Count == 0) return result;

        // Batch en vez de un GetItem(row.ItemId) por fila dentro del foreach — ver el
        // comentario largo en PokemonDatabase.GetItems(IEnumerable<int>) para el bug de
        // rendimiento que esto corrige (N+1 queries SQLite, se sentía como que la app se
        // congelaba cada vez que se entraba a Mochila).
        var dbItems = PokemonDatabase.Instance.GetItems(rowsInTile.Select(r => r.ItemId));

        var generation = save.Generation;

        // Nombre en el idioma de los datos: lista de objetos de PKHeX PARA ESTE JUEGO
        // (GetItemStrings(contexto, versión) — la misma que usa el editor de mochila de PKHeX, indexada por
        // el RawItemId = pouch.Items[].Index). pokemon.db solo queda como llave del sprite y de respaldo.
        IReadOnlyList<string>? localizedItems = null;
        try { localizedItems = DataLanguageService.Instance.Strings.GetItemStrings(save.Context, save.Version); }
        catch { /* contexto sin lista de objetos: se usa el nombre de pokemon.db */ }

        var machineNames = new HashSet<string>(StringComparer.Ordinal);
        var dbDescriptions = PokemonDatabase.Instance.GetItemDescriptions(
            rowsInTile.Select(r => r.ItemId), DataLanguageService.Instance.PkhexCode);
        var machineMoves = PokemonDatabase.Instance.GetMachineMovesByItem(gameId);
        // Nombres de respaldo de pokemon.db (ItemNames) para objetos que PKHeX no nombra (nuevos de Z-A).
        var dbNames = PokemonDatabase.Instance.GetItemNames(
            rowsInTile.Select(r => r.ItemId), DataLanguageService.Instance.PkhexCode);
        foreach (var row in rowsInTile)
        {
            dbItems.TryGetValue(row.ItemId, out var db);
            var name = db?.Name ?? $"Ítem #{row.ItemId}";
            // Los ~131 ítems sintéticos de Colosseum/XD (ItemId >= 90000) no tienen RawItemId significativo.
            string? displayName = null;
            if (localizedItems != null && row.ItemId < 90000 && row.RawItemId > 0 && row.RawItemId < localizedItems.Count
                && !string.IsNullOrWhiteSpace(localizedItems[row.RawItemId]))
                displayName = localizedItems[row.RawItemId];
            if (string.IsNullOrWhiteSpace(displayName) && dbNames.TryGetValue(row.ItemId, out var tableName))
                displayName = tableName;
            // TM/HM/TR: la Description cruda mezcla varias generaciones en una sola oración —
            // se resuelve al movimiento real de ESTA generación (ver TmDescriptionResolver).
            // Descripción en el idioma de los datos desde pokemon.db (ItemDescriptions / MachineMoves).
            // MT/MO/DT: movimiento por juego (MachineMoves) → «Enseña X…»; sin fila, respaldo por el texto inglés
            // (TmDescriptionResolver resuelve la generación del save).
            var isMachine = machineMoves.ContainsKey(row.ItemId) || db?.Category == "all-machines" || MachineName.IsMatch(name); // MT sin categoría ni fila (p. ej. TM161 de Z-A) también cuentan
            string desc;
            if (isMachine)
            {
                var fromTable = machineMoves.TryGetValue(row.ItemId, out var machineMove)
                    ? ItemDescriptionLocalizer.TeachesMove(machineMove) : string.Empty;
                desc = fromTable.Length > 0
                    ? fromTable
                    : ItemDescriptionLocalizer.LegacyMachine(
                        TmDescriptionResolver.Resolve(db?.Description ?? string.Empty, generation));
            }
            else
                desc = dbDescriptions.TryGetValue(row.ItemId, out var text) ? text : db?.Description ?? string.Empty;

            // RawItemId (no ItemId) — ver comentario de clase, punto 4. Se expone como
            // BagItemInfo.ItemId: EditApplyService.ApplyBagPouch matchea directo contra esto.
            var qty = ownedByRawIndex.TryGetValue(row.RawItemId, out var c) ? c : 0;
            result.Add(new BagItemInfo(row.RawItemId, name, desc, qty, realPouch.MaxCount, displayName));
            if (isMachine) machineNames.Add(name);
        }
        FillMissingMachineNames(result, machineNames);
        return result.OrderBy(i => i.DisplayName ?? i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static readonly Regex MachineName = new(@"^(TM|TR|HM)(\d+)$", RegexOptions.Compiled);

    private static bool IsDigit(char c) => char.IsDigit(c); // incluye dígitos de ancho completo (０-９)

    /// <summary>PKHeX a veces no trae el nombre de las MT más nuevas en ciertos idiomas (p. ej. las MT de Z-A en
    /// ja/ko/zh): quedaban con el nombre «TM157» de pokemon.db mezcladas con «わざ００１», y al ordenar parecían una
    /// segunda lista. Se completan copiando el formato (prefijo, cantidad de dígitos, ancho completo) de una MT/DT/MO
    /// del mismo tipo que sí tiene nombre localizado.</summary>
    private static void FillMissingMachineNames(List<BagItemInfo> items, HashSet<string> machineNames)
    {
        if (machineNames.Count == 0) return;
        // plantilla por tipo (TM/TR/HM): (prefijo localizado, nº de dígitos, ancho completo?)
        var templates = new Dictionary<string, (string Prefix, int Digits, bool Wide)>();
        foreach (var it in items)
        {
            if (it.DisplayName == null || !machineNames.Contains(it.Name)) continue;
            var m = MachineName.Match(it.Name);
            if (!m.Success || templates.ContainsKey(m.Groups[1].Value)) continue;
            var d = it.DisplayName;
            var end = d.Length;
            var start = end;
            while (start > 0 && IsDigit(d[start - 1])) start--;
            var digits = end - start;
            if (digits == 0) continue; // nombre sin número final: no hay patrón que copiar
            templates[m.Groups[1].Value] = (d[..start], digits, d[start] > '\u00ff');
        }
        if (templates.Count == 0) return;
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.DisplayName != null || !machineNames.Contains(it.Name)) continue;
            var m = MachineName.Match(it.Name);
            if (!m.Success || !templates.TryGetValue(m.Groups[1].Value, out var t)) continue;
            var num = int.Parse(m.Groups[2].Value).ToString().PadLeft(t.Digits, '0');
            if (t.Wide) num = new string(num.Select(c => (char)('０' + (c - '0'))).ToArray());
            items[i] = it with { DisplayName = t.Prefix + num };
        }
    }

    // Copia local minúscula de BagPouchDatabase.IsAlternateStorage (Exxeguttor.UI) — esta capa
    // (Exxeguttor.App) no puede referenciar Exxeguttor.UI (ver arquitectura en CLAUDE.md), y no
    // vale la pena mover la tabla entera solo por este chequeo de dos valores.
    private static bool IsAlternateStorage(InventoryType type) =>
        type is InventoryType.PCItems or InventoryType.FreeSpace;
}

/// <summary>Resumen de una categoría para la tile del panel central (grilla de categorías).</summary>
public readonly record struct BagPouchInfo(InventoryType Type, int MaxCount, int OwnedCount, int LegalItemCount);

/// <summary>Una fila de la lista de ítems del panel derecho. Quantity es el valor real del
/// save (sin ediciones pendientes de esta sesión aplicadas — eso lo resuelve el ViewModel
/// cruzando con EditSessionService). PouchMaxCount es el tope que usa el botón "Max".
///
/// ⚠️ ItemId acá es <c>ItemGameCodes.RawItemId</c> (el índice real de PKHeX.Core para ESTE
/// juego), NO el ItemId unificado de pokemon.db — ver comentario de clase de
/// ItemInventoryService, punto 4. EditApplyService.ApplyBagPouch lo usa tal cual para matchear
/// contra <c>pouch.Items[].Index</c>, sin traducir nada más.
///
/// <paramref name="Name"/> es la llave de pokemon.db (inglés): sigue siendo la del sprite.
/// <paramref name="DisplayName"/> es el texto a mostrar, en el idioma de los datos activo (null = sin
/// nombre de PKHeX para este ítem → mostrar <paramref name="Name"/>).</summary>
public readonly record struct BagItemInfo(int ItemId, string Name, string Description, int Quantity, int PouchMaxCount, string? DisplayName = null);
