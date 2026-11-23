using Exxeguttor.App.Services;
using Xunit;

namespace Exxeguttor.Tests;

// En Gen 1-3 la clase de daño depende del TIPO del movimiento, no del movimiento.
public class LegacyDamageClassTests
{
    [Theory]
    [InlineData("special",  "normal",   1, "physical")] // Hyper Beam en Gen 1: Normal → Físico
    [InlineData("special",  "normal",   3, "physical")]
    [InlineData("physical", "fire",     2, "special")]  // Fire Punch en Gen 2: Fuego → Especial
    [InlineData("physical", "Dark",     2, "special")]
    [InlineData("special",  "ghost",    3, "physical")] // Shadow Ball en Gen 3: Fantasma → Físico
    [InlineData("status",   "fire",     1, "status")]   // los de estado no cambian
    [InlineData("special",  "normal",   4, "special")]  // Gen 4+: la clase propia del movimiento
    [InlineData("physical", "fire",     8, "physical")]
    public void Resuelve_la_clase_segun_la_generacion(string db, string type, int gen, string expected)
        => Assert.Equal(expected, LegacyDamageClass.Resolve(db, type, gen));
}
