using System.Linq;
using System.Reflection;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Fase 2 de formas: crear Pokémon con forma regional (Marowak de Alola, Meowth de Galar...)
// desde el selector. Ver hotfixes/formas_handoff/fase-formas-handoff.md (sección 5) en el repo
// de contexto y RegionalFormCatalog para el diseño.
//
// Dos grupos de tests:
//  1) Catálogo (RegionalFormCatalog): no toca la base de datos. Su función es avisar si una
//     actualización de PKHeX.Core cambia/agrega formas regionales y el catálogo (y sus sprites en
//     Assets/sprites/pokemon/forms/) quedó desactualizado — la lista es fija, no se descubre sola.
//  2) Creación (BuildPokemon con forma): valida contra el motor de legalidad real de PKHeX.Core
//     que la forma pedida es la que sale Y que sale legal. Usa la pokemon.db real.
public class RegionalFormCreationTests
{
    // SaveFileService solo sabe cargar un save desde archivo; para probar con un save en blanco
    // se inyecta por reflexión (mismo estilo que los tests de diagnóstico del proyecto).
    private static PokemonService ServiceFor(SaveFile save)
    {
        var saveService = new SaveFileService();
        typeof(SaveFileService)
            .GetField("_currentSave", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(saveService, save);
        return new PokemonService(saveService);
    }

    private static SaveFile BlankSave(GameVersion version) =>
        BlankSaveFile.Get(version, "TEST", LanguageID.English);

    // ── 1) Catálogo ──────────────────────────────────────────────────────

    [Fact]
    public void Catalogo_NoTieneFormasDuplicadas()
    {
        var duplicadas = RegionalFormCatalog.All
            .GroupBy(f => (f.Species, f.Form))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicadas.Count == 0, "Formas duplicadas en el catálogo: " + string.Join(", ", duplicadas));
    }

    [Fact]
    public void Catalogo_NingunaFormaEsDeSoloBatalla()
    {
        // Una forma de solo batalla (Darmanitan Galar Zen, Mega...) nunca se guarda en un save:
        // ofrecerla como tarjeta de creación no tiene sentido.
        var soloBatalla = RegionalFormCatalog.All
            .Where(f => FormInfo.IsBattleOnlyForm((ushort)f.Species, f.Form, 9))
            .Select(f => $"{f.Species}/{f.Form}")
            .ToList();

        Assert.True(soloBatalla.Count == 0, "Formas de solo batalla en el catálogo: " + string.Join(", ", soloBatalla));
    }

    [Fact]
    public void Catalogo_CadaFormaCoincideConElNombreDeFormaDePKHeX()
    {
        // Si PKHeX.Core reordena los índices de forma en una versión nueva, la forma N del
        // catálogo pasaría a ser OTRA cosa sin que nada falle de forma obvia: este test lo cacha.
        var strings = GameInfo.GetStrings("en");
        var desalineadas = RegionalFormCatalog.All
            .Where(f =>
            {
                var nombres = FormConverter.GetFormList(
                    (ushort)f.Species, strings.Types, strings.forms, GameInfo.GenderSymbolUnicode, EntityContext.Gen9);
                return nombres.Length <= f.Form || nombres[f.Form] != f.Region;
            })
            .Select(f => $"{f.Species}/{f.Form} ({f.Region})")
            .ToList();

        Assert.True(desalineadas.Count == 0, "Formas del catálogo que ya no coinciden con PKHeX: " + string.Join(", ", desalineadas));
    }

    [Theory]
    [InlineData(GameVersion.SN, 105, 1, true)]   // Marowak de Alola existe en Sol/Luna
    [InlineData(GameVersion.SN, 52, 1, true)]    // Meowth de Alola
    [InlineData(GameVersion.SN, 52, 2, false)]   // Meowth de Galar NO existe en Sol/Luna
    [InlineData(GameVersion.SW, 52, 2, true)]    // ...sí en Espada/Escudo
    [InlineData(GameVersion.SW, 157, 1, false)]  // Typhlosion de Hisui NO está en Espada/Escudo
    [InlineData(GameVersion.PLA, 157, 1, true)]  // ...sí en Leyendas: Arceus
    [InlineData(GameVersion.ZA, 618, 1, false)]  // Stunfisk de Galar: sin encuentro legal en Z-A
    [InlineData(GameVersion.ZA, 713, 1, false)]  // Avalugg de Hisui: idem
    [InlineData(GameVersion.SW, 618, 1, true)]   // ...pero Stunfisk de Galar sí en Espada/Escudo
    [InlineData(GameVersion.PLA, 713, 1, true)]  // ...y Avalugg de Hisui en Leyendas: Arceus
    public void Catalogo_DisponibilidadDependeDelJuegoDelSave(GameVersion version, int species, int form, bool esperada)
    {
        var disponibles = RegionalFormCatalog.AvailableIn(BlankSave(version));

        Assert.Equal(esperada, disponibles.Any(f => f.Species == species && f.Form == form));
    }

