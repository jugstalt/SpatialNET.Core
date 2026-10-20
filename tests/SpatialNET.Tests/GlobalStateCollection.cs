namespace SpatialNET.Tests;

/// <summary>
/// Tests, die statischen Zustand wie <see cref="GeometryFactory.Default"/> oder
/// <see cref="Precision.DefaultGridSize"/> verändern, laufen nicht parallel zu anderen Tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalStateCollection
{
    public const string Name = "Globaler Zustand";
}
