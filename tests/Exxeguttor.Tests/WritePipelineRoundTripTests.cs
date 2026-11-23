using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// ARCHIVO DE VALIDACIÓN — pipeline de escritura real (GAP CRÍTICO, ver CLAUDE.md/context.md).
//
// Enfoque: mutación EN MEMORIA contra la misma instancia de SaveFile (SetBoxSlotAtIndex +
// GetBoxSlotAtIndex sobre el mismo objeto), no round-trip binario completo (Write() + releer
// con SaveUtil.GetSaveFile). Motivo, confirmado en dos corridas anteriores:
//   - BlankSaveFile.Get no deja el save en un estado "recién salido de la consola" — le
//     faltan bytes de inicialización (contador de revisión en SAV3/SAV4 vía BlockInfo.
//     GetRevision, fecha jugada en SV, etc.) que Write()/GetFinalData() dan por sentado que
//     existen. Confirmado que esto pega en SAV3 Y SAV4 (mismo patrón BlockInfo.SetChecksums),
//     no es un caso aislado de una sola generación.
//   - Confirmado a mano con un save Gen3 REAL (abrir sin editar + Exportar en la app tal cual
//     está hoy) que esto NO pasa en producción — es puramente un artefacto de sintetizar un
//     save desde cero con BlankSaveFile.Get, no algo que vaya a afectar al pipeline real
//     (que siempre parte de un save YA ABIERTO desde un archivo real).
// El pipeline real nunca necesita re-parsear sus propios bytes recién escritos — muta el
// SaveFile en memoria y lo escribe a disco una sola vez al final. Por eso probar
// SetBoxSlotAtIndex → GetBoxSlotAtIndex sobre la MISMA instancia cubre lo que de verdad
// importa, sin depender de que BlankSaveFile.Get deje todo listo para un Write() de punta a
// punta.
//
// Aparte, más abajo, un test separado (Diagnostico_WriteNoExplotaPorGeneracion) documenta en
// qué generaciones Write() funciona sin tocar nada más — informativo, no bloquea el resto.
public class WritePipelineRoundTripTests
{
    private static PKM MakeMinimalPkm(SaveFile save, ushort species = 1)
    {
        var pkm = EntityBlank.GetBlank(save);
        pkm.Species  = species;
        pkm.MetLevel = 5;
        pkm.Version  = save.Version;
        pkm.Language = save.Language;
        pkm.OriginalTrainerName   = save.OT;
        pkm.TID16                 = save.TID16;
        pkm.SID16                 = save.SID16;
        pkm.OriginalTrainerGender = save.Gender;
        pkm.PID = 12345;
        pkm.RefreshChecksum();
        return pkm;
    }

    // ── Pokémon: campos básicos, IVs/EVs, movimientos+PPUps (en memoria) ────────────────────
    [Theory]
    [InlineData(35 /* GameVersion.RD = Gen1 */)]
    [InlineData(3  /* GameVersion.E  = Gen3 */)]
    [InlineData(10 /* GameVersion.D  = Gen4 */)]
    [InlineData(44 /* GameVersion.SW = Gen8 */)]
    [InlineData(50 /* GameVersion.SL = Gen9 */)]
    public void MutarPokemon_SeReflejaAlReleerDelMismoSave(int versionRaw)
    {
        var version = (GameVersion)versionRaw;
        var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
        var pkm = MakeMinimalPkm(save);

        pkm.CurrentLevel = 42;
        // Gen 1-2: los IV son DVs (0-15) y el IV de HP se deriva de los otros, no se fija a mano.
        var gen12 = save.Generation <= 2;
        var ivAtk = gen12 ? 14 : 20;
        if (!gen12) pkm.IV_HP = 31;
        pkm.IV_ATK = ivAtk; pkm.IV_DEF = 15;
        pkm.EV_HP  = 4;  pkm.EV_ATK = 252;
        pkm.Move1 = 1; pkm.Move1_PP = 35; pkm.Move1_PPUps = 3;
        pkm.RefreshChecksum();

        // EntityImportSettings default (struct) — "lo conservador", compila sin constructor
        // explícito porque hereda de System.ValueType.
        save.SetBoxSlotAtIndex(pkm, 0, 0, default);

        // Releer de la MISMA instancia, sin pasar por Write()/GetSaveFile.
        var reread = save.GetBoxSlotAtIndex(0, 0);

        Assert.Equal(42, reread.CurrentLevel);
        if (!gen12) Assert.Equal(31, reread.IV_HP);
        Assert.Equal(ivAtk, reread.IV_ATK);
        Assert.Equal(252, reread.EV_ATK);
        Assert.Equal(1, reread.Move1);
        Assert.Equal(3, reread.Move1_PPUps);
    }