    [Fact]
    public void Catalogo_UnSaveDeGen4NoOfreceNingunaForma()
    {
        // Las formas regionales empiezan en Gen7: en Platino la lista tiene que quedar vacía,
        // o el selector mostraría tarjetas que no se pueden crear.
        Assert.Empty(RegionalFormCatalog.AvailableIn(BlankSave(GameVersion.Pt)));
    }

    [Theory]
    [InlineData(105, 1, "Fire,Ghost")]       // Marowak de Alola (en Kanto es solo Ground)
    [InlineData(103, 1, "Grass,Dragon")]     // Exeggutor de Alola
    [InlineData(37, 1, "Ice")]               // Vulpix de Alola: tipo simple
    [InlineData(52, 2, "Steel")]             // Meowth de Galar
    [InlineData(157, 1, "Fire,Ghost")]       // Typhlosion de Hisui
    public void Catalogo_TiposDeLaFormaNoSonLosDeLaEspecieBase(int species, int form, string tiposEsperados)
    {
        var forma = RegionalFormCatalog.Find(species, (byte)form);

        Assert.NotNull(forma);
        Assert.Equal(tiposEsperados, string.Join(",", RegionalFormCatalog.GetTypeNames(forma!.Value)));
    }

    [Fact]
    public void Resolutor_CadaFormaDelCatalogoTieneFilaPropiaEnLaDB()
    {
        // Sin fila propia, el editor mostraría los datos de la especie base (el bug original).
        var sinFila = RegionalFormCatalog.All
            .Where(f => !SpeciesFormResolver.HasOwnRow(f.Species, f.Form))
            .Select(f => $"{f.Species}/{f.Form} ({f.Region})")
            .ToList();

        Assert.True(sinFila.Count == 0, "Formas del catálogo sin fila en pokemon.db: " + string.Join(", ", sinFila));
    }

    [Fact]
    public void Resolutor_LosTiposDeLaDBCoincidenConLosDePKHeX()
    {
        // Cruce de seguridad: la DB (fuente de tipos del proyecto) y PKHeX tienen que decir lo mismo.
        var nombres = GameInfo.GetStrings("en").Types;
        var tablas = new IPersonalTable[] { PersonalTable.SV, PersonalTable.SWSH, PersonalTable.LA, PersonalTable.USUM };
        var distintos = new System.Collections.Generic.List<string>();
        foreach (var f in RegionalFormCatalog.All)
        {
            var tabla = tablas.First(t => t.IsPresentInGame((ushort)f.Species, f.Form));
            var info = (IPersonalType)tabla.GetFormEntry((ushort)f.Species, f.Form);
            var esperados = info.Type1 == info.Type2
                ? new[] { nombres[info.Type1] }
                : new[] { nombres[info.Type1], nombres[info.Type2] };
            var db = RegionalFormCatalog.GetTypeNames(f);
            if (!esperados.SequenceEqual(db))
                distintos.Add($"{f.Species}/{f.Form}: DB={string.Join("/", db)} PKHeX={string.Join("/", esperados)}");
        }

        Assert.True(distintos.Count == 0, string.Join("; ", distintos));
    }

    [Theory]
    [InlineData(103, 1, 10114)]   // Exeggutor de Alola
    [InlineData(110, 1, 10167)]   // Weezing de Galar
    [InlineData(26, 1, 10100)]    // Raichu de Alola
    [InlineData(52, 1, 10107)]    // Meowth de Alola
    [InlineData(52, 2, 10161)]    // Meowth de Galar (índice de forma 2, no 1)
    [InlineData(128, 3, 10252)]   // Tauros Paldea Aqua
    [InlineData(555, 2, 10177)]   // Darmanitan Galar (modo normal, no el Zen que es solo de batalla)
    [InlineData(103, 0, 103)]     // forma base: la propia especie
    [InlineData(25, 5, 25)]       // forma sin fila de forma regional: cae a la base
    public void Resolutor_DevuelveLaFilaDeLaForma(int species, int form, int esperado)
    {
        Assert.Equal(esperado, SpeciesFormResolver.ResolveDbSpeciesId(species, (byte)form));
    }

    [Theory]
    [InlineData(103, 1, "Exeggutor-Alola")]
    [InlineData(110, 1, "Weezing-Galar")]
    [InlineData(128, 3, "Tauros-Paldea-Aqua")]
    [InlineData(103, 0, "Exeggutor")]
    public void NombreShowdown_DeLaFormaParaLosSets(int species, int form, string esperado)
    {
        Assert.Equal(esperado, RegionalFormCatalog.ShowdownName("Exeggutor".Replace("Exeggutor", species switch { 110 => "Weezing", 128 => "Tauros", _ => "Exeggutor" }), species, (byte)form));
    }

