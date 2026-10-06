// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Search.Definition.BundleNavigators;
using Ignixa.Serialization;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.FhirPath.Evaluation;
using Microsoft.Extensions.Logging;

namespace Ignixa.Search.Models;

[DebuggerDisplay("{Name}, Type: {Type}")]
public class SearchParameterInfo : IEquatable<SearchParameterInfo>
{
    public SearchParameterInfo(
        string name,
        string code,
        SearchParamType searchParamType,
        Uri url = null,
        IReadOnlyList<SearchParameterComponentInfo> components = null,
        string expression = null,
        IReadOnlyList<string> targetResourceTypes = null,
        IReadOnlyList<string> baseResourceTypes = null,
        string description = null,
        VectorSearchConfig vectorConfig = null)
        : this(name, code)
    {
        Url = url;
        Type = searchParamType;
        Component = components ?? Array.Empty<SearchParameterComponentInfo>();
        Expression = expression ?? string.Empty;
        TargetResourceTypes = targetResourceTypes ?? Array.Empty<string>();
        BaseResourceTypes = baseResourceTypes ?? Array.Empty<string>();
        Description = description ?? string.Empty;
        VectorConfig = vectorConfig;

        // Enable sorting for sortable parameter types by default
        SortStatus = IsSortableType(searchParamType) ? SortParameterStatus.Enabled : SortParameterStatus.Disabled;
    }

    public SearchParameterInfo(string name, string code)
    {
        EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
        EnsureArg.IsNotNullOrWhiteSpace(code, nameof(code));

        Name = name;
        Code = code;
        Description = string.Empty;
        Expression = string.Empty;
        Url = null!;
        Component = Array.Empty<SearchParameterComponentInfo>();
    }

    public SearchParameterInfo(SearchParameterNavigator wrapper, ILogger logger = null)
    {
        SearchParameterComponentInfo[] components = wrapper.Component
            .Select(x => new SearchParameterComponentInfo(
                new Uri(GetComponentDefinition(x)),
                x.Scalar("expression")?.ToString()))
            .ToArray();

        SearchParamType searchParamType = EnumUtility.ParseLiteral<SearchParamType>(wrapper.Type)
            .GetValueOrDefault();

        Name = wrapper.Name;
        Code = wrapper.Code;
        Type = searchParamType;
        Url = new Uri(wrapper.Url);
        Expression = wrapper.Expression;
        Description = wrapper.Description;
        Component = components;
        TargetResourceTypes = wrapper.Target;
        BaseResourceTypes = wrapper.Base;

        // Enable sorting for sortable parameter types by default
        SortStatus = IsSortableType(searchParamType) ? SortParameterStatus.Enabled : SortParameterStatus.Disabled;

        IElement vectorConfigExtension = wrapper.VectorConfigExtension;
        if (vectorConfigExtension is not null)
        {
            try
            {
                VectorConfig = VectorSearchConfig.Parse(vectorConfigExtension);
            }
            catch (FormatException ex)
            {
                // A malformed vector-search-config must not fail the whole definition load: the
                // parameter is still registered, just unable to support semantic search, same as any
                // other configuration a host cannot act on.
                IsSupported = false;
                logger?.LogWarning(
                    ex,
                    "SearchParameter '{Url}' has an invalid vector-search-config extension and will be registered as unsupported: {Reason}",
                    Url,
                    ex.Message);
            }
        }

        string GetComponentDefinition(IElement component)
        {
            // In Stu3 the Url is under 'definition.reference'
            return component.Scalar("definition.reference")?.ToString() ??
                   component.Scalar("definition")?.ToString();
        }
    }

    /// <summary>
    /// Determines if a search parameter type is sortable.
    /// </summary>
    private static bool IsSortableType(SearchParamType type)
    {
        // These parameter types support sorting per FHIR spec and are commonly used for sorting
        // Note: Reference type is not included as sorting by reference ID is not typically useful
        // and can be enabled explicitly if needed via SortStatus configuration
        return type is SearchParamType.Date
            or SearchParamType.Number
            or SearchParamType.Quantity
            or SearchParamType.String
            or SearchParamType.Token
            or SearchParamType.Uri;
    }

