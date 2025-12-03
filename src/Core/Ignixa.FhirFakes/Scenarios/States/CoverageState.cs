// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bogus;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates a Coverage resource.
/// Coverage represents health insurance information with member ID and payor references.
/// </summary>
public sealed class CoverageState : ScenarioState
{
    private readonly Faker _faker = new();

    /// <summary>
    /// Gets or sets the coverage status ("active", "cancelled", "draft", "entered-in-error").
    /// Default is "active".
    /// </summary>
    public string Status { get; init; } = "active";

    /// <summary>
    /// Gets or sets the member ID.
    /// If not specified, a realistic member ID is auto-generated (format: 3 letters + 9 digits).
    /// </summary>
    public string? MemberId { get; init; }

    /// <summary>
    /// Gets or sets the insurance group ID.
    /// </summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// Gets or sets the subscriber ID.
    /// If not specified, defaults to the member ID.
    /// </summary>
    public string? SubscriberId { get; init; }

    /// <summary>
    /// Gets or sets the relationship to subscriber ("self", "spouse", "child", "parent", "other").
    /// Default is "self".
    /// </summary>
    public string Relationship { get; init; } = "self";

    /// <summary>
    /// Gets or sets the coverage start date.
    /// If not specified, defaults to the patient's birth date.
    /// </summary>
    public DateTime? StartDate { get; init; }

    /// <summary>
    /// Gets or sets the coverage end date.
    /// If null, coverage is ongoing (no end date).
    /// </summary>
    public DateTime? EndDate { get; init; }

    /// <summary>
    /// Gets or sets the payor Organization reference.
    /// If not specified, uses the current organization from the context.
    /// </summary>
    public ResourceJsonNode? Payor { get; init; }

    /// <summary>
    /// Gets or sets the dependent number (for dependents on a family plan).
    /// </summary>
    public int? Dependent { get; init; }

    /// <summary>
    /// Gets or sets the coverage type code.
    /// Uses http://terminology.hl7.org/CodeSystem/v3-ActCode.
    /// Common values: "EHCPOL" (extended healthcare), "PUBLICPOL" (public healthcare), etc.
    /// </summary>
    public string? TypeCode { get; init; }

    /// <summary>
    /// Gets or sets the coverage type display.
    /// </summary>
    public string? TypeDisplay { get; init; }

