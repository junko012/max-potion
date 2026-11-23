using System;
using System.Linq;
using System.Reflection;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// ARCHIVO TEMPORAL DE DIAGNÓSTICO — no es parte de la suite real.
// Objetivo: ver qué trae "de fábrica" EntityBlank.GetBlank(ITrainerInfo) y
// EntityBlank.GetIdealBlank(species, form) antes de decidir qué campos hay que
// setear nosotros a mano al crear un Pokémon nuevo.
// Correr con: dotnet test --filter DiagnosticarEntityBlank
public class EntityBlankDiagnosticTests
{
    [Fact]
    public void DiagnosticarEntityBlank()
    {
        var save = BlankSaveFile.Get(GameVersion.E, "TEST", LanguageID.English);

        string DumpProps(PKM pkm, string label)
        {
            var lines = pkm.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                .Where(p =>
                {
                    var n = p.Name;
                    return n.Contains("Species") || n.Contains("Nickname") || n.Contains("Nature")
                        || n.Contains("Ability") || n.Contains("OT_") || n.Contains("OriginalTrainer")
                        || n == "TID16" || n == "SID16" || n == "PID" || n == "EncryptionConstant"
                        || n.StartsWith("Move") || n.Contains("Level") || n.Contains("Met")
                        || n == "Version" || n == "Language" || n == "Ball" || n.StartsWith("IV_")
                        || n.StartsWith("EV_") || n.Contains("Checksum") || n.Contains("Valid")
                        || n.Contains("Gender") || n.Contains("PersonalityValue") || n.Contains("Form");
                })
                .OrderBy(p => p.Name)
                .Select(p =>
                {
                    object? val;
                    try { val = p.GetValue(pkm); }
                    catch (Exception ex) { val = $"<throw: {ex.GetType().Name}>"; }
                    return $"  {p.Name} ({p.PropertyType.Name}) = {val}";
                });
            return $"── {label} ──\n" + string.Join("\n", lines);
        }

        var resultado = new System.Text.StringBuilder();

        try
        {
            var blank1 = EntityBlank.GetBlank(save);
            resultado.AppendLine(DumpProps(blank1, "EntityBlank.GetBlank(save) [ITrainerInfo]"));
        }
        catch (Exception ex)
        {
            resultado.AppendLine($"GetBlank(save) FALLÓ: {ex}");
        }

        resultado.AppendLine();

        try
        {
            var blank2 = EntityBlank.GetIdealBlank(4, 0); // Charmander, form 0
            resultado.AppendLine(DumpProps(blank2, "EntityBlank.GetIdealBlank(4, 0) [Charmander]"));
        }
        catch (Exception ex)
        {
            resultado.AppendLine($"GetIdealBlank(4, 0) FALLÓ: {ex}");
        }

        Assert.Fail(resultado.ToString());
    }
}
