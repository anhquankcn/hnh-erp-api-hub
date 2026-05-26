using System.Globalization;

namespace ERPApiHub.Domain;

public static class UlidGenerator
{
    public static string Generate()
    {
        return System.Ulid.NewUlid().ToString();
    }
}
