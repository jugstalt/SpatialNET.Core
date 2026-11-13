namespace SpatialNET.Network;

/// <summary>How the value of a weight and the length of an edge turn into the cost of the edge (ADR 0049).</summary>
public enum WeightKind
{
    /// <summary>Cost = length; the value is not used.</summary>
    Length,

    /// <summary>Cost = value, whatever the length, for example a fixed time for a ferry.</summary>
    Absolute,

    /// <summary>Cost = value × length, for example a resistance per kilometre.</summary>
    PerLength,

    /// <summary>Cost = length ÷ value, for example a travel time from a speed; the value must be greater than 0.</summary>
    LengthOverValue,
}

/// <summary>
/// A weight that a search can use (ADR 0049). The values are on the edges (<see cref="GraphEdge{TNodeId, TEdgeId}.Weights"/>);
/// the cost of a path is the sum of the costs of its edges. Units are converted by the host.
/// </summary>
/// <param name="Id">The id that the values on the edges refer to.</param>
/// <param name="Name">The name, for messages.</param>
/// <param name="Kind">How a value turns into a cost.</param>
/// <param name="Fallback">
/// The value for an edge without a value of this weight; without a fallback such an edge is an
/// <see cref="InvalidGraphException"/> when a search reaches it.
/// </param>
public sealed record WeightDefinition(int Id, string Name, WeightKind Kind, double? Fallback = null)
{
    /// <summary>The built-in weight "length": the cost of an edge is its length.</summary>
    public static WeightDefinition Length { get; } = new(0, "Length", WeightKind.Length);
}
