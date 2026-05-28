namespace ERPApiHub.Domain.Entities;

public static class ExternalSystemTypes
{
    public const string Crm = "CRM";
    public const string Ticketing = "Ticketing";
    public const string Payment = "Payment";
    public const string Ota = "OTA";
    public const string Partner = "Partner";
    public const string Internal = "Internal";

    public static readonly string[] All = [Crm, Ticketing, Payment, Ota, Partner, Internal];
}
