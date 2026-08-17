// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Shared container-scoping JSON fixtures used by both <see cref="ReferenceIndexTests"/> (unit
/// level) and <see cref="ResolveFunctionTests"/> (end-to-end through the evaluator), so the two
/// suites exercise the exact same instance shape and cannot drift apart.
/// </summary>
internal static class ContainerScopeTestFixtures
{
    /// <summary>
    /// A Bundle with two entries whose contained resources share the id <c>org1</c> but have
    /// different names, so a fragment resolved from one entry can be told apart from the other.
    /// </summary>
    public const string BundleWithTwoEntriesSharingContainedIdJson = @"{
        ""resourceType"": ""Bundle"",
        ""type"": ""collection"",
        ""entry"": [
            {
                ""resource"": {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patA"",
                    ""managingOrganization"": { ""reference"": ""#org1"" },
                    ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""OrgA"" } ]
                }
            },
            {
                ""resource"": {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patB"",
                    ""managingOrganization"": { ""reference"": ""#org1"" },
                    ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""OrgB"" } ]
                }
            }
        ]
    }";

    /// <summary>
    /// A Parameters resource with a contained fragment at the top-level <c>parameter.resource</c>
    /// and another nested under <c>parameter.part.resource</c>, both sharing the contained id
    /// <c>org1</c> under different names.
    /// </summary>
    public const string ParametersWithContainedFragmentsJson = @"{
        ""resourceType"": ""Parameters"",
        ""parameter"": [
            {
                ""name"": ""top"",
                ""resource"": {
                    ""resourceType"": ""Patient"",
                    ""id"": ""ptop"",
                    ""managingOrganization"": { ""reference"": ""#org1"" },
                    ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""TopOrg"" } ]
                }
            },
            {
                ""name"": ""group"",
                ""part"": [
                    {
                        ""name"": ""nested"",
                        ""resource"": {
                            ""resourceType"": ""Patient"",
                            ""id"": ""pnested"",
                            ""managingOrganization"": { ""reference"": ""#org1"" },
                            ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""NestedOrg"" } ]
                        }
                    }
                ]
            }
        ]
    }";

    /// <summary>
    /// A Bundle where only entry A contains an Organization with id <c>org1</c>; entry B references
    /// <c>#org1</c> but declares no <c>contained</c> of its own. Per FHIR R4 references.html
    /// §2.3.0.8 ("References to contained resources are never resolved outside the container
    /// resource"), resolving <c>#org1</c> from entry B's scope must be empty - it must never leak
    /// entry A's contained Organization.
    /// </summary>
    public const string BundleWhereOnlyOneEntryHasContainedIdJson = @"{
        ""resourceType"": ""Bundle"",
        ""type"": ""collection"",
        ""entry"": [
            {
                ""resource"": {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patA"",
                    ""managingOrganization"": { ""reference"": ""#org1"" },
                    ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""OrgA"" } ]
                }
            },
            {
                ""resource"": {
                    ""resourceType"": ""Patient"",
                    ""id"": ""patB"",
                    ""managingOrganization"": { ""reference"": ""#org1"" }
                }
            }
        ]
    }";

    /// <summary>
    /// The Parameters equivalent of <see cref="BundleWhereOnlyOneEntryHasContainedIdJson"/>: only
    /// the top-level <c>parameter.resource</c> contains an Organization with id <c>org1</c>; the
    /// resource nested under <c>parameter.part.resource</c> references <c>#org1</c> but declares no
    /// <c>contained</c> of its own.
    /// </summary>
    public const string ParametersWhereOnlyOneEntryHasContainedIdJson = @"{
        ""resourceType"": ""Parameters"",
        ""parameter"": [
            {
                ""name"": ""top"",
                ""resource"": {
                    ""resourceType"": ""Patient"",
                    ""id"": ""ptop"",
                    ""managingOrganization"": { ""reference"": ""#org1"" },
                    ""contained"": [ { ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""TopOrg"" } ]
                }
            },
            {
                ""name"": ""group"",
                ""part"": [
                    {
                        ""name"": ""nested"",
                        ""resource"": {
                            ""resourceType"": ""Patient"",
                            ""id"": ""pnested"",
                            ""managingOrganization"": { ""reference"": ""#org1"" }
                        }
                    }
                ]
            }
        ]
    }";

    /// <summary>
    /// A Bundle with 11 entries where entry[1] and entry[10] each contain an Organization with the
    /// same id <c>org1</c> but different names. Regression coverage for <c>SelectContainedPool</c>'s
    /// longest-prefix loop across many candidate scopes (including a two-digit index) - each entry
    /// must still resolve to its own contained Organization. Note this does NOT exercise the
    /// <c>IsInScope</c> trailing-boundary check: <c>"Bundle.entry[10].resource"</c> is not a plain
    /// string-prefix of <c>"Bundle.entry[1].resource"</c> (or vice versa) at all - the closing
    /// <c>']'</c> diverges from the next index digit immediately, so plain <c>StartsWith</c> alone
    /// already separates every bracket-indexed sibling regardless of digit count. See
    /// <c>GivenFocusLocationSharingContainerPrefixWithoutDotBoundary_...</c> in
    /// <c>ReferenceIndexTests</c> for a test that genuinely exercises that guard.
    /// </summary>
    public static string BundleWithElevenEntriesSharingContainedIdAtEntryOneAndTenJson { get; } = BuildBundleWithElevenEntries();

    private static string BuildBundleWithElevenEntries()
    {
        var entries = new List<string>();
        for (var i = 0; i < 11; i++)
        {
            entries.Add(i switch
            {
                1 => BuildEntryWithContainedOrg("pat1", "OrgAtEntryOne"),
                10 => BuildEntryWithContainedOrg("pat10", "OrgAtEntryTen"),
                _ => $@"{{ ""resource"": {{ ""resourceType"": ""Patient"", ""id"": ""pat{i}"" }} }}"
            });
        }

        return $@"{{ ""resourceType"": ""Bundle"", ""type"": ""collection"", ""entry"": [{string.Join(",", entries)}] }}";

        static string BuildEntryWithContainedOrg(string patientId, string orgName) => $@"{{
            ""resource"": {{
                ""resourceType"": ""Patient"",
                ""id"": ""{patientId}"",
                ""managingOrganization"": {{ ""reference"": ""#org1"" }},
                ""contained"": [ {{ ""resourceType"": ""Organization"", ""id"": ""org1"", ""name"": ""{orgName}"" }} ]
            }}
        }}";
    }
}
