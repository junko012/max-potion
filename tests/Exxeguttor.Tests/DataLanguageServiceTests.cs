using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

// Plan de idiomas, Fases 0–1 (docs/i18n_project/i18n-plan.md): preferencias persistentes y
// resolución del idioma de los DATOS del juego. Sin compilar en el sandbox: Juan corre
//   dotnet test --filter "DataLanguageServiceTests|AppSettingsServiceTests"
public class AppSettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "maxpotion-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void SinArchivo_UiEsNull_YDatosEsAuto()
    {
        var s = new AppSettingsService(FilePath);
        Assert.Null(s.GetUiLocale());
        Assert.Equal("auto", s.GetDataLocale());
    }

    [Fact]
    public void Guardar_Y_Releer_ConOtraInstancia()
    {
        new AppSettingsService(FilePath).SetUiLocale("fr");
        new AppSettingsService(FilePath).SetDataLocale("ja");
        var s = new AppSettingsService(FilePath);
        Assert.Equal("fr", s.GetUiLocale());
        Assert.Equal("ja", s.GetDataLocale());
    }

    [Fact]
    public void Escribir_Una_Clave_NoPisaLaOtra_NiLasDesconocidas()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"uiLocale\": \"de\", \"futuro\": 42 }");
        new AppSettingsService(FilePath).SetDataLocale("ko");
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"uiLocale\"", json);
        Assert.Contains("\"futuro\"", json);
        Assert.Equal("de", new AppSettingsService(FilePath).GetUiLocale());
    }

    [Fact]
    public void JsonCorrupto_NoLanza_ComportaComoVacio()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ esto no es json");
        var s = new AppSettingsService(FilePath);
        Assert.Null(s.GetUiLocale());
        s.SetUiLocale("es"); // la escritura reemplaza el archivo roto
        Assert.Equal("es", new AppSettingsService(FilePath).GetUiLocale());
    }
}

