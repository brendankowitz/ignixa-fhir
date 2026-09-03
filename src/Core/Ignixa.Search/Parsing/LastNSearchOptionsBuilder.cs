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

        var outcomes = new List<ParameterTrace>();
        SearchOptions filters = _searchOptionsBuilder.Build(
            "Observation",
            parameters.Where(parameter => !string.Equals(parameter.Name, "max", StringComparison.Ordinal)).ToArray(),
            _schemaProvider,
            outcomes);

        bool requiresInputs = _schemaProvider.Version switch
        {
            FhirVersion.Stu3 => false,
            FhirVersion.R4 or FhirVersion.R4B or FhirVersion.R5 or FhirVersion.R6 or FhirVersion.Unspecified => true,
            _ => throw new NotSupportedException(
                $"The '$lastn' operation does not define required-input validation for FHIR version {(byte)_schemaProvider.Version}."),
        };

        if (requiresInputs && !outcomes.Any(HasSubject))
        {
            throw new BadSearchRequestException("The '$lastn' operation requires a patient or subject parameter.");
        }

        if (requiresInputs && !outcomes.Any(IsCategoryOrCodeBearing))
        {
            throw new BadSearchRequestException(
                "The '$lastn' operation requires a category parameter or a search parameter that resolves to a CodeableConcept or Coding.");
        }

        return new LastNSearchOptions(
            filters,
            maximum,
            _searchParameterDefinitionManager.GetSearchParameter("Observation", "code"),
            _searchParameterDefinitionManager.GetSearchParameter("Observation", "date"),
            parameters.Any(parameter => parameter.Category == ParameterCategory.Count),
            parameters.Any(parameter => parameter.Category == ParameterCategory.ContinuationToken));
    }

    private static int ParseMaximum(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maximum))
        {
            throw new BadSearchRequestException($"The 'max' parameter value '{value}' is not a valid integer.");
        }

        return maximum;
    }

    private static bool HasSubject(ParameterTrace parameter)
    {
        string code = parameter.Key.Split(':', 2)[0];
        return parameter.Outcome is ParameterOutcome.Compiled &&
               code is "patient" or "subject";
    }

    private bool IsCategoryOrCodeBearing(ParameterTrace parameter)
    {
        if (parameter.Outcome is not ParameterOutcome.Compiled)
        {
            return false;
        }

        string code = parameter.Key.Split(':', 2)[0];
        return _searchParameterDefinitionManager.TryGetSearchParameter("Observation", code, out SearchParameterInfo searchParameter) &&
               (searchParameter.Code == "category" ||
               (ReturnsCodeBearingType(searchParameter.Expression) ||
                searchParameter.Component.Any(component => ReturnsCodeBearingType(component.Expression))));
    }

    private bool ReturnsCodeBearingType(string expression)
    {
        var analysis = _fhirPathAnalyzer.Analyze(expression, "Observation");
        return analysis.IsValid &&
               analysis.InferredTypes.Types.Any(type =>
                   type.TypeName is "Coding" or "CodeableConcept" or "code");
    }
}
