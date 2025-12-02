// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.FhirFakes.Lifecycle;

namespace Ignixa.FhirFakes.Tests.Lifecycle;

/// <summary>
/// Unit tests for <see cref="DiseaseRiskCalculator"/> to verify evidence-based risk calculations.
/// </summary>
public class DiseaseRiskCalculatorTests
{
    private readonly DiseaseRiskCalculator _calculator = new();

    #region Diabetes Risk Tests

    [Fact]
    public void GivenYoungAdult_WhenCalculatingDiabetesRisk_ThenReturnsLowBaselineRisk()
    {
        // Arrange & Act
        var risk = _calculator.CalculateDiabetesRisk(age: 25, smoker: false, bmi: 22m, familyHistory: false);

        // Assert
        risk.Should().Be(0.01, "young adults under 30 have 1% baseline risk");
    }

    [Fact]
    public void GivenMiddleAgedAdult_WhenCalculatingDiabetesRisk_ThenReturnsModerateBaselineRisk()
    {
        // Arrange & Act
        var risk = _calculator.CalculateDiabetesRisk(age: 45, smoker: false, bmi: 22m, familyHistory: false);

        // Assert
        risk.Should().Be(0.10, "adults 40-49 have 10% baseline risk");
    }

    [Fact]
    public void GivenObesity_WhenCalculatingDiabetesRisk_ThenDoublesBaselineRisk()
    {
        // Arrange & Act
        var normalWeightRisk = _calculator.CalculateDiabetesRisk(age: 45, smoker: false, bmi: 25m, familyHistory: false);
        var obesityRisk = _calculator.CalculateDiabetesRisk(age: 45, smoker: false, bmi: 32m, familyHistory: false);

        // Assert
        obesityRisk.Should().Be(normalWeightRisk * 2, "BMI >= 30 doubles diabetes risk");
    }

    [Fact]
    public void GivenSmoker_WhenCalculatingDiabetesRisk_ThenIncreasesRiskBy50Percent()
    {
        // Arrange & Act
        var nonSmokerRisk = _calculator.CalculateDiabetesRisk(age: 45, smoker: false, bmi: 25m, familyHistory: false);
        var smokerRisk = _calculator.CalculateDiabetesRisk(age: 45, smoker: true, bmi: 25m, familyHistory: false);

        // Assert
        smokerRisk.Should().Be(nonSmokerRisk * 1.5, "smoking increases risk by 50%");
    }

    [Fact]
    public void GivenFamilyHistory_WhenCalculatingDiabetesRisk_ThenDoublesBaselineRisk()
    {
        // Arrange & Act
        var noFamilyHistoryRisk = _calculator.CalculateDiabetesRisk(age: 45, smoker: false, bmi: 25m, familyHistory: false);
        var familyHistoryRisk = _calculator.CalculateDiabetesRisk(age: 45, smoker: false, bmi: 25m, familyHistory: true);

        // Assert
        familyHistoryRisk.Should().Be(noFamilyHistoryRisk * 2, "family history doubles genetic risk");
    }

    [Fact]
    public void GivenMultipleRiskFactors_WhenCalculatingDiabetesRisk_ThenCapsAt100Percent()
    {
        // Arrange & Act - 70-year-old obese smoker with family history
        var risk = _calculator.CalculateDiabetesRisk(age: 70, smoker: true, bmi: 35m, familyHistory: true);

        // Assert
        risk.Should().Be(1.0, "risk should be capped at 100% even when multipliers exceed 1.0");
        // Calculation: 0.25 * 2.0 (obesity) * 1.5 (smoking) * 2.0 (family) = 1.5 → capped at 1.0
    }

    #endregion

    #region Hypertension Risk Tests

    [Fact]
    public void GivenYoungAdultNoRiskFactors_WhenCalculatingHypertensionRisk_ThenReturnsNHANESBaseline()
    {
        // Arrange & Act
        var risk = _calculator.CalculateHypertensionRisk(age: 35, bmi: 23m, hasDiabetes: false);

        // Assert
        risk.Should().BeApproximately(0.296, 0.001, "NHANES baseline is 29.6% for U.S. adults");
    }