    [Theory]
    [InlineData(GameVersion.SN, 103, 1)]
    [InlineData(GameVersion.SW, 52, 2)]
    [InlineData(GameVersion.PLA, 157, 1)]
    public void ComboForma_UnPokemonRegionalNoOfreceCambiarDeForma(GameVersion version, int species, int form)
    {
        // Exeggutor de Alola NO es "Exeggutor con otra forma": es otro Pokémon. El combo no aparece.
        var pkm = ServiceFor(BlankSave(version)).BuildPokemon(species, (byte)form);

        Assert.True(PokemonService.GetSelectableForms(pkm, GameInfo.GetStrings("en")) == null);
    }

    [Theory]
    [InlineData(GameVersion.SN, 103)]   // Exeggutor base: tiene la forma de Alola pero no se ofrece
    [InlineData(GameVersion.SW, 52)]
    public void ComboForma_UnPokemonBaseNoOfreceLasFormasRegionales(GameVersion version, int species)
    {
        var pkm = ServiceFor(BlankSave(version)).BuildPokemon(species);

        var formas = PokemonService.GetSelectableForms(pkm, GameInfo.GetStrings("en"));

        Assert.True(formas == null || formas.All(f => RegionalFormCatalog.Find(species, f.Index) == null));
    }

    [Fact]
    public void ComboForma_LasFormasNoRegionalesSiguenEditables()
    {
        // Regresión de la Fase 1: Giratina (Altered/Origin) sigue teniendo combo.
        var pkm = ServiceFor(BlankSave(GameVersion.PLA)).BuildPokemon(487);

        var formas = PokemonService.GetSelectableForms(pkm, GameInfo.GetStrings("en"));

        Assert.True(formas != null && formas.Count == 2);
    }

    // ── Formas guardables que NO son regionales (Giratina Origen, Deoxys, Rotom...) ──

    [Theory]
    [InlineData(487, 1, 10007)]    // Giratina Origen — cambia stats y habilidad (Levitate)
    [InlineData(386, 1, 10001)]    // Deoxys Ataque
    [InlineData(479, 3, 10010)]    // Rotom Frost — cambia el tipo
    [InlineData(550, 2, 10247)]    // Basculin White-Striped — SOLO cambia la habilidad
    [InlineData(678, 1, 10025)]    // Meowstic hembra — SOLO cambia la habilidad
    [InlineData(849, 1, 10184)]    // Toxtricity Low Key — SOLO cambia la habilidad
    [InlineData(641, 1, 10019)]    // Tornadus Therian
    [InlineData(646, 2, 10022)]    // Kyurem Black
    [InlineData(201, 1, 201)]      // Unown (forma cosmética): sin fila de forma, cae a la especie
    [InlineData(412, 1, 412)]      // Burmy Sandy: cosmética, mismos datos que la base
    public void Resolutor_FormasNoRegionales(int species, int form, int esperado)
    {
        Assert.Equal(esperado, SpeciesFormResolver.ResolveDbSpeciesId(species, (byte)form));
    }

    [Fact]
    public void Resolutor_UnaFormaDeBatallaNuncaSeResuelveAUnaFilaMegaOGmax()
    {
        // Una forma guardable nunca debe terminar apuntando a una fila mega/gmax/totem/primal.
        var strings = GameInfo.GetStrings("en");
        var db = PokemonDatabase.Instance.LoadFormSource()!.Value;
        var malas = new System.Collections.Generic.List<string>();
        for (ushort sp = 1; sp <= 1025; sp++)
        {
            var nombres = FormConverter.GetFormList(sp, strings.Types, strings.forms, GameInfo.GenderSymbolUnicode, EntityContext.Gen9);
            for (byte f = 1; f < nombres.Length; f++)
            {
                if (FormInfo.IsBattleOnlyForm(sp, f, 9)) continue;
                var id = SpeciesFormResolver.ResolveDbSpeciesId(sp, f);
                if (id == sp) continue;
                var fila = db.Forms.First(x => x.Id == id);
                if (fila.Name.Contains("-mega") || fila.Name.Contains("-gmax") || fila.Name.Contains("-totem") || fila.Name.Contains("-primal"))
                    malas.Add($"{sp}/{f} -> {fila.Name}");
            }
        }
        Assert.True(malas.Count == 0, string.Join(", ", malas));
    }

