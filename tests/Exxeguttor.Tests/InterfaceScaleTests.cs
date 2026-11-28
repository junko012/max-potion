using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

// Escala (zoom) de la interfaz: pasos, Snap/Next/Previous, formato y persistencia en settings.json.
// Sin compilar en el sandbox: corre en tu PC con
//   dotnet test --filter "InterfaceScaleTests"
public class InterfaceScaleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "maxpotion-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.2, 1.25)]
    [InlineData(0.1, 0.9)]
    [InlineData(5.0, 2.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 2.0)]
    public void Snap_DevuelveElPasoMasCercano(double input, double expected)
        => Assert.Equal(expected, InterfaceScale.Snap(input));

    [Fact]
    public void Next_AvanzaPorLosPasos_YSeDetieneEnElMaximo()
    {
        Assert.Equal(1.1, InterfaceScale.Next(1.0));
        Assert.Equal(1.25, InterfaceScale.Next(1.1));
        Assert.Equal(2.0, InterfaceScale.Next(1.75));
        Assert.Equal(2.0, InterfaceScale.Next(2.0));
    }

    [Fact]
    public void Previous_RetrocedePorLosPasos_YSeDetieneEnElMinimo()
    {
        Assert.Equal(1.0, InterfaceScale.Previous(1.1));
        Assert.Equal(0.9, InterfaceScale.Previous(1.0));
        Assert.Equal(0.9, InterfaceScale.Previous(0.9));
    }

    [Fact]
    public void Format_MuestraPorcentajeEntero()
    {
        Assert.Equal("100 %", InterfaceScale.Format(1.0));
        Assert.Equal("125 %", InterfaceScale.Format(1.25));
        Assert.Equal("90 %", InterfaceScale.Format(0.9));
    }

    [Fact]
    public void SinArchivo_LaEscalaEs100()
        => Assert.Equal(1.0, new AppSettingsService(FilePath).GetUiScale());

    [Fact]
    public void Guardar_Y_Releer_ConOtraInstancia()
    {
        new AppSettingsService(FilePath).SetUiScale(1.5);
        Assert.Equal(1.5, new AppSettingsService(FilePath).GetUiScale());
    }

    [Fact]
    public void GuardarEscala_NoPisaIdiomas_NiClavesDesconocidas()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"uiLocale\": \"de\", \"futuro\": 42 }");
        new AppSettingsService(FilePath).SetUiScale(1.25);
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"uiLocale\"", json);
        Assert.Contains("\"futuro\"", json);
        var s = new AppSettingsService(FilePath);
        Assert.Equal("de", s.GetUiLocale());
        Assert.Equal(1.25, s.GetUiScale());
    }

    [Fact]
    public void ValorQueNoEsNumero_SeIgnora()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"uiScale\": \"grande\" }");
        Assert.Equal(1.0, new AppSettingsService(FilePath).GetUiScale());
    }

    [Fact]
    public void ValorFueraDeRango_SeAjustaAlPasoMasCercano()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"uiScale\": 9 }");
        Assert.Equal(2.0, new AppSettingsService(FilePath).GetUiScale());
    }
}
