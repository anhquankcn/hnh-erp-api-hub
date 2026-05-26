namespace ERPApiHub.Application.Errors;

/// <summary>Thrown when a requested resource is not found.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }

    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when an operation is forbidden regardless of authentication.</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }

    public ForbiddenException(string message, Exception innerException) : base(message, innerException) { }
}