    [Fact]
    public void GivenOlderAdult_WhenCalculatingHypertensionRisk_ThenAdds20PercentRisk()
    {
        // Arrange & Act
        var youngerRisk = _calculator.CalculateHypertensionRisk(age: 55, bmi: 23m, hasDiabetes: false);
        var olderRisk = _calculator.CalculateHypertensionRisk(age: 65, bmi: 23m, hasDiabetes: false);

        // Assert
        olderRisk.Should().Be(youngerRisk + 0.20, "age >= 60 adds 20% risk");
    }

    [Fact]
    public void GivenObesity_WhenCalculatingHypertensionRisk_ThenAdds15PercentRisk()
    {
        // Arrange & Act
        var normalWeightRisk = _calculator.CalculateHypertensionRisk(age: 45, bmi: 25m, hasDiabetes: false);
        var obesityRisk = _calculator.CalculateHypertensionRisk(age: 45, bmi: 32m, hasDiabetes: false);

        // Assert
        obesityRisk.Should().Be(normalWeightRisk + 0.15, "BMI >= 30 adds 15% risk");
    }

    [Fact]
    public void GivenDiabetes_WhenCalculatingHypertensionRisk_ThenAdds42PercentRisk()
    {
        // Arrange & Act
        var noDiabetesRisk = _calculator.CalculateHypertensionRisk(age: 45, bmi: 25m, hasDiabetes: false);
        var diabetesRisk = _calculator.CalculateHypertensionRisk(age: 45, bmi: 25m, hasDiabetes: true);

        // Assert
        diabetesRisk.Should().BeApproximately(noDiabetesRisk + 0.423, 0.001, "diabetes adds 42.3% risk");
    }

    [Fact]
    public void GivenMultipleRiskFactors_WhenCalculatingHypertensionRisk_ThenCapsAt100Percent()
    {
        // Arrange & Act - 65-year-old obese diabetic
        var risk = _calculator.CalculateHypertensionRisk(age: 65, bmi: 35m, hasDiabetes: true);

        // Assert
        risk.Should().Be(1.0, "risk should be capped at 100%");
        // Calculation: 0.296 + 0.20 (age) + 0.15 (obesity) + 0.423 (diabetes) = 1.069 → capped at 1.0
    }

    #endregion

    #region Asthma Risk Tests

    [Fact]
    public void GivenChild_WhenCalculatingAsthmaRisk_ThenReturnsPediatricPrevalence()
    {
        // Arrange & Act
        var risk = _calculator.CalculateAsthmaRisk(age: 10, hasAllergies: false);

        // Assert
        risk.Should().BeApproximately(0.263, 0.001, "CDC pediatric asthma prevalence is 26.3%");
    }

    [Fact]
    public void GivenYoungAdult_WhenCalculatingAsthmaRisk_ThenReturnsPeakPrevalence()
    {
        // Arrange & Act
        var risk = _calculator.CalculateAsthmaRisk(age: 25, hasAllergies: false);

        // Assert
        risk.Should().BeApproximately(0.423, 0.001, "CDC shows peak prevalence 42.3% in ages 18-44");
    }

    [Fact]
    public void GivenMiddleAgedAdult_WhenCalculatingAsthmaRisk_ThenReturnsModeratePrevalence()
    {
        // Arrange & Act
        var risk = _calculator.CalculateAsthmaRisk(age: 50, hasAllergies: false);

        // Assert
        risk.Should().BeApproximately(0.351, 0.001, "CDC shows 35.1% prevalence in ages 45-64");
    }

    [Fact]
    public void GivenAllergies_WhenCalculatingAsthmaRisk_ThenIncreasesBy80Percent()
    {
        // Arrange & Act
        var noAllergiesRisk = _calculator.CalculateAsthmaRisk(age: 25, hasAllergies: false);
        var allergiesRisk = _calculator.CalculateAsthmaRisk(age: 25, hasAllergies: true);

        // Assert
        allergiesRisk.Should().Be(noAllergiesRisk * 1.8, "atopy increases risk by 80% (atopic march)");
    }

