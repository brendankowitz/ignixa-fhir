// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Fluent builder for generating Group resources with member references.
/// Supports creating both actual groups (specific members) and descriptive groups (criteria-based).
/// </summary>
/// <remarks>
/// The Group resource is used to define collections of entities (usually patients, practitioners, devices, etc.)
/// that are grouped together for a common purpose such as research studies, care teams, or population health management.
///
/// Example Usage:
/// <code>
/// // Create a simple patient group
/// var group = GroupBuilder.Create(schemaProvider)
///     .WithType("person")
///     .WithActual(true)
///     .WithName("Diabetes Study Cohort")
///     .WithPatientMember("patient-1")
///     .WithPatientMember("patient-2")
///     .Build();
///
/// // Create a group with multiple members at once
/// var group = GroupBuilder.Create(schemaProvider)
///     .WithType("person")
///     .WithActual(true)
///     .WithMembers("patient-1", "patient-2", "patient-3")
///     .Build();
///
/// // Create a group with mixed resource types
/// var group = GroupBuilder.Create(schemaProvider)
///     .WithType("person")
///     .WithActual(true)
///     .WithMember("Patient", "patient-1")
///     .WithMember("Practitioner", "practitioner-1")
///     .Build();
/// </code>
///
/// Cross-Version Compatibility:
/// The builder uses IFhirSchemaProvider to generate base Group resources, ensuring compatibility
/// across FHIR R4, R4B, and R5 versions.
/// </remarks>
public sealed class GroupBuilder : FhirResourceBuilder<GroupBuilder>
{
    private string _type = "person";
    private bool _actual = true;
    private string? _name;
    private readonly List<(string ResourceType, string Id)> _members = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupBuilder"/> class.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="schemaProvider"/> is null.</exception>
    private GroupBuilder(IFhirSchemaProvider schemaProvider)
        : base(schemaProvider)
    {
    }

    /// <summary>
    /// Creates a new GroupBuilder instance.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for cross-version compatibility.</param>
    /// <returns>A new GroupBuilder instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="schemaProvider"/> is null.</exception>
    /// <example>
    /// <code>
    /// var builder = GroupBuilder.Create(schemaProvider)
    ///     .WithType("person")
    ///     .WithActual(true);
    /// </code>
    /// </example>
    public static GroupBuilder Create(IFhirSchemaProvider schemaProvider)
    {
        return new GroupBuilder(schemaProvider);
    }