    // ── Entrenador: nombre, TID/SID, dinero (en memoria, ni hace falta Write) ───────────────
    [Fact]
    public void MutarEntrenador_SeReflejaInmediatamente()
    {
        var save = BlankSaveFile.Get(GameVersion.D, "TEST", LanguageID.English);

        save.OT    = "Selene";
        save.TID16 = 48213;
        save.SID16 = 1;
        save.Money = 18940;

        Assert.Equal("Selene", save.OT);
        Assert.Equal(48213, save.TID16);
        Assert.Equal(18940, (int)save.Money);
    }

    // ── Mochila: los slots del pouch son de TAMAÑO FIJO, no se puede sacar/agregar entradas
    // libremente (InventoryPouch3/4.SetPouch tira ArgumentOutOfRangeException si el array
    // cambia de largo). Siempre tiene exactamente Items.Length slots — un slot "vacío" tiene
    // Count=0, no se elimina del array. Para escribir: buscar el slot que ya tiene ese ItemId
    // (actualizar Count in-place) o, si es cantidad nueva, el primer slot con Count=0.
    [Fact]
    public void MutarMochila_SeReflejaInmediatamente()
    {
        var save = BlankSaveFile.Get(GameVersion.D, "TEST", LanguageID.English);
        var inventory = save.Inventory.ToArray();
        var itemsPouch = inventory.First(p => p.Type == InventoryType.Items);
        var originalLength = itemsPouch.Items.Length;

        var items = itemsPouch.Items;
        var targetSlot = Array.FindIndex(items, i => i.Index == 1);         // ya está en el pouch
        if (targetSlot < 0)
            targetSlot = Array.FindIndex(items, i => i.Count == 0);         // primer slot libre

        Assert.True(targetSlot >= 0, "No debería quedarse sin slots libres en un pouch recién creado.");
        items[targetSlot] = new InventoryItem { Index = 1, Count = 5 };
        itemsPouch.Items = items;

        save.Inventory = inventory;
        Assert.Equal(originalLength, itemsPouch.Items.Length); // el pouch nunca cambia de tamaño

        var reloadedPouch = save.Inventory.First(p => p.Type == InventoryType.Items);
        var found = reloadedPouch.Items.FirstOrDefault(i => i.Index == 1 && i.Count > 0);

        Assert.NotNull(found);
        Assert.Equal(5, found!.Count);
    }

    // ── Cintas, formato viejo (interfaz con setter propio por cinta) — Gen3 y Gen4 comparten
    // el mismo mecanismo (IRibbonSetCommonN), RibbonWriter va a necesitar este camino para
    // todas las generaciones viejas.
    [Theory]
    [InlineData(3  /* GameVersion.E = Gen3 */)]
    [InlineData(10 /* GameVersion.D = Gen4 */)]
    public void MutarCintaFormatoViejo_SeReflejaInmediatamente(int versionRaw)
    {
        var version = (GameVersion)versionRaw;
        var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
        var pkm = MakeMinimalPkm(save);

        bool despues;
        if (pkm is IRibbonSetCommon4 r4)
        {
            r4.RibbonChampionSinnoh = true;
            despues = r4.RibbonChampionSinnoh;
        }
        else if (pkm is IRibbonSetCommon3 r3)
        {
            r3.RibbonChampionG3 = true;
            despues = r3.RibbonChampionG3;
        }
        else
        {
            throw new InvalidOperationException($"El PKM de {version} no implementa ninguna interfaz de cinta vieja conocida — revisar mapeo de RibbonReader/RibbonWriter.");
        }
        Assert.True(despues, "El setter debería reflejarse inmediatamente en el mismo objeto.");
        pkm.RefreshChecksum();

        save.SetBoxSlotAtIndex(pkm, 0, 0, default);
        var reread = save.GetBoxSlotAtIndex(0, 0);

        var rereadValue = reread is IRibbonSetCommon4 rr4 ? rr4.RibbonChampionSinnoh
                         : reread is IRibbonSetCommon3 rr3 ? rr3.RibbonChampionG3
                         : throw new InvalidOperationException("Tipo de cinta inesperado al releer.");
        Assert.True(rereadValue);
    }