    /// <summary>
    /// Creates a Coverage resource linked to the patient.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create Coverage without a Patient. Ensure InitialState runs first.");
        }

        var coverage = faker.Generate("Coverage");
        var node = coverage.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();

        // Set status
        node["status"] = Status;

        // Generate or use provided member ID
        var memberId = MemberId ?? GenerateMemberId();

        // Set identifier (member ID)
        node["identifier"] = new JsonArray
        {
            new JsonObject
            {
                ["system"] = "http://example.org/memberid",
                ["value"] = memberId
            }
        };

        // Set type
        if (!string.IsNullOrEmpty(TypeCode))
        {
            node["type"] = new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/v3-ActCode",
                        ["code"] = TypeCode,
                        ["display"] = TypeDisplay ?? TypeCode
                    }
                }
            };
        }

        // Set policyHolder (patient reference - who owns the policy)
        if (Relationship == "self")
        {
            node["policyHolder"] = new JsonObject
            {
                ["reference"] = $"Patient/{context.Patient.Id}"
            };
        }

        // Set subscriber (who is subscribed to the insurance)
        var subscriberId = SubscriberId ?? memberId;
        node["subscriber"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient.Id}",
            ["identifier"] = new JsonObject
            {
                ["system"] = "http://example.org/memberid",
                ["value"] = subscriberId
            }
        };

        // Set beneficiary (patient reference - who benefits from the coverage)
        node["beneficiary"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient.Id}"
        };

        // Set relationship
        node["relationship"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://terminology.hl7.org/CodeSystem/subscriber-relationship",
                    ["code"] = Relationship,
                    ["display"] = MapRelationshipToDisplay(Relationship)
                }
            }
        };

        // Set period (start/end dates)
        var startDate = StartDate ?? context.BirthDate;
        var periodNode = new JsonObject
        {
            ["start"] = startDate.ToString("yyyy-MM-dd")
        };
        if (EndDate.HasValue)
        {
            periodNode["end"] = EndDate.Value.ToString("yyyy-MM-dd");
        }
        node["period"] = periodNode;

        // Set payor (Organization reference)
        if (Payor is not null)
        {
            node["payor"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference"] = $"{Payor.ResourceType}/{Payor.Id}"
                }
            };
        }
        else
        {
            // Use a generic payor display if no organization is available
            node["payor"] = new JsonArray
            {
                new JsonObject
                {
                    ["display"] = "Health Insurance Company"
                }
            };
        }

        // Set class (for group ID and dependent number)
        var classArray = new JsonArray();
        if (!string.IsNullOrEmpty(GroupId))
        {
            classArray.Add(new JsonObject
            {
                ["type"] = new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = "http://terminology.hl7.org/CodeSystem/coverage-class",
                            ["code"] = "group"
                        }
                    }
                },
                ["value"] = GroupId,
                ["name"] = "Insurance Group"
            });
        }

        if (Dependent.HasValue)
        {
            classArray.Add(new JsonObject
            {
                ["type"] = new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = "http://terminology.hl7.org/CodeSystem/coverage-class",
                            ["code"] = "subgroup"
                        }
                    }
                },
                ["value"] = Dependent.Value.ToString(),
                ["name"] = "Dependent"
            });
        }

        if (classArray.Count > 0)
        {
            node["class"] = classArray;
        }

        // Add to context
        var typeDescription = TypeDisplay ?? TypeCode ?? "Health Insurance";
        context.AddCoverage(coverage, typeDescription);
    }

    /// <summary>
    /// Generates a realistic member ID in the format: 3 letters + 9 digits with check digit.
    /// Example: "ABC123456789"
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    private string GenerateMemberId()
    {
        var prefix = _faker.Random.String2(3, "ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        var digits = _faker.Random.Long(100000000, 999999999);
        return $"{prefix}{digits}";
    }

    private static string MapRelationshipToDisplay(string relationship) => relationship.ToUpperInvariant() switch
    {
        "SELF" => "Self",
        "SPOUSE" => "Spouse",
        "CHILD" => "Child",
        "PARENT" => "Parent",
        "OTHER" => "Other",
        _ => relationship
    };

    #region Factory Methods

    /// <summary>
    /// Creates a self-insured coverage (patient is the subscriber).
    /// </summary>
    public static CoverageState SelfCoverage() => new()
    {
        Name = "Self Coverage",
        Relationship = "self",
        TypeCode = "EHCPOL",
        TypeDisplay = "Extended healthcare"
    };

    /// <summary>
    /// Creates a child coverage (patient is a dependent on parent's plan).
    /// </summary>
    /// <param name="dependent">The dependent number (default 1).</param>
    public static CoverageState ChildCoverage(int dependent = 1) => new()
    {
        Name = "Child Coverage",
        Relationship = "child",
        Dependent = dependent,
        TypeCode = "EHCPOL",
        TypeDisplay = "Extended healthcare"
    };

    /// <summary>
    /// Creates a spouse coverage (patient is covered under spouse's plan).
    /// </summary>
    public static CoverageState SpouseCoverage() => new()
    {
        Name = "Spouse Coverage",
        Relationship = "spouse",
        TypeCode = "EHCPOL",
        TypeDisplay = "Extended healthcare"
    };

    /// <summary>
    /// Creates a Medicare coverage (US public healthcare for seniors).
    /// </summary>
    public static CoverageState Medicare() => new()
    {
        Name = "Medicare Coverage",
        Relationship = "self",
        TypeCode = "PUBLICPOL",
        TypeDisplay = "Public healthcare"
    };

    /// <summary>
    /// Creates a Medicaid coverage (US public healthcare for low-income).
    /// </summary>
    public static CoverageState Medicaid() => new()
    {
        Name = "Medicaid Coverage",
        Relationship = "self",
        TypeCode = "PUBLICPOL",
        TypeDisplay = "Public healthcare"
    };

    /// <summary>
    /// Creates a dental insurance coverage.
    /// </summary>
    public static CoverageState Dental() => new()
    {
        Name = "Dental Coverage",
        Relationship = "self",
        TypeCode = "DENTAL",
        TypeDisplay = "Dental program"
    };

    /// <summary>
    /// Creates a vision insurance coverage.
    /// </summary>
    public static CoverageState Vision() => new()
    {
        Name = "Vision Coverage",
        Relationship = "self",
        TypeCode = "VISPOL",
        TypeDisplay = "Vision program"
    };

    #endregion
}