    [Fact]
    public void Resolutor_LaFilaElegidaCoincideConPKHeXEnTipos()
    {
        // Para cada forma guardable que tiene fila propia: los tipos de la DB == los de PKHeX.
        var strings = GameInfo.GetStrings("en");
        var tablas = new IPersonalTable[] { PersonalTable.ZA, PersonalTable.SV, PersonalTable.SWSH, PersonalTable.LA,
            PersonalTable.BDSP, PersonalTable.USUM, PersonalTable.SM, PersonalTable.AO, PersonalTable.XY,
            PersonalTable.B2W2, PersonalTable.BW, PersonalTable.HGSS, PersonalTable.Pt, PersonalTable.DP };
        var db = PokemonDatabase.Instance.LoadFormSource()!.Value;
        var distintas = new System.Collections.Generic.List<string>();
        for (ushort sp = 1; sp <= 1025; sp++)
        {
            var nombres = FormConverter.GetFormList(sp, strings.Types, strings.forms, GameInfo.GenderSymbolUnicode, EntityContext.Gen9);
            for (byte f = 1; f < nombres.Length; f++)
            {
                if (FormInfo.IsBattleOnlyForm(sp, f, 9)) continue;
                var id = SpeciesFormResolver.ResolveDbSpeciesId(sp, f);
                if (id == sp) continue;
                var fila = db.Forms.First(x => x.Id == id);
                var tabla = tablas.FirstOrDefault(t => t.IsPresentInGame(sp, f));
                if (tabla is null) continue;
                var info = tabla.GetFormEntry(sp, f);
                var tipos = (IPersonalType)info;
                var t1 = strings.Types[tipos.Type1].ToLowerInvariant();
                var t2 = strings.Types[tipos.Type2].ToLowerInvariant();
                var tiposDb = fila.Types.Count > 1 ? (fila.Types[0], fila.Types[1]) : (fila.Types[0], fila.Types[0]);
                if (tiposDb != (t1, t2))
                    distintas.Add($"{sp}/{f} {fila.Name}: tipos DB={tiposDb} PKHeX=({t1},{t2})");
            }
        }
        Assert.True(distintas.Count == 0, string.Join("; ", distintas));
    }

    // ── Cambio de forma en un Pokémon existente (Fase 1) ──────────────────

    [Theory]
    [InlineData(GameVersion.ZA, 678)]   // Meowstic: en Z-A la hembra NO aprende lo mismo que el macho
    [InlineData(GameVersion.SW, 678)]
    [InlineData(GameVersion.SL, 678)]
    public void ApplyForm_MeowsticHembra_NoDejaMovimientosInvalidos(GameVersion version, int species)
    {
        var pkm = ServiceFor(BlankSave(version)).BuildPokemon(species);

        PokemonService.ApplyForm(pkm, 1);

        Assert.Equal(1, (int)pkm.Gender);
        var legalidad = new LegalityAnalysis(pkm);
        Assert.True(!legalidad.Info.Moves.Any(m => !m.Valid), "Movimientos inválidos tras cambiar a hembra:\n" + legalidad.Report());
    }

    // ── 2) Creación ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(GameVersion.SN, 105, 1)]   // Marowak de Alola — el caso que originó esta fase
    [InlineData(GameVersion.SN, 103, 1)]   // Exeggutor de Alola — evolución: TryEvolveForward tiene que elegir la rama de Alola
    [InlineData(GameVersion.SN, 26, 1)]    // Raichu de Alola (Piedra Trueno, no la rama Kanto)
    [InlineData(GameVersion.SW, 52, 2)]    // Meowth de Galar (índice de forma 2, no 1: el 1 es Alola)
    [InlineData(GameVersion.SW, 80, 2)]    // Slowbro de Galar
    [InlineData(GameVersion.PLA, 157, 1)]  // Typhlosion de Hisui
    [InlineData(GameVersion.PLA, 706, 1)]  // Goodra de Hisui (cadena de tres etapas)
    [InlineData(GameVersion.SL, 128, 3)]   // Tauros Paldea Aqua
    [InlineData(GameVersion.SL, 194, 1)]   // Wooper de Paldea
    public void BuildPokemon_ConFormaRegional_DevuelveEsaFormaYSaleLegal(GameVersion version, int species, int form)
    {
        var pkm = ServiceFor(BlankSave(version)).BuildPokemon(species, (byte)form);

        Assert.Equal(species, (int)pkm.Species);
        Assert.Equal(form, (int)pkm.Form);

        var legalidad = new LegalityAnalysis(pkm);
        Assert.True(legalidad.Valid, "Salió ilegal:\n" + legalidad.Report());
    }

    [Theory]
    [InlineData(GameVersion.SN, 105)]
    [InlineData(GameVersion.SW, 52)]
    [InlineData(GameVersion.SL, 128)]
    public void BuildPokemon_SinForma_SigueDevolviendoLaFormaBase(GameVersion version, int species)
    {
        // Regresión: el parámetro nuevo tiene default 0 y NO debe cambiar lo de siempre.
        var pkm = ServiceFor(BlankSave(version)).BuildPokemon(species);

        Assert.Equal(species, (int)pkm.Species);
        Assert.Equal(0, (int)pkm.Form);
    }
}
