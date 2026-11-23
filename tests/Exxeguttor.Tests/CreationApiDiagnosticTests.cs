using System;
using System.Linq;
using System.Reflection;
using PKHeX.Core;
using Xunit;

namespace Exxeguttor.Tests;

// ARCHIVO TEMPORAL DE DIAGNÓSTICO — para la funcionalidad de "crear Pokémon".
// Junta en un solo lugar todo lo que todavía no confirmamos de la API de PKHeX 25.11.7:
// - Cómo derivar Género/Naturaleza/Habilidad a partir del PID (Gen 3-5)
// - Cómo guardar un PKM nuevo en una caja/equipo del SaveFile
// - Cómo queda expuesta la tabla de personal (para GenderRatio, abilities por especie)
public class CreationApiDiagnosticTests
{
    [Fact]
    public void DiagnosticarApiCreacion()
    {
        var save = BlankSaveFile.Get(GameVersion.E, "TEST", LanguageID.English);
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

        // 1) Métodos de PKM relacionados a género/naturaleza/habilidad/PID/checksum/legalidad sugerida
        DumpMethods(typeof(PKM), "PKM (instancia)", n =>
            n.Contains("Gender") || n.Contains("Nature") || n.Contains("Ability") ||
            n.Contains("PID") || n.Contains("Refresh") || n.Contains("Checksum") ||
            n.Contains("Suggest") || n.Contains("Random") || n.Contains("Legal"));

        // 2) Métodos de SaveFile para guardar en caja/equipo
        DumpMethods(typeof(SaveFile), "SaveFile (instancia)", n =>
            n.Contains("Box") || n.Contains("Party") || n.Contains("Slot") || n.Contains("Set"));

        // 3) Clases utilitarias sueltas en el ensamblado con "Gender"/"Random" en el nombre
        var tiposUtiles = typeof(SaveFile).Assembly.GetTypes()
            .Where(t => t.IsPublic && (t.Name.Contains("Gender") || t.Name.Contains("EntityPID") || t.Name.Contains("Randomizer")))
            .Select(t => t.FullName)
            .Distinct()
            .OrderBy(s => s);
        sb.AppendLine("── Tipos con 'Gender'/'EntityPID'/'Randomizer' en el ensamblado ──");
        sb.AppendLine(string.Join("\n", tiposUtiles));
        sb.AppendLine();

        // 4) PersonalInfo de la especie 4 (Charmander) en este save — Gender/Abilities
        try
        {
            var pi = save.Personal[4];
            DumpMethods(pi.GetType(), "PersonalInfo (instancia, especie 4)", n =>
                n.Contains("Gender") || n.Contains("Abilit"));
        }
        catch (Exception ex)
        {
            sb.AppendLine($"save.Personal[4] FALLÓ: {ex}");
        }

        Assert.Fail(sb.ToString());
    }
}
