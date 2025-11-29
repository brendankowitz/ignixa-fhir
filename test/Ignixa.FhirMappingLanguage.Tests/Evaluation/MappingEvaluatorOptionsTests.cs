/* Copyright (c) 2025, Ignixa Contributors */

using FluentAssertions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class MappingEvaluatorOptionsTests
{
    #region Default Values

    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenHasRecommendedSecuritySettings()
    {
        // Arrange & Act
        var options = MappingEvaluatorOptions.Default;

        // Assert
        options.MaxRecursionDepth.Should().Be(50);
        options.MaxElementsCreated.Should().Be(100_000);
        options.MaxMapSizeBytes.Should().Be(50_000_000);
        options.MaxInputResourceSizeBytes.Should().Be(10_000_000);
        options.TransformTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.FhirPathTimeout.Should().Be(TimeSpan.FromSeconds(5));
        options.MaxCodeLength.Should().Be(100);
        options.MaxErrorsCollected.Should().Be(100);
        options.ErrorMode.Should().Be(ErrorMode.Strict);
        options.AllowFileSystemImports.Should().BeFalse();
    }

    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenHasStandardAllowedDomains()
    {
        // Arrange & Act
        var options = MappingEvaluatorOptions.Default;

        // Assert
        options.AllowedImportDomains.Should().Contain("hl7.org");
        options.AllowedImportDomains.Should().Contain("fhir.org");
        options.AllowedImportDomains.Should().Contain("build.fhir.org");
    }

    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenHasStandardAllowedConceptMapSystems()
    {
        // Arrange & Act
        var options = MappingEvaluatorOptions.Default;

        // Assert
        options.AllowedConceptMapTargetSystems.Should().Contain("http://snomed.info/sct");
        options.AllowedConceptMapTargetSystems.Should().Contain("http://loinc.org");
        options.AllowedConceptMapTargetSystems.Should().Contain("http://hl7.org/fhir/*");
        options.AllowedConceptMapTargetSystems.Should().Contain("http://unitsofmeasure.org");
        options.AllowedConceptMapTargetSystems.Should().Contain("http://terminology.hl7.org/*");
        options.AllowedConceptMapTargetSystems.Should().Contain("urn:oid:*");
    }

    #endregion

    #region Validation

    [Fact]
    public void GivenNegativeMaxRecursionDepth_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxRecursionDepth = -1
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxRecursionDepth must be positive*");
    }

    [Fact]
    public void GivenNegativeMaxElementsCreated_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxElementsCreated = -1
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxElementsCreated must be positive*");
    }

    [Fact]
    public void GivenNegativeMaxMapSizeBytes_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxMapSizeBytes = -1
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxMapSizeBytes must be positive*");
    }

    [Fact]
    public void GivenNegativeMaxInputResourceSizeBytes_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxInputResourceSizeBytes = -1
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxInputResourceSizeBytes must be positive*");
    }

    [Fact]
    public void GivenNegativeTransformTimeout_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            TransformTimeout = TimeSpan.FromSeconds(-1)
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*TransformTimeout must be positive*");
    }

    [Fact]
    public void GivenNegativeFhirPathTimeout_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            FhirPathTimeout = TimeSpan.FromSeconds(-1)
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*FhirPathTimeout must be positive*");
    }

    [Fact]
    public void GivenNegativeMaxCodeLength_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxCodeLength = -1
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxCodeLength must be positive*");
    }

    [Fact]
    public void GivenNegativeMaxErrorsCollected_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxErrorsCollected = -1
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxErrorsCollected must be positive*");
    }

    [Fact]
    public void GivenFileSystemImportsEnabledWithoutSandbox_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            AllowFileSystemImports = true,
            FileSystemImportSandbox = null
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*FileSystemImportSandbox must be specified*");
    }

    [Fact]
    public void GivenFileSystemImportsEnabledWithEmptySandbox_WhenValidated_ThenThrowsArgumentException()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            AllowFileSystemImports = true,
            FileSystemImportSandbox = "   "
        };

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*FileSystemImportSandbox must be specified*");
    }

    [Fact]
    public void GivenValidOptions_WhenValidated_ThenDoesNotThrow()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var act = () => options.Validate();

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region IsDomainAllowed

    [Fact]
    public void GivenAllowedDomain_WhenIsDomainAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsDomainAllowed("hl7.org");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GivenAllowedDomainDifferentCase_WhenIsDomainAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsDomainAllowed("HL7.ORG");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GivenDisallowedDomain_WhenIsDomainAllowedCalled_ThenReturnsFalse()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsDomainAllowed("evil.com");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GivenNullDomain_WhenIsDomainAllowedCalled_ThenThrowsArgumentNullException()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var act = () => options.IsDomainAllowed(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region IsTargetSystemAllowed

    [Fact]
    public void GivenExactMatchTargetSystem_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("http://snomed.info/sct");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GivenExactMatchDifferentCase_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("HTTP://SNOMED.INFO/SCT");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GivenWildcardMatch_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("http://hl7.org/fhir/ValueSet/example");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GivenWildcardMatchWithDifferentCase_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("HTTP://HL7.ORG/FHIR/ValueSet/example");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GivenOidSystemWithWildcard_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("urn:oid:2.16.840.1.113883.6.96");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GivenDisallowedTargetSystem_WhenIsTargetSystemAllowedCalled_ThenReturnsFalse()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("http://evil.com/codesystem");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GivenEmptyAllowedSystems_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = new MappingEvaluatorOptions();
        options.AllowedConceptMapTargetSystems.Clear();

        // Act
        var result = options.IsTargetSystemAllowed("http://any-system.com");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GivenNullTargetSystem_WhenIsTargetSystemAllowedCalled_ThenThrowsArgumentNullException()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var act = () => options.IsTargetSystemAllowed(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Custom Configuration

    [Fact]
    public void GivenCustomOptions_WhenConfigured_ThenReflectsCustomSettings()
    {
        // Arrange
        var options = new MappingEvaluatorOptions
        {
            MaxRecursionDepth = 10,
            MaxElementsCreated = 1000,
            TransformTimeout = TimeSpan.FromSeconds(5),
            ErrorMode = ErrorMode.Lenient,
            MaxErrorsCollected = 50
        };

        // Act & Assert
        options.MaxRecursionDepth.Should().Be(10);
        options.MaxElementsCreated.Should().Be(1000);
        options.TransformTimeout.Should().Be(TimeSpan.FromSeconds(5));
        options.ErrorMode.Should().Be(ErrorMode.Lenient);
        options.MaxErrorsCollected.Should().Be(50);
    }

    [Fact]
    public void GivenCustomAllowedDomains_WhenConfigured_ThenCanAddAndRemoveDomains()
    {
        // Arrange
        var options = new MappingEvaluatorOptions();

        // Act
        options.AllowedImportDomains.Clear();
        options.AllowedImportDomains.Add("example.com");
        options.AllowedImportDomains.Add("test.org");

        // Assert
        options.AllowedImportDomains.Should().HaveCount(2);
        options.IsDomainAllowed("example.com").Should().BeTrue();
        options.IsDomainAllowed("test.org").Should().BeTrue();
        options.IsDomainAllowed("hl7.org").Should().BeFalse();
    }

    [Fact]
    public void GivenCustomConceptMapSystems_WhenConfigured_ThenCanAddAndRemoveSystems()
    {
        // Arrange
        var options = new MappingEvaluatorOptions();

        // Act
        options.AllowedConceptMapTargetSystems.Clear();
        options.AllowedConceptMapTargetSystems.Add("http://custom.com/*");

        // Assert
        options.AllowedConceptMapTargetSystems.Should().HaveCount(1);
        options.IsTargetSystemAllowed("http://custom.com/test").Should().BeTrue();
        options.IsTargetSystemAllowed("http://snomed.info/sct").Should().BeFalse();
    }

    #endregion
}
