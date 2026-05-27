namespace ERPApiHub.Application.Exceptions;

public sealed class InvoiceStatusChangeBlockedException : Exception
{
    public InvoiceStatusChangeBlockedException(string reason) : base(reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}
