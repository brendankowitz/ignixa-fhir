/* Copyright (c) 2025, Ignixa Contributors */

using Shouldly;
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
        options.MaxRecursionDepth.ShouldBe(50);
        options.MaxElementsCreated.ShouldBe(100_000);
        options.MaxMapSizeBytes.ShouldBe(50_000_000);
        options.MaxInputResourceSizeBytes.ShouldBe(10_000_000);
        options.TransformTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.FhirPathTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        options.MaxCodeLength.ShouldBe(100);
        options.MaxErrorsCollected.ShouldBe(100);
        options.ErrorMode.ShouldBe(ErrorMode.Strict);
        options.AllowFileSystemImports.ShouldBeFalse();
    }

    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenHasStandardAllowedDomains()
    {
        // Arrange & Act
        var options = MappingEvaluatorOptions.Default;

        // Assert
        options.AllowedImportDomains.ShouldContain("hl7.org");
        options.AllowedImportDomains.ShouldContain("fhir.org");
        options.AllowedImportDomains.ShouldContain("build.fhir.org");
    }

    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenHasStandardAllowedConceptMapSystems()
    {
        // Arrange & Act
        var options = MappingEvaluatorOptions.Default;

        // Assert
        options.AllowedConceptMapTargetSystems.ShouldContain("http://snomed.info/sct");
        options.AllowedConceptMapTargetSystems.ShouldContain("http://loinc.org");
        options.AllowedConceptMapTargetSystems.ShouldContain("http://hl7.org/fhir/*");
        options.AllowedConceptMapTargetSystems.ShouldContain("http://unitsofmeasure.org");
        options.AllowedConceptMapTargetSystems.ShouldContain("http://terminology.hl7.org/*");
        options.AllowedConceptMapTargetSystems.ShouldContain("urn:oid:*");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("MaxRecursionDepth must be positive");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("MaxElementsCreated must be positive");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("MaxMapSizeBytes must be positive");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("MaxInputResourceSizeBytes must be positive");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("TransformTimeout must be positive");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("FhirPathTimeout must be positive");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("MaxCodeLength must be positive");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("MaxErrorsCollected must be positive");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("FileSystemImportSandbox must be specified");
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
        Should.Throw<ArgumentException>(act).Message.ShouldContain("FileSystemImportSandbox must be specified");
    }

    [Fact]
    public void GivenValidOptions_WhenValidated_ThenDoesNotThrow()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var act = () => options.Validate();

        // Assert
        Should.NotThrow(act);
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
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenAllowedDomainDifferentCase_WhenIsDomainAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsDomainAllowed("HL7.ORG");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenDisallowedDomain_WhenIsDomainAllowedCalled_ThenReturnsFalse()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsDomainAllowed("evil.com");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenNullDomain_WhenIsDomainAllowedCalled_ThenThrowsArgumentNullException()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var act = () => { _ = options.IsDomainAllowed(null!); };

        // Assert
        Should.Throw<ArgumentNullException>(act);
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
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenExactMatchDifferentCase_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("HTTP://SNOMED.INFO/SCT");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenWildcardMatch_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("http://hl7.org/fhir/ValueSet/example");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenWildcardMatchWithDifferentCase_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("HTTP://HL7.ORG/FHIR/ValueSet/example");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenOidSystemWithWildcard_WhenIsTargetSystemAllowedCalled_ThenReturnsTrue()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("urn:oid:2.16.840.1.113883.6.96");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenDisallowedTargetSystem_WhenIsTargetSystemAllowedCalled_ThenReturnsFalse()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var result = options.IsTargetSystemAllowed("http://evil.com/codesystem");

        // Assert
        result.ShouldBeFalse();
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
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenNullTargetSystem_WhenIsTargetSystemAllowedCalled_ThenThrowsArgumentNullException()
    {
        // Arrange
        var options = MappingEvaluatorOptions.Default;

        // Act
        var act = () => { _ = options.IsTargetSystemAllowed(null!); };

        // Assert
        Should.Throw<ArgumentNullException>(act);
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
        options.MaxRecursionDepth.ShouldBe(10);
        options.MaxElementsCreated.ShouldBe(1000);
        options.TransformTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        options.ErrorMode.ShouldBe(ErrorMode.Lenient);
        options.MaxErrorsCollected.ShouldBe(50);
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
        options.AllowedImportDomains.Count.ShouldBe(2);
        options.IsDomainAllowed("example.com").ShouldBeTrue();
        options.IsDomainAllowed("test.org").ShouldBeTrue();
        options.IsDomainAllowed("hl7.org").ShouldBeFalse();
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
        options.AllowedConceptMapTargetSystems.Count.ShouldBe(1);
        options.IsTargetSystemAllowed("http://custom.com/test").ShouldBeTrue();
        options.IsTargetSystemAllowed("http://snomed.info/sct").ShouldBeFalse();
    }

    #endregion
}
