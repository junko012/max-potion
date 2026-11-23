using System;
using System.Linq;
using System.Reflection;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// ARCHIVO TEMPORAL DE DIAGNÓSTICO — para el módulo "Mochila / Objetos".
// Confirma contra PKHeX.Core 25.11.7 real (no memoria/entrenamiento) tres cosas antes de
// terminar de cerrar ItemInventoryService:
//   1) Qué pouches (InventoryType) trae cada generación en un save en blanco.
//   2) MaxCount real por pouch (el tope para el botón "Max" del stepper).
//   3) La firma exacta de los métodos de InventoryPouch que nos interesan (GetAllItems,
//      CanContain, GetSuggestedItemCount, GiveItem, IsValidItemAndCount, Sanitize) — ya
//      confirmamos que EXISTEN por metadata cruda del DLL (dnfile, sin dotnet SDK
//      disponible en el sandbox), pero no los parámetros exactos.
// Confirmado ya por inspección de metadata (no hace falta re-diagnosticar):
//   - SaveFile.Inventory (get/set) existe en SAV1 y SAV2 también — Gen1/2 exponen la misma
//     API unificada que el resto, no hay un sistema paralelo.
//   - InventoryType incluye PCItems como un valor más del enum (16 valores en total: None,
//     Items, KeyItems, TMHMs, Medicine, Berries, Balls, BattleItems, MailItems, PCItems,
//     FreeSpace, ZCrystals, Candy, Treasure, Ingredients, MegaStones).
public class InventoryApiDiagnosticTests
{
    [Fact]
    public void DiagnosticarApiInventory()
    {
        var sb = new System.Text.StringBuilder();

        void DumpMethods(Type t, string label, Func<string, bool> filter)
        {
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => filter(m.Name) && !m.IsSpecialName)
                .Select(m => $"  {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}) -> {m.ReturnType.Name}")
                .Distinct()
                .OrderBy(s => s);
            sb.AppendLine($"── {label} ──");
            sb.AppendLine(string.Join("\n", methods));
            sb.AppendLine();
        }

        // 1) Firmas exactas de InventoryPouch — para saber si GetAllItems()/CanContain()/
        //    GetSuggestedItemCount() sirven para "listar todos los ítems legales de un pouch"
        //    y "tope real para el botón Max", o si hay que resolverlo distinto.
        DumpMethods(typeof(InventoryPouch), "InventoryPouch (instancia)", n => true);
        DumpMethods(typeof(InventoryItem), "InventoryItem (instancia)", n => true);

