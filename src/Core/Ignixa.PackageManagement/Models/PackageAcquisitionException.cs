using System.Net;

namespace Ignixa.PackageManagement.Models;

/// <summary>Safe acquisition diagnostic. It never contains URLs, credentials, response bodies or nested transport errors.</summary>
public sealed class PackageAcquisitionException(PackageAcquisitionError error, HttpStatusCode? statusCode = null)
    : Exception($"Package acquisition failed: {error}.")
{
    /// <summary>
    /// Exception.Data key whose value is PackageAcquisitionError.CacheFailure when owned staging
    /// cleanup also failed. Available on the primary acquisition or caller-cancellation exception.
    /// </summary>
    public const string CacheCleanupFailureDataKey = "Ignixa.PackageManagement.CacheCleanupFailure";

    public PackageAcquisitionError Error { get; } = error;
    public HttpStatusCode? StatusCode { get; } = statusCode;

    internal static void PreserveCleanupFailure(Exception source, Exception destination)
    {
        if (source.Data[CacheCleanupFailureDataKey] is PackageAcquisitionError.CacheFailure)
        {
            destination.Data[CacheCleanupFailureDataKey] = PackageAcquisitionError.CacheFailure;
        }
    }
}
