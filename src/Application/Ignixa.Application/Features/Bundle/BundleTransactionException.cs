using Ignixa.Domain.Exceptions;
using Ignixa.Models;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// The exception that is thrown when an entry of a transaction bundle fails, rejecting the whole transaction.
/// </summary>
/// <remarks>
/// The summary issue comes first, followed by the failing entry's own issues so clients can see why it failed.
/// </remarks>
public sealed class BundleTransactionException : BadRequestException
{
    private readonly int _statusCode;

    /// <param name="message">Summary of which entry failed.</param>
    /// <param name="statusCode">The failing entry's status, returned as the transaction response status.</param>
    /// <param name="entryIssues">The failing entry's OperationOutcome issues, added after the summary issue.</param>
    public BundleTransactionException(string message, int statusCode, IEnumerable<OperationOutcomeIssue>? entryIssues = null)
        : base(message)
    {
        _statusCode = statusCode;
        foreach (var issue in entryIssues ?? [])
        {
            Issues.Add(issue);
        }
    }

    public override int StatusCode => _statusCode;
}
