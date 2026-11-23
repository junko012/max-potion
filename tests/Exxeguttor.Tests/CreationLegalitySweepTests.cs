using System.Reflection;
using Exxeguttor.App.Services;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// Barrido de "Crear Pokémon": para cada juego, crea TODAS las especies que ofrece el selector
// (CreatableSpecies.HiddenInCreationPicker) con BuildPokemon y comprueba que salgan del formato del
// save (si no, SetBoxSlot lanza "PKM Format needs to be PA9") y legales según PKHeX.
// Los casos que PKHeX 25.11.07 no puede validar (Z-A: Mewtwo, Hoopa, Volcanion, Espeon, Umbreon,
// Pupitar y las formas de Hisui de Sliggoo/Goodra) NO se ofrecen en el selector (ver
// CreatableSpecies.ZaUnlegalizable y RegionalFormCatalog.NotObtainable), así que cualquier ilegal
// que aparezca acá es una regresión nueva. También se barren las formas regionales ofrecidas.
public class CreationLegalitySweepTests
{
    public static IEnumerable<object[]> Games() =>
        new[]
        {
            GameVersion.ZA, GameVersion.SL, GameVersion.SW, GameVersion.BD, GameVersion.PLA, GameVersion.GP,
            GameVersion.RD, GameVersion.GD, GameVersion.C, GameVersion.R, GameVersion.E, GameVersion.FR,
            GameVersion.D, GameVersion.Pt, GameVersion.HG, GameVersion.B, GameVersion.B2, GameVersion.X,
            GameVersion.OR, GameVersion.SN, GameVersion.US,
        }.Select(g => new object[] { g });

