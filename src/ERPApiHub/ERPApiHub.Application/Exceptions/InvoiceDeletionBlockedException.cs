namespace ERPApiHub.Application.Exceptions;

public sealed class InvoiceDeletionBlockedException : Exception
{
    public InvoiceDeletionBlockedException(string reason) : base(reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}
