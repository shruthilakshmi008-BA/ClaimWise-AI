namespace ClaimWise.Api.Infrastructure;

/// <summary>Thrown when something the caller asked for does not exist (HTTP 404).</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Thrown when a request is well-formed but breaks a business rule (HTTP 409).</summary>
public sealed class BusinessRuleException(string message) : Exception(message);
