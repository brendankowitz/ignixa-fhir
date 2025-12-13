// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Builders;

/// <summary>
/// Base class for all FHIR resource builders.
/// Provides common functionality like ID, tags, profile metadata, and helper methods for creating FHIR elements.
/// </summary>
/// <typeparam name="TBuilder">The concrete builder type (CRTP pattern for fluent API).</typeparam>
/// <remarks>
/// This base class uses the Curiously Recurring Template Pattern (CRTP) to enable fluent APIs in derived builders.
/// All derived builders automatically inherit:
/// - ID and tag management for test isolation
/// - Profile URL management for _profile searches
/// - Helper methods for creating common FHIR elements (Reference, CodeableConcept)
/// - Consistent meta element generation
///
/// Example Usage in Derived Builder:
/// <code>
/// public sealed class MyResourceBuilder : FhirResourceBuilder&lt;MyResourceBuilder&gt;
/// {
///     public MyResourceBuilder(IFhirSchemaProvider schemaProvider)
///         : base(schemaProvider) { }
///
///     public override ResourceJsonNode Build()
///     {
///         var resource = new JsonObject
///         {
///             ["resourceType"] = "MyResource",
///             ["id"] = _id ?? Guid.NewGuid().ToString(),
///             ["meta"] = BuildMeta(),  // From base class
///             // ... resource-specific fields
///         };
///
///         return JsonSourceNodeFactory.Parse&lt;ResourceJsonNode&gt;(resource.ToJsonString());
///     }
/// }
/// </code>
///
/// Example Usage by Test Code:
/// <code>
/// var resource = MyResourceBuilder.Create(schemaProvider)
///     .WithId("test-123")
///     .WithTag("my-test-tag")
///     .WithProfile("http://example.org/fhir/StructureDefinition/MyProfile")
///     .Build();
/// </code>
/// </remarks>
public abstract class FhirResourceBuilder<TBuilder>
    where TBuilder : FhirResourceBuilder<TBuilder>
{
    private readonly IFhirSchemaProvider _schemaProvider;
    private string? _id;
    private string? _tag;
    private readonly List<string> _profileUrls = [];

    /// <summary>
    /// Gets the FHIR schema provider for resource validation and schema access.
    /// </summary>
    protected IFhirSchemaProvider SchemaProvider => _schemaProvider;

    /// <summary>
    /// Gets the resource ID. If not set, Build() should generate a new GUID.
    /// </summary>
    protected string? Id => _id;

    /// <summary>
    /// Gets the tag for test isolation.
    /// </summary>
    protected string? Tag => _tag;

    /// <summary>
    /// Gets the profile URLs to add to meta.profile.
    /// </summary>
    protected IReadOnlyList<string> ProfileUrls => _profileUrls;

    /// <summary>
    /// Initializes a new instance of the <see cref="FhirResourceBuilder{TBuilder}"/> class.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="schemaProvider"/> is null.</exception>
    protected FhirResourceBuilder(IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        _schemaProvider = schemaProvider;
    }

    /// <summary>
    /// Sets the resource ID.
    /// </summary>
    /// <param name="id">The resource ID to set.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
    /// <example>
    /// <code>
    /// var patient = PatientBuilder.Create(schemaProvider)
    ///     .WithId("patient-123")
    ///     .Build();
    /// </code>
    /// </example>
    public TBuilder WithId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        _id = id;
        return (TBuilder)this;
    }

    /// <summary>
    /// Sets a tag for test isolation.
    /// Tag will be added to meta.tag with system "http://ignixa.dev/test-isolation".
    /// </summary>
    /// <param name="tag">The tag code to set.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tag"/> is null.</exception>
    /// <example>
    /// <code>
    /// var testTag = Guid.NewGuid().ToString();
    /// var patient = PatientBuilder.Create(schemaProvider)
    ///     .WithTag(testTag)
    ///     .Build();
    ///
    /// // Later in test:
    /// var results = await Harness.SearchAsync("Patient", $"_tag={testTag}");
    /// </code>
    /// </example>
    public TBuilder WithTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _tag = tag;
        return (TBuilder)this;
    }

    /// <summary>
    /// Adds a profile URL to meta.profile.
    /// Can be called multiple times to add multiple profiles.
    /// </summary>
    /// <param name="profileUrl">The profile URL to add.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profileUrl"/> is null.</exception>
    /// <example>
    /// <code>
    /// var patient = PatientBuilder.Create(schemaProvider)
    ///     .WithProfile("http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient")
    ///     .WithProfile("http://example.org/fhir/StructureDefinition/custom-patient")
    ///     .Build();
    ///
    /// // Search by profile:
    /// var results = await Harness.SearchAsync("Patient",
    ///     "_profile=http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient");
    /// </code>
    /// </example>
    public TBuilder WithProfile(string profileUrl)
    {
        ArgumentNullException.ThrowIfNull(profileUrl);
        _profileUrls.Add(profileUrl);
        return (TBuilder)this;
    }

    /// <summary>
    /// Builds the meta element with version, lastUpdated, tag, and profile.
    /// </summary>
    /// <returns>A JsonObject representing the meta element.</returns>
    /// <remarks>
    /// The meta element always includes:
    /// - versionId: "1"
    /// - lastUpdated: current UTC time in ISO 8601 format
    ///
    /// Optionally includes (if set via WithTag/WithProfile):
    /// - tag: array with test isolation tag
    /// - profile: array of profile URLs
    /// </remarks>
    protected JsonObject BuildMeta()
    {
        var meta = new JsonObject
        {
            ["versionId"] = "1",
            ["lastUpdated"] = DateTime.UtcNow.ToString("o")
        };

        if (_tag is not null)
        {
            meta["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://ignixa.dev/test-isolation",
                    ["code"] = _tag
                }
            };
        }

        if (_profileUrls.Count > 0)
        {
            meta["profile"] = new JsonArray(
                _profileUrls.Select(u => JsonValue.Create(u)).ToArray());
        }

        return meta;
    }

    /// <summary>
    /// Creates a FHIR Reference JSON object.
    /// </summary>
    /// <param name="resourceType">The referenced resource type (e.g., "Patient", "Organization").</param>
    /// <param name="id">The referenced resource ID.</param>
    /// <returns>A JsonObject representing a FHIR Reference with reference field set to "{resourceType}/{id}".</returns>
    /// <example>
    /// <code>
    /// // In derived builder's Build() method:
    /// var patientJson = new JsonObject
    /// {
    ///     ["managingOrganization"] = CreateReference("Organization", organizationId)
    /// };
    /// </code>
    /// </example>
    protected static JsonObject CreateReference(string resourceType, string id)
    {
        return new JsonObject
        {
            ["reference"] = $"{resourceType}/{id}"
        };
    }

    /// <summary>
    /// Creates a FHIR CodeableConcept JSON object.
    /// </summary>
    /// <param name="code">The code value.</param>
    /// <param name="system">The code system URI.</param>
    /// <param name="display">Optional display text for the code.</param>
    /// <param name="text">Optional free text representation of the concept.</param>
    /// <returns>A JsonObject representing a FHIR CodeableConcept.</returns>
    /// <remarks>
    /// Creates a CodeableConcept with a single coding entry.
    /// The structure is:
    /// <code>
    /// {
    ///   "coding": [
    ///     {
    ///       "system": "{system}",
    ///       "code": "{code}",
    ///       "display": "{display}"  // optional
    ///     }
    ///   ],
    ///   "text": "{text}"  // optional
    /// }
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// // In derived builder's Build() method:
    /// var obsJson = new JsonObject
    /// {
    ///     ["code"] = CreateCodeableConcept(
    ///         "4548-4",
    ///         "http://loinc.org",
    ///         "Hemoglobin A1c",
    ///         "HbA1c")
    /// };
    /// </code>
    /// </example>
    protected static JsonObject CreateCodeableConcept(
        string code,
        string system,
        string? display = null,
        string? text = null)
    {
        var coding = new JsonObject
        {
            ["system"] = system,
            ["code"] = code
        };

        if (display is not null)
        {
            coding["display"] = display;
        }

        var concept = new JsonObject
        {
            ["coding"] = new JsonArray { coding }
        };

        if (text is not null)
        {
            concept["text"] = text;
        }

        return concept;
    }

    /// <summary>
    /// Builds the FHIR resource as a ResourceJsonNode.
    /// Must be implemented by derived classes to provide resource-specific construction logic.
    /// </summary>
    /// <returns>A ResourceJsonNode representing the built FHIR resource.</returns>
    /// <remarks>
    /// Implementation should:
    /// 1. Create a JsonObject with the resource structure
    /// 2. Set resourceType field
    /// 3. Use _id ?? Guid.NewGuid().ToString() for the id field
    /// 4. Call BuildMeta() for the meta field
    /// 5. Add resource-specific fields from private builder state
    /// 6. Parse to ResourceJsonNode using JsonSourceNodeFactory.Parse
    /// </remarks>
    public abstract ResourceJsonNode Build();
}