        sb.AppendLine("── InventoryPouch: campos públicos ──");
        foreach (var f in typeof(InventoryPouch).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            sb.AppendLine($"  {f.PropertyType.Name} {f.Name}");
        foreach (var f in typeof(InventoryPouch).GetFields(BindingFlags.Public | BindingFlags.Instance))
            sb.AppendLine($"  field {f.FieldType.Name} {f.Name}");
        sb.AppendLine();

        // 2) Qué pouches trae cada gen en un save en blanco, y MaxCount de cada uno.
        //    RD=Rojo(Gen1), GD=Oro(Gen2), E=Esmeralda(Gen3), D=Diamante(Gen4), B=Negro(Gen5),
        //    X=X(Gen6), SN=Sol(Gen7), GP=LetsGoPikachu(Gen7b), SW=Espada(Gen8), SL=Escarlata(Gen9).
        (string Label, GameVersion Version)[] versions =
        {
            ("Gen1 — Rojo (RD)", GameVersion.RD),
            ("Gen2 — Oro (GD)", GameVersion.GD),
            ("Gen3 — Esmeralda (E)", GameVersion.E),
            ("Gen4 — Diamante (D)", GameVersion.D),
            ("Gen5 — Negro (B)", GameVersion.B),
            ("Gen6 — X (X)", GameVersion.X),
            ("Gen7 — Sol (SN)", GameVersion.SN),
            ("Gen7b — Let's Go Pikachu (GP)", GameVersion.GP),
            ("Gen8 — Espada (SW)", GameVersion.SW),
            ("Gen9 — Escarlata (SL)", GameVersion.SL),
        };

        foreach (var (label, version) in versions)
        {
            sb.AppendLine($"── Inventory: {label} ──");
            try
            {
                var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
                var inv = save.Inventory;
                // .Count() (extensión LINQ) en vez de .Count/.Length — no sabemos todavía si
                // Inventory/Items son array, List<T> o IReadOnlyList<T> exacto, y LINQ
                // funciona igual para los tres sin arriesgar un error de compilación acá.
                sb.AppendLine($"  Inventory.Count() = {inv.Count()}, tipo colección = {inv.GetType().Name}");
                foreach (var pouch in inv)
                {
                    sb.AppendLine($"  Type={pouch.Type,-12} MaxCount={pouch.MaxCount,-6} Items.Count()={pouch.Items.Count()} Items[]tipo={pouch.Items.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  FALLÓ: {ex.GetType().Name}: {ex.Message}");
            }
            sb.AppendLine();
        }

        Assert.Fail(sb.ToString());
    }

    // SEGUNDA VUELTA — sesión "categorización de Mochila", bug reportado: MTs/HMs vacías en
    // Gen1 Y Gen2 al clickear la tile, mientras el resto de categorías carga bien. El primer
    // diagnóstico de arriba confirmó que Gen1 NO tiene pouch TMHMs propio (fallback a Items) y
    // Gen2 SÍ lo tiene (57 slots, dedicado) — que ambos casos fallen igual apunta a que el
    // problema es la NUMERACIÓN de Index, no la existencia del pouch: pokemon.db (construido
    // contra PokeAPI, ItemId de TM01=328 en adelante) puede no coincidir con el índice
    // que PKHeX.Core usa internamente para esos mismos ítems en formatos legacy.
    //
    // GetAllItems() -> ReadOnlySpan<ushort> (confirmado arriba) es la lista canónica de índices
    // legales de PKHeX.Core para un pouch puntual — cruzando esto contra el rango 328+ que
    // pokemon.db asume para TM01+ se ve directo si coinciden o no.
    [Fact]
    public void DiagnosticarIndicesTMHM()
    {
        var sb = new System.Text.StringBuilder();

        void DumpPouch(SaveFile save, InventoryType type, string label)
        {
            sb.AppendLine($"── {label}: pouch {type} ──");
            var pouch = save.Inventory.FirstOrDefault(p => p.Type == type);
            if (pouch == null) { sb.AppendLine("  (no existe este pouch en este save)"); sb.AppendLine(); return; }

            var all = pouch.GetAllItems().ToArray();
            sb.AppendLine($"  GetAllItems() = {all.Length} índices: [{string.Join(", ", all)}]");

            sb.AppendLine("  pouch.Items[] (slots físicos, Index crudo por slot):");
            foreach (var item in pouch.Items)
                sb.AppendLine($"    Index={item.Index} Count={item.Count}");
            sb.AppendLine();
        }

        var rd = BlankSaveFile.Get(GameVersion.RD, "TEST", LanguageID.English);
        DumpPouch(rd, InventoryType.Items, "Gen1 — Rojo");

        var gd = BlankSaveFile.Get(GameVersion.GD, "TEST", LanguageID.English);
        DumpPouch(gd, InventoryType.TMHMs, "Gen2 — Oro");

        // Referencia: para comparar, el mismo pouch en un formato "confiable" (Gen3) — ahí ya
        // sabemos por Juan que las MTs SÍ cargan bien, sirve de control.
        var e = BlankSaveFile.Get(GameVersion.E, "TEST", LanguageID.English);
        DumpPouch(e, InventoryType.TMHMs, "Gen3 — Esmeralda (control, funciona bien)");

        Assert.Fail(sb.ToString());
    }

    // TERCERA VUELTA — el diagnóstico anterior (DiagnosticarIndicesTMHM) confirmó que
    // GetAllItems() para el pouch TMHMs usa una numeración PROPIA de cada generación (Gen1:
    // 196-250, Gen2: 191-249, Gen3: 289-346 — ninguna coincide con el ItemId=328+ que
    // pokemon.db le da a "TM01"). Esto significa que pokemon.db modela mal las MTs/MOs
    // (un solo ItemId compartido entre generaciones, cuando PKHeX.Core las trata como ítems
    // DISTINTOS por generación) — pero el editor real de PKHeX sí resuelve el nombre
    // correcto ("TM01" ligado al movimiento que corresponde) para un save Gen1, así que tiene
    // que existir la API correcta en algún lado — esto la busca por reflection en vez de
    // adivinar de nuevo.
    [Fact]
    public void DiagnosticarNombresTM()
    {
        var sb = new System.Text.StringBuilder();
        var asm = typeof(SaveFile).Assembly;

        sb.AppendLine("── Tipos públicos con 'TM'/'Machine'/'ItemStorage' en el nombre ──");
        foreach (var t in asm.GetTypes()
                     .Where(t => t.IsPublic && (t.Name.Contains("TM") || t.Name.Contains("Machine") || t.Name.Contains("ItemStorage")))
                     .OrderBy(t => t.FullName))
            sb.AppendLine($"  {t.FullName}");
        sb.AppendLine();

        sb.AppendLine("── Métodos públicos ESTÁTICOS con 'Item' Y 'Name' en el nombre (buscando algo tipo GetItemName(item, generation)) ──");
        foreach (var t in asm.GetTypes().Where(t => t.IsPublic))
        {
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name.Contains("Item") && m.Name.Contains("Name") && !m.IsSpecialName);
            foreach (var m in methods)
                sb.AppendLine($"  {t.Name}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}) -> {m.ReturnType.Name}");
        }
        sb.AppendLine();

        sb.AppendLine("── Miembros públicos de GameStrings con 'Item' en el nombre ──");
        foreach (var m in typeof(GameStrings).GetMembers(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m.Name.Contains("Item")))
            sb.AppendLine($"  {m.MemberType} {m.Name}");
        sb.AppendLine();

        var itemStorageType = asm.GetTypes().FirstOrDefault(t => t.Name is "ItemStorage" or "IItemStorage");
        if (itemStorageType != null)
        {
            sb.AppendLine($"── Miembros de {itemStorageType.Name} (campo 'Info' de InventoryPouch era de este tipo) ──");
            var members = itemStorageType.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m is not MethodInfo mi || !mi.IsSpecialName)
                .Select(m => m is MethodInfo mi
                    ? $"  {mi.Name}({string.Join(", ", mi.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}) -> {mi.ReturnType.Name}"
                    : $"  {m.MemberType} {m.Name}")
                .Distinct();
            foreach (var m in members) sb.AppendLine(m);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("── No se encontró ningún tipo 'ItemStorage'/'IItemStorage' público ──");
            sb.AppendLine();
        }

