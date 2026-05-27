using System.Globalization;

namespace ERPApiHub.Domain;

public static class UlidGenerator
{
    /// <summary>
    /// Generates a new identifier.
    /// Currently uses GUID as fallback until NetUlid package compatibility is resolved.
    /// TODO: Migrate to proper ULID implementation (NetUlid or Ulid NuGet package)
    /// </summary>
    public static string Generate()
    {
        // Using Guid as temporary replacement for ULID
        // ULID provides sortable, lexicographically ordered identifiers
        // Guid is random and not sortable, but serves the same uniqueness purpose
        return Guid.NewGuid().ToString("N"); // 32 chars, no dashes
    }
}
