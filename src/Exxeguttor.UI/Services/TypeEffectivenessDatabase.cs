using Exxeguttor.App.Services;
using Exxeguttor.UI.ViewModels;

namespace Exxeguttor.UI.Services;

/// <summary>
/// Calcula la tabla de efectividad de tipos para un Pokémon.
///
/// La tabla TypeEffectiveness solo almacena las excepciones al factor 1
/// (filas con Multiplier 0.0, 0.5 o 2.0). Todo lo que no está en la
/// tabla tiene factor 1 implícito.
///
/// Se usan exactamente 2 queries por cálculo:
///   1. DefenseType IN (tipos propios)  → para la columna Recibes
///   2. AttackType  IN (tipos propios)  → para la columna Pegas
///
/// Para un Pokémon de doble tipo, la defensa multiplica ambos factores
/// (e.g. Acero: ×0.5 + Eléctrico: ×0.5 = ×0.25 para Tierra).
/// La ofensa se evalúa por tipo propio independientemente.
/// </summary>
public class TypeEffectivenessDatabase
{
    private static TypeEffectivenessDatabase? _instance;
    public static TypeEffectivenessDatabase Instance => _instance ??= new TypeEffectivenessDatabase();

    private static readonly string[] AllTypes =
    [
        "normal","fire","water","electric","grass","ice",
        "fighting","poison","ground","flying","psychic","bug",
        "rock","ghost","dragon","dark","steel","fairy"
    ];

    private static readonly string[] BucketOrder = ["×4","×2","×1","×½","×¼","×0"];

    private TypeEffectivenessDatabase() { }

    /// <summary>
    /// Retorna la tabla completa de efectividad para un Pokémon de
    /// uno o dos tipos. Cada fila: Factor | Recibes | Pegas.
    /// Siempre devuelve los 6 factores (×4, ×2, ×1, ×½, ×¼, ×0), tengan o no
    /// tipos asociados: una fila sin tipos queda vacía y la vista muestra "—".
    /// </summary>
    public List<TypeEffectivenessEntry> GetMatchups(string type1, string type2 = "")
    {
        var t1 = type1.ToLowerInvariant();
        var t2 = type2.ToLowerInvariant();
        bool isDual = !string.IsNullOrEmpty(t2) && t1 != t2;

        var ownTypes = isDual ? new[] { t1, t2 } : new[] { t1 };
        var db = PokemonDatabase.Instance;

        // ── Query 1: defensa ─────────────────────────────────────────
        // Excepciones donde algo ataca a nuestros tipos
        // Clave: attackType → {t1: mult, t2: mult}
        var defExceptions = db.GetDefenseMatchupsForTypes(ownTypes);

        // Para cada tipo atacante calcula el multiplicador compuesto
        var defMultipliers = new Dictionary<string, double>();
        foreach (var attackType in AllTypes)
        {
            double mult = 1.0;
            foreach (var ownType in ownTypes)
            {
                if (defExceptions.TryGetValue((attackType, ownType), out var m))
                    mult *= m;
                // Si no existe en la tabla → factor 1, no multiplica
            }
            defMultipliers[attackType] = mult;
        }

        // ── Query 2: ofensa ──────────────────────────────────────────
        // Excepciones donde nuestros tipos atacan a otros
        var offExceptions = db.GetOffenseMatchupsForTypes(ownTypes);

        // Por tipo propio: qué tipos defienden con excepción
        // offByOwnType[ownType][defType] = mult
        var offByOwnType = new Dictionary<string, Dictionary<string, double>>();
        foreach (var ownType in ownTypes)
            offByOwnType[ownType] = new Dictionary<string, double>();

        foreach (var ((ownType, defType), mult) in offExceptions)
            if (offByOwnType.TryGetValue(ownType, out var d))
                d[defType] = mult;

        // ── Agrupar en buckets ────────────────────────────────────────
        var defGroups  = BucketOrder.ToDictionary(b => b, _ => new List<string>());
        // Ofensa: una lista por bucket para cada POSICIÓN de tipo propio (0 = t1, 1 = t2 si es
        // dual) — antes se armaba un string combinado "Fire: Grass, Bug" y la vista lo
        // reparseaba por nombre; ahora la vista recibe directamente la lista ya separada por
        // tipo propio (ver TypeEffectivenessEntry.OffenseTargets1/2).
        var offGroups1 = BucketOrder.ToDictionary(b => b, _ => new List<string>());
        var offGroups2 = BucketOrder.ToDictionary(b => b, _ => new List<string>());

        // Defensa: agrupar tipos atacantes por multiplicador compuesto
        foreach (var (attackType, mult) in defMultipliers)
        {
            var bucket = MultToBucket(mult);
            if (bucket != null)
                defGroups[bucket].Add(Capitalize(attackType));
        }

        // Ofensa: para cada tipo propio (por posición: 0 = t1, 1 = t2), agrupar tipos
        // defensores por bucket.
        for (var i = 0; i < ownTypes.Length; i++)
        {
            var ownType = ownTypes[i];
            var target  = i == 0 ? offGroups1 : offGroups2;

            foreach (var defType in AllTypes)
            {
                double mult = offByOwnType[ownType].TryGetValue(defType, out var m) ? m : 1.0;
                var bucket = MultToBucket(mult);
                if (bucket != null)
                    target[bucket].Add(Capitalize(defType));
            }
        }

        // ── Construir filas ───────────────────────────────────────────
        var result = new List<TypeEffectivenessEntry>();
        foreach (var bucket in BucketOrder)
        {
            var defStr = string.Join(", ", defGroups[bucket]);
            var off1   = offGroups1[bucket];
            var off2   = offGroups2[bucket];

            result.Add(new TypeEffectivenessEntry(bucket, defStr, off1, off2, isDual));
        }
        return result;
    }

    /// <summary>Mantiene la API anterior para código existente.</summary>
    public List<TypeEffectivenessEntry> GetWeaknesses(string type1, string type2 = "")
        => GetMatchups(type1, type2);

    private static string? MultToBucket(double mult) => mult switch
    {
        >= 3.9  => "×4",
        >= 1.9  => "×2",
        >= 0.9  => "×1",
        >= 0.26 => "×½",
        > 0.0   => "×¼",
        0.0     => "×0",
        _       => null
    };

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