    public string Name { get; }

    public string Code { get; }

    public string Description { get; set; }

    public string Expression { get; }

    public IReadOnlyList<string> TargetResourceTypes { get; } = Array.Empty<string>();

    public IReadOnlyList<string> BaseResourceTypes { get; } = Array.Empty<string>();

    public Uri Url { get; }

    public SearchParamType Type { get; }

    /// <summary>
    /// Returns true if this parameter is enabled for searches
    /// </summary>
    public bool IsSearchable { get; set; } = true;

    /// <summary>
    /// Returns true if the system has the capability for indexing and searching for this parameter
    /// </summary>
    public bool IsSupported { get; set; } = true;

    /// <summary>
    /// Returns true if the search parameter resolves to more than one type (FhirString, FhirUri, etc...)
    /// but not all types are able to be indexed / searched
    /// </summary>
    public bool IsPartiallySupported { get; set; }

    /// <summary>
    /// The status of the search parameters use for sorting
    /// </summary>
    public SortParameterStatus SortStatus { get; set; }

    /// <summary>
    /// The component definitions if this is a composite search parameter (<see cref="Type"/> is <see cref="SearchParamType.Composite"/>)
    /// </summary>
    public IReadOnlyList<SearchParameterComponentInfo> Component { get; }

    /// <summary>
    /// If this search parameter overrides another parameter (e.g., IG parameter overriding base parameter),
    /// this contains the canonical URL of the overridden parameter.
    /// Used for database indexing to ensure both parameters map to the same SearchParamId.
    /// Example: US Core "us-core-encounter-patient" overrides base "clinical-patient".
    /// </summary>
    public Uri OverridesUrl { get; set; }

    /// <summary>
    /// Semantic/vector search configuration parsed from the <c>vector-search-config</c> extension, or
    /// null when the extension is absent or failed to parse (see <see cref="IsSupported"/>).
    /// </summary>
    public VectorSearchConfig VectorConfig { get; set; }

    /// <summary>
    /// True when this is a <see cref="SearchParamType.Special"/> parameter carrying a successfully
    /// parsed <see cref="VectorConfig"/>. Query and indexing behavior that act on this are implemented
    /// separately from this parse step.
    /// </summary>
    public bool IsSemantic => Type == SearchParamType.Special && VectorConfig is not null;

    /// <summary>Equality is decided on <see cref="Url"/> alone when one is present: a canonical URL identifies a
    /// search parameter, and two definitions carrying it are the same parameter however their other fields drift.
    /// Code/Type/Expression are only consulted for the URL-less case. <see cref="GetHashCode"/> mirrors this
    /// split exactly -- both must agree, or a HashSet keeps two entries it considers equal.</summary>
    public bool Equals([AllowNull] SearchParameterInfo other)
    {
        if (other == null) return false;

        if (Url != other.Url) return false;

        if (Url == null)
            if (!Code.Equals(other.Code, StringComparison.OrdinalIgnoreCase) ||
                Type != other.Type ||
                Expression != other.Expression)
                return false;

        return true;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as SearchParameterInfo);
    }

    /// <summary>Mirrors <see cref="Equals(SearchParameterInfo)"/>: hashed on Url alone when one is present, on the
    /// same three fields Equals falls back to when it is not. Combining all four unconditionally would break the
    /// contract -- two instances sharing a Url but differing in Expression compare equal yet would land in
    /// different buckets, so a HashSet would retain both and a later ToDictionary on Code would throw.</summary>
    public override int GetHashCode()
    {
        if (Url != null) return Url.GetHashCode();

        return HashCode.Combine(
            Code?.GetHashCode(StringComparison.OrdinalIgnoreCase),
            Type.GetHashCode(),
            Expression?.GetHashCode(StringComparison.OrdinalIgnoreCase));
    }
}
