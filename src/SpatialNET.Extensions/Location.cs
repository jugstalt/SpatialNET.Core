namespace SpatialNET;

/// <summary>The position of a point relative to a geometry (ADR 0022).</summary>
public enum Location
{
    /// <summary>The point is not in the geometry.</summary>
    Outside = 0,

    /// <summary>The point is on the boundary: on a ring of a polygon, or on an end point of a polyline (mod-2 rule, ADR 0013).</summary>
    Boundary = 1,

    /// <summary>The point is in the interior: in the area of a polygon, on a polyline away from its end points, or a point of the geometry.</summary>
    Inside = 2,
}
