namespace Inventory.Application.Common;

/// <summary>The resource does not exist or is outside the caller's scope (404, so existence is not leaked).</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException() : base("The requested resource was not found.") { }
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Authentication failed (401). The message is safe to show to the user.</summary>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException() : base("Sign-in failed.") { }
    public AuthenticationFailedException(string message) : base(message) { }
    public AuthenticationFailedException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>The caller is authenticated but not allowed to do this (403).</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException() : base("You do not have permission to do this.") { }
    public ForbiddenException(string message) : base(message) { }
    public ForbiddenException(string message, Exception innerException) : base(message, innerException) { }
}
