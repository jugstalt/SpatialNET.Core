using System.Text.Json;

namespace SpatialNET.Playground.Algorithms;

/// <summary>Ein Eingabe-Operand eines Algorithmus im Playground.</summary>
/// <param name="Key">Schlüssel (a, b).</param>
/// <param name="Label">Beschriftung.</param>
/// <param name="Types">Erlaubte Geometrie-Typen (Point, MultiPoint, LineString, Polygon).</param>
public sealed record InputDescriptor(string Key, string Label, string[] Types);

/// <summary>Ein Parameter eines Algorithmus im Playground.</summary>
/// <param name="Key">Schlüssel.</param>
/// <param name="Label">Beschriftung.</param>
/// <param name="Type">number, int, bool oder enum.</param>
/// <param name="Default">Standardwert (oder null).</param>
/// <param name="Unit">„kbs“ = Einheit des Rechen-KBS (ADR 0010), sonst null.</param>
/// <param name="Values">Werte für enum.</param>
public sealed record ParameterDescriptor(string Key, string Label, string Type, object? Default = null, string? Unit = null, string[]? Values = null);

/// <summary>
/// Ein im Playground angebotener Algorithmus (ADR 0011). Implementierungen werden per Assembly-Scan gefunden;
/// die UI entsteht vollständig aus diesen Deskriptoren.
/// </summary>
public interface IPlaygroundAlgorithm
{
    /// <summary>Id = Methodenname in Kleinbuchstaben vorn (z. B. <c>buffer</c>), wie die Fixture-Ordner.</summary>
    string Id { get; }

    string Name { get; }

    string Group { get; }

    IReadOnlyList<InputDescriptor> Inputs { get; }

    IReadOnlyList<ParameterDescriptor> Parameters { get; }

    /// <summary>Führt den Algorithmus aus; Ergebnis ist eine Geometrie, eine Liste von Geometrien oder ein Wert.</summary>
    object? Run(IReadOnlyDictionary<string, IGeometry2D> inputs, PlaygroundParameters parameters, IGeometryFactory factory, double? gridSize);
}

/// <summary>Parameterwerte eines Aufrufs (aus JSON).</summary>
public sealed class PlaygroundParameters(IReadOnlyDictionary<string, JsonElement> values, IReadOnlyList<ParameterDescriptor> descriptors)
{
    public double Number(string key) =>
        values.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : Convert.ToDouble(Default(key));

    public double? OptionalNumber(string key) =>
        values.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    public bool Bool(string key) =>
        values.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : Default(key) is true;

    public T Enum<T>(string key) where T : struct, System.Enum =>
        System.Enum.Parse<T>(values.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : (string)Default(key)!);

    private object? Default(string key) => descriptors.First(d => d.Key == key).Default;
}

/// <summary>Basisklasse: Deskriptor als Konstruktor-Argumente, Ausführung als Delegat.</summary>
public abstract class PlaygroundAlgorithm(
    string id, string name, string group, InputDescriptor[] inputs, ParameterDescriptor[] parameters,
    Func<IReadOnlyDictionary<string, IGeometry2D>, PlaygroundParameters, IGeometryFactory, double?, object?> run) : IPlaygroundAlgorithm
{
    public string Id => id;
    public string Name => name;
    public string Group => group;
    public IReadOnlyList<InputDescriptor> Inputs => inputs;
    public IReadOnlyList<ParameterDescriptor> Parameters => parameters;

    public object? Run(IReadOnlyDictionary<string, IGeometry2D> inputs, PlaygroundParameters parameters, IGeometryFactory factory, double? gridSize) =>
        run(inputs, parameters, factory, gridSize);

    /// <summary>Alle Algorithmen dieser Assembly (Assembly-Scan, ADR 0011).</summary>
    public static IReadOnlyList<IPlaygroundAlgorithm> All { get; } = typeof(PlaygroundAlgorithm).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlaygroundAlgorithm).IsAssignableFrom(t))
        .Select(t => (IPlaygroundAlgorithm)Activator.CreateInstance(t)!)
        .OrderBy(a => a.Group, StringComparer.Ordinal).ThenBy(a => a.Name, StringComparer.Ordinal)
        .ToList();
}