    [Fact]
    public void GivenAtopicYoungAdult_WhenCalculatingAsthmaRisk_ThenCapsAt100Percent()
    {
        // Arrange & Act
        var risk = _calculator.CalculateAsthmaRisk(age: 25, hasAllergies: true);

        // Assert
        risk.Should().Be(0.7614, "0.423 * 1.8 = 0.7614, under cap");
    }

    #endregion

    #region Cancer Risk Tests

    [Fact]
    public void GivenYoungAdult_WhenCalculatingCancerRisk_ThenReturnsLowRisk()
    {
        // Arrange & Act
        var risk = _calculator.CalculateCancerRisk(age: 25, smoker: false, familyHistory: false);

        // Assert
        risk.Should().Be(0.005, "SEER shows 0.5% risk under age 30");
    }

    [Fact]
    public void GivenAge50Plus_WhenCalculatingCancerRisk_ThenShowsExponentialIncrease()
    {
        // Arrange & Act
        var age40Risk = _calculator.CalculateCancerRisk(age: 45, smoker: false, familyHistory: false);
        var age60Risk = _calculator.CalculateCancerRisk(age: 60, smoker: false, familyHistory: false);

        // Assert
        age60Risk.Should().BeGreaterThan(age40Risk * 4, "risk accelerates exponentially after 50");
    }

    [Fact]
    public void GivenSmoker_WhenCalculatingCancerRisk_ThenIncreases2Point5Times()
    {
        // Arrange & Act
        var nonSmokerRisk = _calculator.CalculateCancerRisk(age: 60, smoker: false, familyHistory: false);
        var smokerRisk = _calculator.CalculateCancerRisk(age: 60, smoker: true, familyHistory: false);

        // Assert
        smokerRisk.Should().Be(nonSmokerRisk * 2.5, "tobacco increases cancer risk 2.5x");
    }

    [Fact]
    public void GivenFamilyHistory_WhenCalculatingCancerRisk_ThenIncreasesBy80Percent()
    {
        // Arrange & Act
        var noFamilyHistoryRisk = _calculator.CalculateCancerRisk(age: 60, smoker: false, familyHistory: false);
        var familyHistoryRisk = _calculator.CalculateCancerRisk(age: 60, smoker: false, familyHistory: true);

        // Assert
        familyHistoryRisk.Should().Be(noFamilyHistoryRisk * 1.8, "hereditary factors increase risk 80%");
    }

    [Fact]
    public void GivenElderlySmokerWithFamilyHistory_WhenCalculatingCancerRisk_ThenReturnsHighRisk()
    {
        // Arrange & Act
        var risk = _calculator.CalculateCancerRisk(age: 70, smoker: true, familyHistory: true);

        // Assert
        risk.Should().Be(1.0, "0.35 * 2.5 * 1.8 = 1.575, correctly capped at 1.0 (100%)");
        risk.Should().BeLessThanOrEqualTo(1.0, "risk should never exceed 100%");
    }

    #endregion

    #region Stroke Risk Tests

    [Fact]
    public void GivenYoungAdultNoRiskFactors_WhenCalculatingStrokeRisk_ThenReturnsLowRisk()
    {
        // Arrange & Act
        var risk = _calculator.CalculateStrokeRisk(age: 40, hasHypertension: false, hasDiabetes: false, smoker: false);

        // Assert
        risk.Should().Be(0.005, "stroke is rare under age 45 (0.5% baseline)");
    }

    [Fact]
    public void GivenOlderAdult_WhenCalculatingStrokeRisk_ThenShowsAgeRelatedIncrease()
    {
        // Arrange & Act
        var age50Risk = _calculator.CalculateStrokeRisk(age: 50, hasHypertension: false, hasDiabetes: false, smoker: false);
        var age70Risk = _calculator.CalculateStrokeRisk(age: 70, hasHypertension: false, hasDiabetes: false, smoker: false);

        // Assert
        age70Risk.Should().Be(0.10, "age 70 baseline is 10%");
        age50Risk.Should().Be(0.02, "age 50 baseline is 2%");
        age70Risk.Should().BeGreaterThan(age50Risk * 4, "stroke risk increases dramatically with age (5x: 0.10 vs 0.02)");
    }

