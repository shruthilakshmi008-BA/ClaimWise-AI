namespace ClaimWise.Api.Infrastructure;

/// <summary>Thrown when something the caller asked for does not exist (HTTP 404).</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Thrown when a request is well-formed but breaks a business rule (HTTP 409).</summary>
public sealed class BusinessRuleException(string message) : Exception(message);

/// <summary>Thrown when an upload exceeds a size or count limit (HTTP 413).</summary>
public sealed class PayloadTooLargeException(string message) : Exception(message);

/// <summary>Thrown when an upload has a file type the system does not accept (HTTP 415).</summary>
public sealed class UnsupportedFileException(string message) : Exception(message);
