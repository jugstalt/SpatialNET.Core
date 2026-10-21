using System.Runtime.CompilerServices;
using CsCheck;

namespace SpatialNET.Tests;

/// <summary>
/// Lets CI raise the number of iterations of the property-based tests (CsCheck) without code changes:
/// set <c>SPATIALNET_PROPERTY_ITER</c>. Tests that pass an explicit <c>iter</c> keep their own count.
/// </summary>
internal static class PropertyTestSettings
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("SPATIALNET_PROPERTY_ITER"), out var iterations) && iterations > 0)
        {
            Check.Iter = iterations;
        }
    }
}