    [Fact]
    public void GivenHypertension_WhenCalculatingStrokeRisk_ThenAdds8PercentRisk()
    {
        // Arrange & Act
        var noHypertensionRisk = _calculator.CalculateStrokeRisk(age: 60, hasHypertension: false, hasDiabetes: false, smoker: false);
        var hypertensionRisk = _calculator.CalculateStrokeRisk(age: 60, hasHypertension: true, hasDiabetes: false, smoker: false);

        // Assert
        hypertensionRisk.Should().Be(noHypertensionRisk + 0.08, "hypertension adds 8% stroke risk");
    }

    [Fact]
    public void GivenDiabetes_WhenCalculatingStrokeRisk_ThenAdds5PercentRisk()
    {
        // Arrange & Act
        var noDiabetesRisk = _calculator.CalculateStrokeRisk(age: 60, hasHypertension: false, hasDiabetes: false, smoker: false);
        var diabetesRisk = _calculator.CalculateStrokeRisk(age: 60, hasHypertension: false, hasDiabetes: true, smoker: false);

        // Assert
        diabetesRisk.Should().Be(noDiabetesRisk + 0.05, "diabetes adds 5% stroke risk");
    }

    [Fact]
    public void GivenSmoking_WhenCalculatingStrokeRisk_ThenAdds4PercentRisk()
    {
        // Arrange & Act
        var nonSmokerRisk = _calculator.CalculateStrokeRisk(age: 60, hasHypertension: false, hasDiabetes: false, smoker: false);
        var smokerRisk = _calculator.CalculateStrokeRisk(age: 60, hasHypertension: false, hasDiabetes: false, smoker: true);

        // Assert
        smokerRisk.Should().Be(nonSmokerRisk + 0.04, "smoking adds 4% stroke risk");
    }

    [Fact]
    public void GivenMultipleVascularRiskFactors_WhenCalculatingStrokeRisk_ThenAddsAllFactors()
    {
        // Arrange & Act - 70-year-old with hypertension, diabetes, and smoking
        var risk = _calculator.CalculateStrokeRisk(age: 70, hasHypertension: true, hasDiabetes: true, smoker: true);

        // Assert
        risk.Should().Be(0.10 + 0.08 + 0.05 + 0.04, "Framingham uses additive model: 0.10 + 0.08 + 0.05 + 0.04 = 0.27");
    }

    [Fact]
    public void GivenExtremeRiskProfile_WhenCalculatingStrokeRisk_ThenCapsAt100Percent()
    {
        // Arrange & Act - 80-year-old with all risk factors
        var risk = _calculator.CalculateStrokeRisk(age: 80, hasHypertension: true, hasDiabetes: true, smoker: true);

        // Assert
        risk.Should().BeLessThanOrEqualTo(1.0, "risk should never exceed 100%");
    }

    #endregion

    #region Cross-Disease Correlations

    [Fact]
    public void GivenDiabetes_WhenCalculatingHypertensionAndStrokeRisk_ThenShowsComorbidityCorrelation()
    {
        // Arrange & Act
        var hypertensionRisk = _calculator.CalculateHypertensionRisk(age: 60, bmi: 32m, hasDiabetes: true);
        var strokeRisk = _calculator.CalculateStrokeRisk(age: 60, hasHypertension: false, hasDiabetes: true, smoker: false);

        // Assert
        hypertensionRisk.Should().BeGreaterThan(0.296, "diabetes increases hypertension risk");
        strokeRisk.Should().BeGreaterThan(0.05, "diabetes increases stroke risk");
    }

    [Fact]
    public void GivenObesity_WhenCalculatingDiabetesAndHypertensionRisk_ThenBothIncreaseSubstantially()
    {
        // Arrange & Act
        var diabetesRisk = _calculator.CalculateDiabetesRisk(age: 50, smoker: false, bmi: 35m, familyHistory: false);
        var hypertensionRisk = _calculator.CalculateHypertensionRisk(age: 50, bmi: 35m, hasDiabetes: false);

        // Assert
        diabetesRisk.Should().BeGreaterThan(0.10, "obesity doubles diabetes baseline");
        hypertensionRisk.Should().BeGreaterThan(0.296, "obesity adds 15% to hypertension");
    }

    #endregion
}