public class DataLanguageServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "maxpotion-tests-" + Guid.NewGuid().ToString("N"));
    private AppSettingsService Settings => new(Path.Combine(_dir, "settings.json"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Theory]
    [InlineData("es", "es")]
    [InlineData("es-PE", "es")]
    [InlineData("en", "en")]
    [InlineData("fr", "fr")]
    [InlineData("de", "de")]
    [InlineData("it", "it")]
    [InlineData("ja", "ja")]
    [InlineData("ko", "ko")]
    [InlineData("zh-CN", "zh-Hans")]   // regresión del bug: antes "zh", que PKHeX no reconoce
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("es-419", "es-419")]
    public void MapUiToPkhex_IdiomasConDatos(string ui, string esperado)
        => Assert.Equal(esperado, DataLanguageService.MapUiToPkhex(ui));

    [Theory]
    [InlineData("ru")] [InlineData("ar")] [InlineData("hi")] [InlineData("id")] [InlineData("pt-BR")] [InlineData("")]
    public void MapUiToPkhex_SinDatosEnPKHeX_EsNull(string ui)
        => Assert.Null(DataLanguageService.MapUiToPkhex(ui));

    [Fact]
    public void Auto_SigueALaUi_YCaeAIngles_SiPKHeXNoLoTiene()
    {
        var d = new DataLanguageService(Settings);
        Assert.True(d.IsAuto);
        d.SetUiLocale("fr");
        Assert.Equal("fr", d.PkhexCode);
        Assert.False(d.FallsBackToEnglish);
        d.SetUiLocale("ru");
        Assert.Equal("en", d.PkhexCode);
        Assert.True(d.FallsBackToEnglish);
    }

    [Fact]
    public void Explicito_QuedaFijo_YNoSigueALaUi()
    {
        var d = new DataLanguageService(Settings);
        d.SetPreference("ja");
        d.SetUiLocale("es");
        Assert.Equal("ja", d.PkhexCode);
        Assert.False(d.FallsBackToEnglish);
    }

    [Fact]
    public void Preferencia_Persiste_YUnaInvalidaSeIgnora()
    {
        new DataLanguageService(Settings).SetPreference("ko");
        Assert.Equal("ko", new DataLanguageService(Settings).Preference);

        var d = new DataLanguageService(Settings);
        d.SetPreference("klingon");          // inválida: se ignora
        Assert.Equal("ko", d.Preference);
        d.SetPreference(DataLanguageService.Auto);
        Assert.Equal("auto", new DataLanguageService(Settings).Preference);
    }

    [Fact]
    public void PreferenciaGuardadaInvalida_VuelveAAuto()
    {
        Settings.SetDataLocale("zh");        // "zh" suelto no existe en PKHeX
        Assert.True(new DataLanguageService(Settings).IsAuto);
    }

    [Fact]
    public void Changed_SoloSeDisparaSiCambiaElCodigoActivo()
    {
        var d = new DataLanguageService(Settings);
        d.SetUiLocale("es");
        var n = 0; d.Changed += () => n++;
        d.SetUiLocale("es");                  // mismo idioma: nada
        d.SetUiLocale("ru"); d.SetUiLocale("ar"); // en→en entre ru y ar: un solo cambio (es→en)
        Assert.Equal(1, n);
        d.SetPreference("de");
        Assert.Equal(2, n);
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("de")]
    [InlineData("ja")] [InlineData("ko")] [InlineData("zh-Hans")] [InlineData("zh-Hant")]
    public void CadaIdiomaSeleccionable_TieneListasDeNombresDistintasDeInglesVacio(string code)
    {
        var d = new DataLanguageService(Settings);
        d.SetPreference(code);
        var strings = d.Strings;
        Assert.False(string.IsNullOrEmpty(strings.Species[25]));  // Pikachu
        Assert.False(string.IsNullOrEmpty(strings.Move[1]));      // Pound
        if (code != "en")
            Assert.NotEqual(PKHeX.Core.GameInfo.GetStrings("en").Move[1], strings.Move[1]);
    }
}

// ApplyToPkhexGlobals toca estado global del proceso (GameInfo): se restaura siempre al terminar.
[Collection("PKHeXGlobals")]
public class DataLanguagePkhexGlobalsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "maxpotion-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _langBefore = PKHeX.Core.GameInfo.CurrentLanguage;
    private readonly PKHeX.Core.GameStrings _stringsBefore = PKHeX.Core.GameInfo.Strings;

    public void Dispose()
    {
        PKHeX.Core.GameInfo.CurrentLanguage = _langBefore;
        PKHeX.Core.GameInfo.Strings = _stringsBefore;
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void ApplyToPkhexGlobals_AlineaElIdiomaGlobal_YLasUbicaciones()
    {
        var d = new DataLanguageService(new AppSettingsService(Path.Combine(_dir, "settings.json")));
        d.SetPreference("es");
        d.ApplyToPkhexGlobals();
        Assert.Equal("es", PKHeX.Core.GameInfo.CurrentLanguage);
        Assert.Equal(PKHeX.Core.GameInfo.GetStrings("es").Types[9], PKHeX.Core.GameInfo.Strings.Types[9]);

        // Platino (Gen 4): las ubicaciones de captura vienen del global — con "es" deben diferir de "en".
        var es = PKHeX.Core.GameInfo.GetLocationList(PKHeX.Core.GameVersion.Pt, PKHeX.Core.EntityContext.Gen4, egg: false)
            .Select(l => l.Text).ToList();
        d.SetPreference("en");
        d.ApplyToPkhexGlobals();
        var en = PKHeX.Core.GameInfo.GetLocationList(PKHeX.Core.GameVersion.Pt, PKHeX.Core.EntityContext.Gen4, egg: false)
            .Select(l => l.Text).ToList();
        Assert.NotEmpty(es);
        Assert.NotEqual(en, es);
    }
}