        sb.AppendLine("── Tipos legacy Gen1/2 relacionados con TM (nombre con '1'/'2' + 'TM'/'Machine') ──");
        foreach (var t in asm.GetTypes()
                     .Where(t => t.IsPublic && System.Text.RegularExpressions.Regex.IsMatch(t.Name, @"(TM|Machine).*[12]|[12].*(TM|Machine)"))
                     .OrderBy(t => t.FullName))
            sb.AppendLine($"  {t.FullName}");

        Assert.Fail(sb.ToString());
    }

    // CUARTA VUELTA — encontramos GameStrings.GetItemStrings (generation-aware, justo lo que
    // hace falta) y las clases ItemStorage1/ItemStorage2/ItemStorage3E/etc (candidatas a tener
    // la lista ordenada real de MTs/MOs por juego, ver "field IItemStorage Info" de
    // InventoryPouch). Esto saca las firmas exactas antes de escribir el fix.
    [Fact]
    public void DiagnosticarGetItemStringsYItemStorage()
    {
        var sb = new System.Text.StringBuilder();
        var asm = typeof(SaveFile).Assembly;

        void DumpType(Type t)
        {
            sb.AppendLine($"── {t.FullName} ──");
            var members = t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m is not MethodInfo mi || !mi.IsSpecialName)
                .Select(m => m switch
                {
                    MethodInfo mi => $"  {(mi.IsStatic ? "static " : "")}{mi.Name}({string.Join(", ", mi.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}) -> {mi.ReturnType.Name}",
                    FieldInfo fi => $"  {(fi.IsStatic ? "static field " : "field ")}{fi.FieldType.Name} {fi.Name}",
                    PropertyInfo pi => $"  {(pi.GetMethod?.IsStatic == true ? "static " : "")}property {pi.PropertyType.Name} {pi.Name}",
                    _ => $"  {m.MemberType} {m.Name}"
                })
                .Distinct();
            foreach (var m in members) sb.AppendLine(m);
            sb.AppendLine();
        }

        DumpType(typeof(GameStrings));

        foreach (var name in new[] { "ItemStorage1", "ItemStorage2", "ItemStorage3E", "ItemStorage3FRLG", "ItemStorage3RS", "IItemStorage" })
        {
            var t = asm.GetType($"PKHeX.Core.{name}");
            if (t != null) DumpType(t);
            else sb.AppendLine($"── {name}: NO ENCONTRADO ──\n");
        }

        // Si GetItemStrings existe, probarlo en vivo contra un save Gen1 real (blanco) y
        // volcar los nombres en el rango 196-250 (el rango real de MTs/MOs de Gen1 que ya
        // confirmamos con DiagnosticarIndicesTMHM) — a ver si ahí sí dice "TM01" etc.
        var method = typeof(GameStrings).GetMethod("GetItemStrings");
        if (method != null)
        {
            sb.AppendLine($"── GetItemStrings encontrado, params: {string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))} ──");
        }

        Assert.Fail(sb.ToString());
    }
}
