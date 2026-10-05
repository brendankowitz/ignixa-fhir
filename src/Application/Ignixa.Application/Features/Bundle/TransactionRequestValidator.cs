using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Utilities;

namespace Ignixa.Application.Features.Bundle;

/// <summary>
/// Admits only canonical tenant-relative FHIR resource interactions into the staged transaction pipeline.
/// Administrative and operation endpoints must never be selected by this pipeline.
/// </summary>
internal static class TransactionRequestValidator
{
    private static readonly Uri BaseUri = new("http://transaction.invalid/");

    public static BundleEntryContext Validate(BundleEntryContext entry, IReadOnlySet<string> resourceTypes)
    {
        if (!Uri.TryCreate(entry.RequestUrl, UriKind.Relative, out var relative) ||
            !Uri.TryCreate(BaseUri, relative, out var uri) ||
            uri.Authority != BaseUri.Authority || uri.Scheme != BaseUri.Scheme || uri.Fragment.Length != 0)
        {
            throw InvalidInteraction(entry);
        }

        var path = entry.RequestUrl.Split('?', 2)[0];
        if (path.StartsWith('/'))
        {
            path = path[1..];
        }

        // Reject any path whose interpretation changes during URI normalization, including dot
        // segments and alternate separators. Encoded path characters also fail the shape checks.
        if (uri.AbsolutePath != "/" + path)
        {
            throw InvalidInteraction(entry);
        }

        var read = entry.HttpVerb is "GET" or "HEAD";
        if (read && (path is "" or "_history"))
        {
            return entry with { RequestUrl = path + uri.Query, ResourceType = null, ResourceId = null };
        }

        var segments = path.Split('/');
        var resourceType = segments[0];
        if (!resourceTypes.Contains(resourceType))
        {
            throw InvalidInteraction(entry);
        }

        var instance = segments.Length == 2 && IsResourceId(segments[1]);
        var instanceHistory = segments.Length == 3 && IsResourceId(segments[1]) && segments[2] == "_history";
        var typeHistory = segments.Length == 2 && segments[1] == "_history";
        var versionRead = segments.Length == 4 && IsResourceId(segments[1]) && segments[2] == "_history" &&
            FhirIdSyntax.IsValid(segments[3]);
        var supported = entry.HttpVerb switch
        {
            "GET" or "HEAD" => segments.Length == 1 || instance || instanceHistory || typeHistory || versionRead,
            "POST" => resourceType != "Bundle" && segments.Length == 1 && uri.Query.Length == 0,
            "PUT" or "PATCH" or "DELETE" => resourceType != "Bundle" &&
                (instance || (segments.Length == 1 && uri.Query.Length > 1)),
            _ => false
        };
        if (!supported)
        {
            throw InvalidInteraction(entry);
        }

        return entry with
        {
            RequestUrl = path + uri.Query,
            ResourceType = resourceType,
            ResourceId = instance || instanceHistory || versionRead ? segments[1] : null
        };
    }

    public static bool IsStagedPointRead(BundleEntryContext entry) =>
        (entry.HttpVerb is "GET" or "HEAD") && IsInstancePath(entry);

    public static bool IsExplicitPut(BundleEntryContext entry) =>
        entry.HttpVerb == "PUT" && entry.ResourceType != null && entry.ResourceId != null &&
        entry.RequestUrl.Split('?', 2)[0] == $"{entry.ResourceType}/{entry.ResourceId}";

    private static bool IsInstancePath(BundleEntryContext entry) =>
        entry.ResourceType != null && entry.ResourceId != null &&
        entry.RequestUrl == $"{entry.ResourceType}/{entry.ResourceId}";

    private static bool IsResourceId(string value) =>
        FhirIdSyntax.IsValid(value);

    private static BadRequestException InvalidInteraction(BundleEntryContext entry) =>
        new($"Transaction entry {entry.Index} must name a supported canonical tenant-relative FHIR resource interaction.");
}
