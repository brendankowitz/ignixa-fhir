// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Ignixa.FhirFakes.Scenarios.Codes;
using Shouldly;

namespace Ignixa.FhirFakes.Tests;

/// <summary>
/// Verifies BindingCodeMapper's lazily-cached GetAll*Codes() pools are genuinely immutable and
/// genuinely cached: the returned type cannot be mutated (compile-time guarantee of ImmutableArray&lt;T&gt;),
/// and repeated calls reuse the same underlying array rather than recomputing it via reflection each time.
/// </summary>
public class BindingCodeMapperCacheTests
{
    [Theory]
    [MemberData(nameof(AllGetAllMethods))]
    public void GetAllCodes_WhenCalledTwice_ThenReturnsSameUnderlyingArrayInstance(string name, Func<ImmutableArray<FhirCode>> getter)
    {
        // Act
        var first = getter();
        var second = getter();

        // Assert - ImmutableArray<T> wraps a single backing array; if the cache were recomputed on
        // every call (defeating the point of lazy caching), these would be different array instances.
        ReferenceEquals(ImmutableCollectionsMarshal.AsArray(first), ImmutableCollectionsMarshal.AsArray(second))
            .ShouldBeTrue($"{name} should reuse its cached array across calls, not recompute it via reflection each time");
    }

    public static IEnumerable<object[]> AllGetAllMethods()
    {
        yield return new object[] { nameof(BindingCodeMapper.GetAllAllergenCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllAllergenCodes };
        yield return new object[] { nameof(BindingCodeMapper.GetAllImmunizationCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllImmunizationCodes };
        yield return new object[] { nameof(BindingCodeMapper.GetAllLabObservationCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllLabObservationCodes };
        yield return new object[] { nameof(BindingCodeMapper.GetAllProcedureCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllProcedureCodes };
        yield return new object[] { nameof(BindingCodeMapper.GetAllVitalSignCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllVitalSignCodes };
        yield return new object[] { nameof(BindingCodeMapper.GetAllDiagnosticReportCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllDiagnosticReportCodes };
        yield return new object[] { nameof(BindingCodeMapper.GetAllMedicationCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllMedicationCodes };
        yield return new object[] { nameof(BindingCodeMapper.GetAllConditionCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllConditionCodes };
        yield return new object[] { nameof(BindingCodeMapper.GetAllEncounterTypeCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllEncounterTypeCodes };
        yield return new object[] { nameof(BindingCodeMapper.GetAllObservationCodes), (Func<ImmutableArray<FhirCode>>)BindingCodeMapper.GetAllObservationCodes };
    }
}
