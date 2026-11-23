using System.IO.Enumeration;
using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

// Los saves de Gen 6 en adelante (3DS y Switch) se llaman "main", sin extensión. Avalonia 11.3.8 envía FilePickerFileTypes.All al
// selector GTK como el patrón "*.*" (exige un punto en el nombre) y el tipo MIME "*/*" (no es un tipo real), así que esos saves no
// aparecían ni marcando "All". Además el commit c92d486 había quitado el "*" que antes los cubría en el filtro de saves.
public class SaveFilePatternsTests
{
    // Mismo criterio de coincidencia que un glob de selector de archivos: "*" y "?" sobre el nombre, sin distinguir carpeta
    private static bool Matches(IEnumerable<string> patterns, string fileName) =>
        patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, fileName, ignoreCase: false));

    [Theory]
    [InlineData("main")]
    [InlineData("MAIN")]
    public void ElFiltroDeSaves_MuestraLosSavesSinExtensionDeGen6EnAdelante(string nombre)
    {
        Assert.True(Matches(SaveFilePatterns.Saves(), nombre), nombre);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("00000001")]
    [InlineData("savedata")]
    [InlineData("Pokemon X.sav")]
    [InlineData("export.DSV")]
    [InlineData("a.b.c")]
    public void TodosLosArchivos_MuestraTodo_IncluidosLosQueNoTienenExtension(string nombre)
    {
        Assert.True(Matches(SaveFilePatterns.AllFiles(), nombre), nombre);
    }

    [Fact]
    public void AvaloniaAll_ConElPatronPuntoAsteriscoPunto_OcultaLosSavesSinExtension_ElDefectoReportado()
    {
        // Caracterización del defecto: así llegaba "All" al selector (Avalonia FilePickerFileTypes.All → Patterns = "*.*")
        var avaloniaAll = new[] { "*.*" };
        Assert.False(Matches(avaloniaAll, "main"), "con *.* un nombre sin punto no coincide: por eso 'main' no se veía");
        Assert.True(Matches(avaloniaAll, "Pokemon X.sav"));
    }

    [Theory]
    [InlineData("partida.sav")]
    [InlineData("partida.SAV")]
    [InlineData("Pokemon - Edicion Platino.dsv")]
    [InlineData("export_d.DSV")]
    [InlineData("pk.bak")]
    public void ElFiltroDeSaves_ConservaLasExtensionesConocidasEnAmbasCapitalizaciones(string nombre)
    {
        Assert.True(Matches(SaveFilePatterns.Saves(), nombre), nombre);
    }

    [Theory]
    [InlineData("notas.txt")]
    [InlineData("foto.png")]
    [InlineData("leeme")]
    public void ElFiltroDeSaves_SigueSiendoSelectivo_NoMuestraCualquierArchivo(string nombre)
    {
        Assert.False(Matches(SaveFilePatterns.Saves(), nombre), nombre);
    }
}
