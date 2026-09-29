using System.Net;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure;

#pragma warning disable CA1064 // Internal control flow, always translated at the public acquisition boundary.

// Only transport/body reads and the acquisition deadline may create this internal retry signal.
internal sealed class RetryablePackageException(
    PackageAcquisitionError error, HttpStatusCode? statusCode = null, TimeSpan? retryAfter = null) : Exception
{
    internal TimeSpan? RetryAfter { get; } = retryAfter;
    internal PackageAcquisitionException Diagnostic { get; } = new(error, statusCode);
}
