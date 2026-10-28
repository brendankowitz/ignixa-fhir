// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;

namespace Ignixa.Search.Tests.Indexing;

/// <summary>
/// Guards: implicit systems <see cref="CodeSystemResolver"/> already gave correctly at bdb927b, which the generator must
/// keep. Each row passes there and was mutation-checked; the regressions live in <see cref="CodeSystemResolverTests"/>.
/// </summary>
public class CodeSystemResolverGuardTests
{
    [Theory]
    [InlineData(FhirVersion.R4, "Patient.language", "urn:ietf:bcp:47")]
    [InlineData(FhirVersion.Stu3, "Patient.language", "urn:ietf:bcp:47")]
    [InlineData(FhirVersion.R4B, "Observation.language", "urn:ietf:bcp:47")]
    public void GivenAResourceLanguage_WhenResolved_ThenItIsBcp47(FhirVersion version, string path, string expected)
    {
        // STU3 binds Resource.language as extensible and R4/R4B as preferred, each with all-languages (BCP-47) as its
        // required maximum value set, which is what implies the system.
        new CodeSystemResolver(version).ResolveSystem(path).ShouldBe(expected);
    }
}
