// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Patch.Validation;
using Xunit;

namespace Ignixa.Application.Tests.Features.Patch.Validation;

public class FhirPatchValidatorTests
{
    private readonly FhirPatchValidator _validator;

    public FhirPatchValidatorTests()
    {
        _validator = new FhirPatchValidator();
    }

    [Fact]
    public void GivenValidOperations_WhenValidating_ThenNoExceptionThrown()
    {
        var operations = new[]
        {
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Replace,
                Path = "Patient.gender",
                Value = "female",
            },
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Add,
                Path = "Patient.name",
                Value = new { family = "Doe" },
            },
        };

        _validator.Validate(operations);
    }

    [Fact]
    public void GivenNullOperations_WhenValidating_ThenThrowsException()
    {
        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(null));
        Assert.Contains("At least one patch operation is required", ex.Message);
    }

    [Fact]
    public void GivenEmptyOperations_WhenValidating_ThenThrowsException()
    {
        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(System.Array.Empty<FhirPatchOperation>()));
        Assert.Contains("At least one patch operation is required", ex.Message);
    }

    [Fact]
    public void GivenMissingPath_WhenValidatingReplace_ThenThrowsException()
    {
        var operations = new[]
        {
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Replace,
                Path = null,
                Value = "test",
            },
        };

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(operations));
        Assert.Contains("path", ex.Message);
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void GivenEmptyPath_WhenValidatingReplace_ThenThrowsException()
    {
        var operations = new[]
        {
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Replace,
                Path = string.Empty,
                Value = "test",
            },
        };

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(operations));
        Assert.Contains("path", ex.Message);
    }

    [Fact]
    public void GivenMissingValue_WhenValidatingAdd_ThenThrowsException()
    {
        var operations = new[]
        {
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Add,
                Path = "Patient.name",
                Value = null,
            },
        };

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(operations));
        Assert.Contains("value", ex.Message);
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void GivenMissingIndex_WhenValidatingInsert_ThenThrowsException()
    {
        var operations = new[]
        {
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Insert,
                Path = "Patient.name",
                Value = "test",
                Index = null,
            },
        };

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(operations));
        Assert.Contains("index", ex.Message);
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void GivenMissingSource_WhenValidatingMove_ThenThrowsException()
    {
        var operations = new[]
        {
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Move,
                Source = null,
                Destination = "Patient.name",
            },
        };

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(operations));
        Assert.Contains("source", ex.Message);
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void GivenMissingDestination_WhenValidatingMove_ThenThrowsException()
    {
        var operations = new[]
        {
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Move,
                Source = "Patient.gender",
                Destination = null,
            },
        };

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(operations));
        Assert.Contains("destination", ex.Message);
        Assert.Contains("required", ex.Message);
    }

    [Fact]
    public void GivenDeleteOperation_WhenValidatingWithPathOnly_ThenNoExceptionThrown()
    {
        var operations = new[]
        {
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Delete,
                Path = "Patient.gender",
            },
        };

        _validator.Validate(operations);
    }

    [Fact]
    public void GivenMultipleOperations_WhenOneIsInvalid_ThenThrowsException()
    {
        var operations = new[]
        {
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Replace,
                Path = "Patient.gender",
                Value = "female",
            },
            new FhirPatchOperation
            {
                Type = FhirPatchOperationType.Add,
                Path = "Patient.name",
                Value = null, // Invalid
            },
        };

        var ex = Assert.Throws<FhirPatchException>(() => _validator.Validate(operations));
        Assert.Contains("Operation 1", ex.Message); // Second operation (index 1)
    }
}