    // ── Cintas, formato moderno (IRibbonIndex.SetRibbon, no hay setter por propiedad) ───────
    [Fact]
    public void MutarCintaFormatoModerno_SeReflejaInmediatamente()
    {
        var save = BlankSaveFile.Get(GameVersion.SL, "TEST", LanguageID.English);
        var pkm = MakeMinimalPkm(save);

        Assert.True(pkm is IRibbonIndex, "El PKM de Gen9 debería implementar IRibbonIndex — si esto falla, revisar RibbonReader para Gen9.");
        var ribbonIndex = (int)RibbonIndex.ChampionG3; // cualquier valor válido del enum sirve para probar el mecanismo
        ((IRibbonIndex)pkm).SetRibbon(ribbonIndex, true);
        pkm.RefreshChecksum();

        save.SetBoxSlotAtIndex(pkm, 0, 0, default);
        var reread = save.GetBoxSlotAtIndex(0, 0);

        Assert.True(((IRibbonIndex)reread).GetRibbon(ribbonIndex));
    }

    // ── Diagnóstico informativo: ¿Write() explota con un save recién sintetizado por
    // BlankSaveFile.Get? No bloquea nada (try/catch, siempre reporta) — documenta el estado
    // real por generación en vez de asumir. Ya sabemos que SAV3/SAV4 fallan acá (BlockInfo.
    // GetRevision) y que un save REAL de esas mismas generaciones anda bien (confirmado a
    // mano) — esto es solo para tener constancia escrita de qué generaciones tienen esta
    // limitación puntual de BlankSaveFile.Get.
    [Fact]
    public void Diagnostico_WriteNoExplotaPorGeneracion()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (label, version) in new (string, GameVersion)[]
                 {
                     ("Gen1 Rojo", GameVersion.RD),
                     ("Gen3 Esmeralda", GameVersion.E),
                     ("Gen4 Diamante", GameVersion.D),
                     ("Gen5 Negro", GameVersion.B),
                     ("Gen6 X", GameVersion.X),
                     ("Gen7 Sol", GameVersion.SN),
                     ("Gen8 Espada", GameVersion.SW),
                     ("Gen9 Escarlata", GameVersion.SL),
                 })
        {
            var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
            try
            {
                _ = save.Write(BinaryExportSetting.None);
                sb.AppendLine($"  {label}: Write() OK");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  {label}: Write() FALLÓ — {ex.GetType().Name}: {ex.Message}");
            }
        }
        Assert.Fail(sb.ToString());
    }

    // ── Diagnóstico (no assertion real): convenciones de nombre de archivo/backup ───────────
    [Fact]
    public void Diagnostico_ConvencionesDeNombreDeArchivo()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (label, version) in new (string, GameVersion)[]
                 {
                     ("Gen1 Rojo", GameVersion.RD),
                     ("Gen3 Esmeralda", GameVersion.E),
                     ("Gen8 Espada", GameVersion.SW),
                     ("Gen9 Escarlata", GameVersion.SL),
                 })
        {
            var save = BlankSaveFile.Get(version, "TEST", LanguageID.English);
            sb.AppendLine($"── {label} ──");
            sb.AppendLine($"  save.Extension = \"{save.Extension}\"");
            sb.AppendLine($"  save.Metadata.GetSuggestedExtension() = \"{save.Metadata.GetSuggestedExtension()}\"");
            try
            {
                sb.AppendLine($"  save.Metadata.GetBackupFileName(\"/tmp\") = \"{save.Metadata.GetBackupFileName("/tmp")}\"");
            }
            catch (Exception ex) { sb.AppendLine($"  GetBackupFileName FALLÓ: {ex.GetType().Name}: {ex.Message} (esperable: BlankSaveFile.Get no inicializa fecha/hora jugada en SV, un save real del usuario sí la tiene siempre)"); }
            sb.AppendLine();
        }
        Assert.Fail(sb.ToString());
    }
}
