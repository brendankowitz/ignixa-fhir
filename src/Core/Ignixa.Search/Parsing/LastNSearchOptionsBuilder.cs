// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Models;

namespace Ignixa.Search.Parsing;

/// <summary>
/// Builds operation-specific options for an Observation <c>$lastn</c> request.
/// </summary>
public sealed class LastNSearchOptionsBuilder
{
    private const int DefaultMaximum = 1;
    private const int MaximumAllowed = 1000;
    private readonly ISearchOptionsBuilder _searchOptionsBuilder;
    private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly FhirPathAnalyzer _fhirPathAnalyzer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LastNSearchOptionsBuilder"/> class.
    /// </summary>
    /// <param name="searchOptionsBuilder">The ordinary Observation search-options builder.</param>
    /// <param name="searchParameterDefinitionManager">The version-specific search-parameter definitions.</param>
    /// <param name="schemaProvider">The version-specific Observation schema.</param>
    public LastNSearchOptionsBuilder(
        ISearchOptionsBuilder searchOptionsBuilder,
        ISearchParameterDefinitionManager searchParameterDefinitionManager,
        IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(searchOptionsBuilder);
        ArgumentNullException.ThrowIfNull(searchParameterDefinitionManager);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        _searchOptionsBuilder = searchOptionsBuilder;
        _searchParameterDefinitionManager = searchParameterDefinitionManager;
        _schemaProvider = schemaProvider;
        _fhirPathAnalyzer = new FhirPathAnalyzer(schemaProvider);
    }

    /// <summary>
    /// Builds <see cref="LastNSearchOptions"/> from Observation operation parameters.
    /// </summary>
    /// <param name="parameters">The parsed query parameters.</param>
    /// <returns>The operation-specific options and ordinary candidate-set filters.</returns>
    public LastNSearchOptions Build(IReadOnlyList<QueryParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (_schemaProvider.Version is FhirVersion.R4 or FhirVersion.R4B or FhirVersion.R5 &&
            !parameters.Any(HasSubject))
        {
            throw new BadSearchRequestException("The '$lastn' operation requires a patient or subject parameter.");
        }

        if (_schemaProvider.Version is FhirVersion.R4 or FhirVersion.R4B or FhirVersion.R5 &&
            !parameters
                .Where(parameter => !string.Equals(parameter.Name, "max", StringComparison.Ordinal))
                .Any(IsCategoryOrCodeBearing))
        {
            throw new BadSearchRequestException(
                "The '$lastn' operation requires a category parameter or a search parameter that resolves to a CodeableConcept or Coding.");
        }

        QueryParameter[] maximumParameters = parameters
            .Where(parameter => string.Equals(parameter.Name, "max", StringComparison.Ordinal))
            .ToArray();
        if (maximumParameters.Length > 1)
        {
            throw new BadSearchRequestException("The 'max' parameter cannot be specified more than once.");
        }

        int maximum = maximumParameters.Length == 0
            ? DefaultMaximum
            : ParseMaximum(maximumParameters[0].Value);
        if (maximum < 1)
        {
            throw new BadSearchRequestException("The 'max' parameter value must be a positive integer.");
        }

        if (maximum > MaximumAllowed)
        {
            throw new BadSearchRequestException($"The 'max' parameter value must not exceed {MaximumAllowed}.");
        }

        SearchOptions filters = _searchOptionsBuilder.Build(
            "Observation",
            parameters.Where(parameter => !string.Equals(parameter.Name, "max", StringComparison.Ordinal)).ToArray(),
            _schemaProvider);

        return new LastNSearchOptions(
            filters,
            maximum,
            _searchParameterDefinitionManager.GetSearchParameter("Observation", "code"),
            _searchParameterDefinitionManager.GetSearchParameter("Observation", "date"));
    }

    private static int ParseMaximum(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maximum))
        {
            throw new BadSearchRequestException($"The 'max' parameter value '{value}' is not a valid integer.");
        }

        return maximum;
    }

    private static bool HasSubject(QueryParameter parameter)
    {
        string code = parameter.Name.Split(':', 2)[0];
        return code is "patient" or "subject";
    }

    private bool IsCategoryOrCodeBearing(QueryParameter parameter)
    {
        string code = parameter.Name.Split(':', 2)[0];
        SearchParameterInfo? searchParameter = _searchParameterDefinitionManager.GetSearchParameter("Observation", code);
        if (searchParameter?.Code == "category")
        {
            return true;
        }

        var analysis = _fhirPathAnalyzer.Analyze(searchParameter?.Expression ?? string.Empty, "Observation");
        return analysis.IsValid &&
               (analysis.InferredTypes.CanBeOfType("CodeableConcept") ||
                analysis.InferredTypes.CanBeOfType("Coding"));
    }
}
