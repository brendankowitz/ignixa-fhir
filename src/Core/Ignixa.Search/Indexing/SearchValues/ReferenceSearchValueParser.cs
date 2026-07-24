// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using System.Text.RegularExpressions;
using EnsureThat;
using Ignixa.Specification;

namespace Ignixa.Search.Indexing.SearchValues;

/// <summary>
/// Provides mechanism to parse a string to an instance of <see cref="ReferenceSearchValue"/>.
/// </summary>
public class ReferenceSearchValueParser : IReferenceSearchValueParser
{
    private const string ResourceTypeCapture = "resourceType";
    private const string ResourceIdCapture = "resourceId";

    private static readonly string[] SupportedSchemes = [Uri.UriSchemeHttps, Uri.UriSchemeHttp];

    private readonly IFhirSchemaProvider _fhirSchema;
    private readonly IFhirBaseUriProvider _baseUriProvider;
    private readonly string ReferenceCaptureRegexPattern;

    private readonly Regex ReferenceRegex;
    private readonly string ResourceTypesPattern;

    /// <param name="baseUriProvider">
    /// Recognizes an absolute reference that points back at this server. Required rather than optional:
    /// the index path and the query path must be handed the same provider, and a null here silently stores
    /// self-references in a form the other path will not find. Pass
    /// <see cref="NullFhirBaseUriProvider.Instance"/> to opt out deliberately.
    /// </param>
    public ReferenceSearchValueParser(IFhirSchemaProvider fhirSchema, IFhirBaseUriProvider baseUriProvider)
    {
        ArgumentNullException.ThrowIfNull(baseUriProvider);

        _baseUriProvider = baseUriProvider;
        ResourceTypesPattern = string.Join('|', fhirSchema.ResourceTypeNames);
        ReferenceCaptureRegexPattern = $@"(?<{ResourceTypeCapture}>{ResourceTypesPattern})\/(?<{ResourceIdCapture}>[A-Za-z0-9\-\.]{{1,64}})(\/_history\/[A-Za-z0-9\-\.]{{1,64}})?";

        ReferenceRegex = new Regex(
            ReferenceCaptureRegexPattern,
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        _fhirSchema = fhirSchema;
    }

    /// <inheritdoc />
    public ReferenceSearchValue Parse(string s)
    {
        EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

        Match match = ReferenceRegex.Match(s);

        if (match.Success)
        {
            string resourceTypeInString = match.Groups[ResourceTypeCapture].Value;

            if (!string.IsNullOrEmpty(resourceTypeInString) && !_fhirSchema.ResourceTypeNames.Contains(resourceTypeInString)) throw new ArgumentException(string.Format(Resources.ResourceNotSupported, resourceTypeInString), resourceTypeInString);

            string resourceId = match.Groups[ResourceIdCapture].Value;

            int resourceTypeStartIndex = match.Groups[ResourceTypeCapture].Index;

            if (resourceTypeStartIndex == 0)
                // This is relative URL.
                return new ReferenceSearchValue(
                    ReferenceKind.InternalOrExternal,
                    null,
                    resourceTypeInString,
                    resourceId);

            try
            {
                var baseUri = new Uri(s.Substring(0, resourceTypeStartIndex), UriKind.RelativeOrAbsolute);

                // An absolute URL pointing back at this server is the same resource as the equivalent
                // relative reference, so it collapses to the relative form. Both the index path and the
                // query path run this parser, which is what makes the two reconcile. The provider answers
                // over every base this server serves under, so the collapse does not depend on which route
                // form the request arrived on.
                if (_baseUriProvider.IsServiceBaseUri(baseUri))
                {
                    return new ReferenceSearchValue(
                        ReferenceKind.Internal,
                        null,
                        resourceTypeInString,
                        resourceId);
                }

                if (baseUri.IsAbsoluteUri && SupportedSchemes.Contains(baseUri.Scheme, StringComparer.OrdinalIgnoreCase))
                {
                    return new ReferenceSearchValue(
                        ReferenceKind.External,
                        baseUri,
                        resourceTypeInString,
                        resourceId);
                }

                // Neither this server's base nor a usable absolute URL: leave the base attached but do not
                // claim to know which side of the boundary it falls on.
                return new ReferenceSearchValue(
                    ReferenceKind.InternalOrExternal,
                    baseUri,
                    resourceTypeInString,
                    resourceId);
            }
            catch (UriFormatException)
            {
                // The type/id shape matched but the base segment ahead of it is not a parseable absolute
                // URI, so there is no base to attach. Fall through to the same unparsed form the regex
                // miss below produces: indexing records the reference verbatim rather than failing the
                // write, and both routes into that form agree on what it means.
            }
        }

        // Unparsed: no base and no resource type, the whole input preserved as the id. Reached either
        // because the reference does not match the type/id shape at all, or because its base did not parse.
        return new ReferenceSearchValue(
            ReferenceKind.InternalOrExternal,
            null,
            null,
            s);
    }
}
