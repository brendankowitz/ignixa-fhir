using System.Data.Common;

namespace Ignixa.Application.Features.Conformance;

public sealed class ReindexTriggerUnavailableException(string message, Exception innerException)
    : Exception(message, innerException)
{
    public static bool IsOperational(Exception exception) =>
        exception is DbException or IOException or TimeoutException;
}
