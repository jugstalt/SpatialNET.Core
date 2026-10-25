using System.Text.Json;

namespace SpatialNET.Playground.Algorithms;

/// <summary>An input operand of an algorithm in the playground.</summary>
/// <param name="Key">The key (a, b).</param>
/// <param name="Label">The label.</param>
/// <param name="Types">The allowed geometry types (Point, MultiPoint, LineString, Polygon).</param>
/// <param name="Optional"><c>true</c> if the algorithm also runs without this input (the algorithm gets no entry for it).</param>
public sealed record InputDescriptor(string Key, string Label, string[] Types, bool Optional = false);

/// <summary>A parameter of an algorithm in the playground.</summary>
/// <param name="Key">The key.</param>
/// <param name="Label">The label.</param>
/// <param name="Type">number, int, bool or enum.</param>
/// <param name="Default">The default value (or null).</param>
/// <param name="Unit">"kbs" = the unit of the computation CRS (ADR 0010), otherwise null.</param>
/// <param name="Values">The values of an enum.</param>
public sealed record ParameterDescriptor(string Key, string Label, string Type, object? Default = null, string? Unit = null, string[]? Values = null);

/// <summary>
/// An algorithm offered in the playground (ADR 0011). Implementations are found by an assembly scan;
/// the UI is built entirely from these descriptors.
/// </summary>
public interface IPlaygroundAlgorithm
{
    /// <summary>The id: the method name with a lower-case first letter (e.g. <c>buffer</c>), like the fixture folders.</summary>
    string Id { get; }

    string Name { get; }

    string Group { get; }

    IReadOnlyList<InputDescriptor> Inputs { get; }

    IReadOnlyList<ParameterDescriptor> Parameters { get; }

    /// <summary>Runs the algorithm; the result is a geometry, a list of geometries or a value.</summary>
    object? Run(IReadOnlyDictionary<string, IGeometry2D> inputs, PlaygroundParameters parameters, IGeometryFactory factory, double? gridSize);
}

/// <summary>The parameter values of a call (from JSON).</summary>
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

/// <summary>Base class: the descriptor as constructor arguments, the execution as a delegate.</summary>
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

    /// <summary>All algorithms of this assembly (assembly scan, ADR 0011).</summary>
    public static IReadOnlyList<IPlaygroundAlgorithm> All { get; } = typeof(PlaygroundAlgorithm).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlaygroundAlgorithm).IsAssignableFrom(t))
        .Select(t => (IPlaygroundAlgorithm)Activator.CreateInstance(t)!)
        .OrderBy(a => a.Group, StringComparer.Ordinal).ThenBy(a => a.Name, StringComparer.Ordinal)
        .ToList();
}
