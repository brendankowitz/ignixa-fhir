// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Exceptions;
using Ignixa.SourceNodeSerialization.Models;
using Ignixa.Validation;

namespace Ignixa.Application.Features.Resource;

/// <summary>
/// Exception thrown when a FHIR resource fails validation.
/// Contains the validation result that can be converted to an OperationOutcome.
/// </summary>
public class ValidationException : FhirException
{
    private readonly OperationOutcomeJsonNode _operationOutcome;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationException"/> class.
    /// </summary>
    /// <param name="validationResult">The validation result containing the issues.</param>
    public ValidationException(ValidationResult validationResult)
        : base("Resource validation failed")
    {
        ValidationResult = validationResult ?? throw new ArgumentNullException(nameof(validationResult));
        _operationOutcome = validationResult.ToOperationOutcome();
    }

    /// <summary>
    /// Gets the validation result containing all issues found.
    /// </summary>
    public ValidationResult ValidationResult { get; }

    /// <summary>
    /// Gets the OperationOutcome representation of the validation issues.
    /// </summary>
    public override OperationOutcomeJsonNode OperationOutcome => _operationOutcome;
}
