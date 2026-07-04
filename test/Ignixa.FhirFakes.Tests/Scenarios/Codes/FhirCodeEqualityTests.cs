// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.Codes;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios.Codes;

public class FhirCodeEqualityTests
{
    [Fact]
    public void GivenSameSystemAndCode_WhenComparing_ThenEqualDespiteDomainAndDisplay()
    {
        var handBuilt = new FhirCode(FhirCode.Systems.SnomedCt, "44054006", "Diabetes mellitus type 2");

        handBuilt.Domain.ShouldBeNull();
        FhirCode.Conditions.DiabetesType2.Domain.ShouldBe(ClinicalDomain.Endocrinology);
        handBuilt.ShouldBe(FhirCode.Conditions.DiabetesType2);
    }

    [Fact]
    public void GivenDifferentCode_WhenComparing_ThenNotEqualEvenWithIdenticalDisplayAndDomain()
    {
        var first = new FhirCode(FhirCode.Systems.SnomedCt, "111111", "Same display")
        {
            Domain = ClinicalDomain.Cardiology,
        };
        var second = new FhirCode(FhirCode.Systems.SnomedCt, "222222", "Same display")
        {
            Domain = ClinicalDomain.Cardiology,
        };

        first.ShouldNotBe(second);
    }

    [Fact]
    public void GivenEqualCodes_WhenHashing_ThenHashCodesAgreeAndSetDeduplicates()
    {
        var handBuilt = new FhirCode(FhirCode.Systems.SnomedCt, "44054006", "Diabetes mellitus type 2");

        handBuilt.GetHashCode().ShouldBe(FhirCode.Conditions.DiabetesType2.GetHashCode());

        var set = new HashSet<FhirCode> { handBuilt, FhirCode.Conditions.DiabetesType2 };
        set.Count.ShouldBe(1);
    }
}
