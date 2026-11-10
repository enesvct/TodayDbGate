namespace TodayDbGate.Application.Exceptions;

/// <summary>
/// Base class for errors whose <see cref="Exception.Message"/> is safe to return to an MCP client.
/// Never put hosts, usernames or connection strings in these messages.
/// </summary>
public class GatewayException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The token has no grant for the requested alias (or the alias does not exist; we do not tell which).</summary>
public sealed class AccessDeniedException(string message) : GatewayException(message);

/// <summary>The query was refused before reaching the database.</summary>
public sealed class QueryRejectedException(string message) : GatewayException(message);

/// <summary>The target database could not be reached. Details stay in <see cref="Exception.InnerException"/>.</summary>
public sealed class DatabaseUnavailableException(Exception innerException)
    : GatewayException("Could not connect to the database.", innerException);

/// <summary>The database returned an error while running the query.</summary>
public sealed class QueryFailedException(string message, Exception innerException)
    : GatewayException(message, innerException);