    /// <summary>
    /// Sets the type of entities in the group.
    /// </summary>
    /// <param name="type">The group type. Valid values: "person", "animal", "practitioner", "device", "medication", "substance".</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
    /// <remarks>
    /// Common values:
    /// - "person": Group contains Patient or RelatedPerson resources
    /// - "practitioner": Group contains Practitioner or PractitionerRole resources
    /// - "device": Group contains Device resources
    /// - "medication": Group contains Medication resources
    /// - "substance": Group contains Substance resources
    /// - "animal": Group contains animal-related entities (less common)
    /// </remarks>
    /// <example>
    /// <code>
    /// var group = GroupBuilder.Create(schemaProvider)
    ///     .WithType("practitioner")
    ///     .WithActual(true)
    ///     .Build();
    /// </code>
    /// </example>
    public GroupBuilder WithType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
        return this;
    }

    /// <summary>
    /// Sets whether this is an actual group with specific members (true) or a descriptive group defined by characteristics (false).
    /// </summary>
    /// <param name="actual">
    /// True if this is an actual group with enumerated members.
    /// False if this is a descriptive group defined by characteristics/criteria.
    /// </param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// - Actual groups (true): Have explicit member references. Used for specific cohorts, care teams, etc.
    /// - Descriptive groups (false): Defined by characteristics/criteria. Used for population queries.
    ///
    /// Most test scenarios use actual=true with explicit member lists.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Actual group with specific members
    /// var actualGroup = GroupBuilder.Create(schemaProvider)
    ///     .WithActual(true)
    ///     .WithPatientMember("patient-1")
    ///     .Build();
    ///
    /// // Descriptive group (defined by characteristics)
    /// var descriptiveGroup = GroupBuilder.Create(schemaProvider)
    ///     .WithActual(false)
    ///     .WithName("All diabetic patients")
    ///     .Build();
    /// </code>
    /// </example>
    public GroupBuilder WithActual(bool actual)
    {
        _actual = actual;
        return this;
    }

    /// <summary>
    /// Sets the human-readable name of the group.
    /// </summary>
    /// <param name="name">The group name (e.g., "Diabetes Study Cohort", "Primary Care Team").</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    /// <example>
    /// <code>
    /// var group = GroupBuilder.Create(schemaProvider)
    ///     .WithName("Diabetes Study Cohort")
    ///     .WithType("person")
    ///     .Build();
    /// </code>
    /// </example>
    public GroupBuilder WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        return this;
    }

    /// <summary>
    /// Adds a member to the group with specified resource type and ID.
    /// </summary>
    /// <param name="resourceType">The resource type of the member (e.g., "Patient", "Practitioner", "Device").</param>
    /// <param name="id">The resource ID of the member.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resourceType"/> or <paramref name="id"/> is null.</exception>
    /// <remarks>
    /// This method can be called multiple times to add multiple members.
    /// Use this for groups with mixed resource types (e.g., both Patients and Practitioners).
    /// For patient-only groups, consider using <see cref="WithPatientMember"/> or <see cref="WithMembers"/> instead.
    /// </remarks>
    /// <example>
    /// <code>
    /// var group = GroupBuilder.Create(schemaProvider)
    ///     .WithType("person")
    ///     .WithMember("Patient", "patient-1")
    ///     .WithMember("Patient", "patient-2")
    ///     .WithMember("Practitioner", "practitioner-1")
    ///     .Build();
    /// </code>
    /// </example>
    public GroupBuilder WithMember(string resourceType, string id)
    {
        ArgumentNullException.ThrowIfNull(resourceType);
        ArgumentNullException.ThrowIfNull(id);
        _members.Add((resourceType, id));
        return this;
    }

    /// <summary>
    /// Adds a Patient member to the group.
    /// </summary>
    /// <param name="patientId">The Patient resource ID.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="patientId"/> is null.</exception>
    /// <remarks>
    /// This is a convenience method equivalent to calling <c>WithMember("Patient", patientId)</c>.
    /// </remarks>
    /// <example>
    /// <code>
    /// var group = GroupBuilder.Create(schemaProvider)
    ///     .WithType("person")
    ///     .WithPatientMember("patient-1")
    ///     .WithPatientMember("patient-2")
    ///     .Build();
    /// </code>
    /// </example>
    public GroupBuilder WithPatientMember(string patientId)
    {
        ArgumentNullException.ThrowIfNull(patientId);
        return WithMember("Patient", patientId);
    }

    /// <summary>
    /// Adds multiple Patient members to the group.
    /// </summary>
    /// <param name="patientIds">Array of Patient resource IDs to add as members.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="patientIds"/> is null.</exception>
    /// <remarks>
    /// This is a convenience method for adding multiple patients at once.
    /// Each ID is added as a Patient reference.
    /// </remarks>
    /// <example>
    /// <code>
    /// var group = GroupBuilder.Create(schemaProvider)
    ///     .WithType("person")
    ///     .WithActual(true)
    ///     .WithMembers("patient-1", "patient-2", "patient-3")
    ///     .Build();
    /// </code>
    /// </example>
    public GroupBuilder WithMembers(params string[] patientIds)
    {
        ArgumentNullException.ThrowIfNull(patientIds);
        foreach (var id in patientIds)
        {
            WithPatientMember(id);
        }
        return this;
    }

    /// <summary>
    /// Builds the Group resource with all configured properties.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the Group resource.</returns>
    /// <remarks>
    /// The built Group resource will include:
    /// - resourceType: "Group"
    /// - id: Auto-generated GUID if not set via WithId()
    /// - meta: Version and lastUpdated metadata (from base class)
    /// - type: The configured group type (default: "person")
    /// - actual: Whether this is an actual group (default: true)
    /// - name: Optional group name if set
    /// - member: Array of member references if any members were added
    ///
    /// Each member has the structure:
    /// <code>
    /// {
    ///   "entity": {
    ///     "reference": "{ResourceType}/{Id}"
    ///   }
    /// }
    /// </code>
    /// </remarks>
    public override ResourceJsonNode Build()
    {
        var groupJson = new JsonObject
        {
            ["resourceType"] = "Group",
            ["id"] = Id ?? Guid.NewGuid().ToString(),
            ["meta"] = BuildMeta(),
            ["type"] = _type,
            ["actual"] = _actual
        };

        if (!string.IsNullOrEmpty(_name))
        {
            groupJson["name"] = _name;
        }

        if (_members.Count > 0)
        {
            var membersArray = new JsonArray();
            foreach (var (resourceType, id) in _members)
            {
                membersArray.Add(new JsonObject
                {
                    ["entity"] = CreateReference(resourceType, id)
                });
            }
            groupJson["member"] = membersArray;
        }

        return JsonSourceNodeFactory.Parse<ResourceJsonNode>(groupJson);
    }
}