    [Theory]
    [MemberData(nameof(Games))]
    public void TodasLasEspeciesCreables_SalenDelFormatoDelSave_YLegales(GameVersion game)
    {
        var save = BlankSaveFile.Get(game, "TEST", LanguageID.English);
        var saveService = new SaveFileService();
        typeof(SaveFileService).GetField("_currentSave", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(saveService, save);
        var service = new PokemonService(saveService);

        var hidden = CreatableSpecies.HiddenInCreationPicker(save);
                var problems = new List<string>();

        for (var species = 1; species <= Math.Min((int)save.MaxSpeciesID, 1025); species++)
        {
            if (hidden.Contains(species)) continue;

            var ok = false;
            var why = "";
            for (var attempt = 0; attempt < 3 && !ok; attempt++)
            {
                var pk = service.BuildPokemon(species);
                if (pk.GetType() != save.PKMType) { why = $"formato {pk.GetType().Name} != {save.PKMType.Name}"; continue; }
                var la = new LegalityAnalysis(pk);
                ok = la.Valid;
                if (!ok) why = string.Join("; ", la.Results.Where(r => !r.Valid).Select(r => $"{r.Identifier}:{r.Result}").Take(3));
            }
            if (!ok) problems.Add($"{species} {(Species)species}: {why}");
        }

        Assert.True(problems.Count == 0, $"{game}: especies creables que salen ilegales: " + string.Join(" | ", problems));
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void TodasLasFormasRegionalesCreables_SalenDelFormatoDelSave_YLegales(GameVersion game)
    {
        var save = BlankSaveFile.Get(game, "TEST", LanguageID.English);
        var saveService = new SaveFileService();
        typeof(SaveFileService).GetField("_currentSave", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(saveService, save);
        var service = new PokemonService(saveService);

        var problems = new List<string>();
        foreach (var rf in RegionalFormCatalog.AvailableIn(save))
        {
            var ok = false;
            var why = "";
            for (var attempt = 0; attempt < 3 && !ok; attempt++)
            {
                var pk = service.BuildPokemon(rf.Species, rf.Form);
                if (pk.GetType() != save.PKMType) { why = $"formato {pk.GetType().Name} != {save.PKMType.Name}"; continue; }
                var la = new LegalityAnalysis(pk);
                ok = la.Valid && pk.Form == rf.Form;
                if (!ok) why = $"forma={pk.Form} " + string.Join("; ", la.Results.Where(r => !r.Valid).Select(r => $"{r.Identifier}:{r.Result}").Take(3));
            }
            if (!ok) problems.Add($"{rf.Species} {(Species)rf.Species} f{rf.Form}: {why}");
        }

        Assert.True(problems.Count == 0, $"{game}: formas regionales creables que salen ilegales: " + string.Join(" | ", problems));
    }

    [Fact]
    public void ZA_NoOfreceLosCasosQuePKHeXNoPuedeValidar()
    {
        var save = BlankSaveFile.Get(GameVersion.ZA, "TEST", LanguageID.English);
        var hidden = CreatableSpecies.HiddenInCreationPicker(save);
        foreach (var species in new[] { 150, 196, 197, 247, 720, 721 })
            Assert.Contains(species, hidden);
        var forms = RegionalFormCatalog.AvailableIn(save);
        Assert.DoesNotContain(forms, f => f.Species is 705 or 706 && f.Form == 1);
    }

    // El combo "Forma" del editor nunca debe ofrecer una forma que deje ilegal al Pokémon recién creado.
    [Theory]
    [MemberData(nameof(Games))]
    public void ComboDeFormas_NoOfreceFormasQueDejanIlegalAlPokemon(GameVersion game)
    {
        var save = BlankSaveFile.Get(game, "TEST", LanguageID.English);
        var saveService = new SaveFileService();
        typeof(SaveFileService).GetField("_currentSave", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(saveService, save);
        var service = new PokemonService(saveService);
        var strings = GameInfo.GetStrings("en");
        var hidden = CreatableSpecies.HiddenInCreationPicker(save);

        var problems = new List<string>();
        for (var species = 1; species <= Math.Min((int)save.MaxSpeciesID, 1025); species++)
        {
            if (hidden.Contains(species)) continue;
            var pk = service.BuildPokemon(species);
            if (!new LegalityAnalysis(pk).Valid) continue;   // lo cubre el otro barrido
            var forms = PokemonService.GetSelectableForms(pk, strings);
            if (forms == null) continue;
            foreach (var (index, name) in forms)
            {
                if (index == pk.Form) continue;
                var candidate = pk.Clone();
                PokemonService.ApplyForm(candidate, index);
                candidate.RefreshChecksum();
                var la = new LegalityAnalysis(candidate);
                if (!la.Valid)
                    problems.Add($"{species} {(Species)species} f{index} '{name}': " +
                        string.Join("; ", la.Results.Where(r => !r.Valid).Select(r => $"{r.Identifier}:{r.Result}").Take(3)));
            }
        }

        Assert.True(problems.Count == 0, $"{game}: formas del combo que salen ilegales: " + string.Join(" | ", problems));
    }

    // Cada forma alternativa que el selector ofrece como tarjeta propia (AlternateFormCatalog) debe salir legal.
    [Theory]
    [MemberData(nameof(Games))]
    public void TodasLasFormasAlternativasCreables_SalenDelFormatoDelSave_YLegales(GameVersion game)
    {
        var save = BlankSaveFile.Get(game, "TEST", LanguageID.English);
        var saveService = new SaveFileService();
        typeof(SaveFileService).GetField("_currentSave", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(saveService, save);
        var service = new PokemonService(saveService);

        var problems = new List<string>();
        foreach (var (species, form) in AlternateFormCatalog.AvailableIn(game))
        {
            var ok = false;
            var why = "";
            for (var attempt = 0; attempt < 3 && !ok; attempt++)
            {
                var pk = service.BuildPokemon(species, form);
                if (pk.GetType() != save.PKMType) { why = $"formato {pk.GetType().Name} != {save.PKMType.Name}"; continue; }
                var la = new LegalityAnalysis(pk);
                ok = la.Valid && pk.Form == form;
                if (!ok) why = $"forma={pk.Form} " + string.Join("; ", la.Results.Where(r => !r.Valid).Select(r => $"{r.Identifier}:{r.Result}").Take(3));
            }
            if (!ok) problems.Add($"{species} {(Species)species} f{form}: {why}");
        }

        Assert.True(problems.Count == 0, $"{game}: formas alternativas creables que salen ilegales: " + string.Join(" | ", problems));
    }
}
