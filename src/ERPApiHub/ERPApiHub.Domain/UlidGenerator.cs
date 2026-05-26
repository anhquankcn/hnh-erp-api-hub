using NetUlid;

namespace ERPApiHub.Domain;

public static class UlidGenerator
{
    public static string NewId() => Ulid.Generate().ToString();
}
