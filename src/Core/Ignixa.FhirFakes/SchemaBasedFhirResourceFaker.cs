// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Bogus;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using FhirCode = Ignixa.FhirFakes.Scenarios.Codes.FhirCode;

namespace Ignixa.FhirFakes;

/// <summary>
/// Schema-driven fake FHIR resource generator.
/// Uses ISchema metadata to intelligently generate realistic test data.
/// </summary>
/// <remarks>
/// APPROACH:
/// 1. Get IType from schema for the resource type
/// 2. Iterate through Children to find required vs optional elements
/// 3. Use BINDING INFORMATION from ITypeExtended to select appropriate codes (preferred)
/// 4. Fall back to element name + datatype heuristics when bindings unavailable
/// 5. Generate JSON structure respecting cardinality (IsCollection, IsRequired)
///
/// BINDING-AWARE GENERATION (New - preferred approach):
/// - Elements with bindings are matched to predefined code constants via BindingCodeMapper
/// - Binding strength is respected: required bindings use only value set codes
/// - Falls back to heuristics when no binding or no matching codes available
///
/// MATCHING STRATEGY (fallback):
/// - Element name contains "name" -> Faker.Name methods
/// - Element name contains "address" -> Faker.Address methods
/// - Element name contains "phone" or "telecom" -> Faker.Phone methods
/// - Element name contains "email" -> Faker.Internet.Email()
/// - Element name contains "date" or "birth" -> Faker.Date methods
/// - Element name contains "id" -> Faker.Random.Guid()
/// - Datatype "code" + binding -> Pick from binding value set
/// - Datatype "Reference" + referenceTargets -> Generate valid reference
/// </remarks>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Random is used for test data generation only")]
public class SchemaBasedFhirResourceFaker
{
    private const int MaxRecursionDepth = 5; // Prevent infinite recursion in complex type generation

    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly Faker _faker;
    private readonly Random _random;
    private readonly int? _seed;
    private string? _tag;

    /// <summary>
    /// Gets the FHIR schema provider used by this faker.
    /// Exposed to allow states to access version information for version-specific logic.
    /// </summary>
    public IFhirSchemaProvider SchemaProvider => _schemaProvider;

    /// <summary>
    /// Gets the tag code applied to resources, if any.
    /// Exposed to allow states like PatientBuilderState to apply the same tag to their generated resources.
    /// </summary>
    public string? Tag => _tag;

    /// <summary>
    /// Controls how densely generated resources are populated. Defaults to
    /// <see cref="GenerationDensity.Minimal"/>, which preserves the required-only behavior.
    /// </summary>
    public GenerationDensity Density { get; set; } = GenerationDensity.Minimal;

    public SchemaBasedFhirResourceFaker(IFhirSchemaProvider schemaProvider)
    {
        _schemaProvider = schemaProvider;
        _faker = new Faker();
        _random = new Random();
    }

    /// <summary>
    /// Creates a faker whose randomness is seeded for reproducible generation.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for the desired FHIR version.</param>
    /// <param name="seed">The seed applied to the internal randomizers and propagated to the Patient builder.</param>
    /// <remarks>
    /// The seed is propagated to the <see cref="PatientBuilder"/> created by
    /// <see cref="CreatePatient"/> and <see cref="CreateSeattlePatient"/> so the base Patient
    /// generation path is reproducible. The generic <see cref="Generate(string)"/> path is seeded
    /// via the internal randomizers only.
    /// <para>
    /// Output is byte-reproducible from the seed EXCEPT <c>meta.lastUpdated</c>, which
    /// <see cref="Generate(string)"/> stamps with wall-clock <see cref="DateTime.UtcNow"/>. A reader
    /// must not infer byte-identical output from a seed alone.
    /// </para>
    /// </remarks>
    public SchemaBasedFhirResourceFaker(IFhirSchemaProvider schemaProvider, int seed)
    {
        _schemaProvider = schemaProvider;
        _faker = new Faker { Random = new Randomizer(seed) };
        _random = new Random(seed);
        _seed = seed;
    }

    /// <summary>
    /// Configures this faker to tag all generated resources with the specified tag code.
    /// This enables test isolation via the _tag search parameter.
    /// </summary>
    /// <param name="tag">The tag code to apply (typically a GUID for test isolation).</param>
    /// <returns>This faker instance for fluent chaining.</returns>
    public SchemaBasedFhirResourceFaker WithTag(string? tag)
    {
        _tag = tag;
        return this;
    }

    #region PatientBuilder Convenience Methods

    /// <summary>
    /// Creates a simple patient using PatientBuilder with basic Bogus-based randomization.
    /// Suitable for basic tests where demographic realism is not critical.
    /// </summary>
    /// <param name="configure">Optional configuration action to customize the patient.</param>
    /// <returns>A ResourceJsonNode representing the generated Patient resource.</returns>
    /// <example>
    /// <code>
    /// var patient = faker.CreatePatient(p => p
    ///     .WithAge(45)
    ///     .WithGender(g => g.Male)
    ///     .WithGivenName("John")
    ///     .WithFamilyName("Smith"));
    /// </code>
    /// </example>
    public ResourceJsonNode CreatePatient(Action<PatientBuilder>? configure = null)
    {
        var builder = PatientBuilderFactory.Create(_schemaProvider, _seed);

        // Apply tag from faker if set
        if (_tag is not null)
        {
            builder.WithTag(_tag);
        }

        // Apply user configuration
        configure?.Invoke(builder);

        return builder.Build();
    }

    /// <summary>
    /// Creates a patient from Seattle, Washington with realistic Pacific Northwest demographics.
    /// Seattle is special and deserves its own method.
    /// </summary>
    /// <param name="configure">Optional configuration action to customize the patient after city selection.</param>
    /// <returns>A ResourceJsonNode representing the generated Patient resource.</returns>
    /// <example>
    /// <code>
    /// var patient = faker.CreateSeattlePatient(p => p
    ///     .WithAge(35)
    ///     .WithRealisticBMI());
    /// </code>
    /// </example>
    public ResourceJsonNode CreateSeattlePatient(Action<PatientBuilder>? configure = null)
    {
        var builder = PatientBuilderFactory.Create(_schemaProvider, _seed)
            .FromSeattle();

        // Apply tag from faker if set
        if (_tag is not null)
        {
            builder.WithTag(_tag);
        }

        // Apply user configuration (allows overrides after Seattle defaults)
        configure?.Invoke(builder);

        return builder.Build();
    }

    #endregion

    /// <summary>
    /// Generates a fake FHIR resource by resource type name. Population is governed by
    /// <see cref="Density"/>: required elements only by default (Minimal/Realistic); under
    /// <see cref="GenerationDensity.Maximum"/> every optional element is included as well. Inclusion
    /// is deterministic for a given density — optionals are never sampled at random.
    /// </summary>
    public ResourceJsonNode Generate(string resourceType)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        if (!_schemaProvider.ResourceTypeNames.Contains(resourceType))
        {
            throw new ArgumentException($"Resource type '{resourceType}' is not valid for FHIR version {_schemaProvider.Version}", nameof(resourceType));
        }

        var typeDefinition = _schemaProvider.GetTypeDefinition(resourceType);
        if (typeDefinition is null)
        {
            throw new InvalidOperationException($"Could not retrieve type definition for '{resourceType}'");
        }

        var root = new JsonObject
        {
            ["resourceType"] = resourceType,
            ["id"] = _faker.Random.Guid().ToString()
        };

        // Add meta
        root["meta"] = new JsonObject
        {
            ["versionId"] = "1",
            ["lastUpdated"] = DateTime.UtcNow.ToString("o")
        };

        // Iterate through children and fill elements
        foreach (var child in typeDefinition.Children)
        {
            var elementName = child.Info.Name;

            // Skip meta elements (already added), text, contained, extension, modifierExtension (complex)
            // At root level, also skip "language" (it's metadata)
            if (IsSkippableElement(elementName, isRootResourceLevel: true))
            {
                continue;
            }

            // Handle choice elements (value[x] pattern)
            // For choice elements, we need to pick a type and append it to the element name
            var (actualElementName, actualElement) = ResolveChoiceElement(child);

            // Minimal/Realistic: required elements only. Maximum: required plus optional.
            if (!ShouldPopulate(child))
            {
                continue;
            }

            var value = GenerateElementValue(actualElement, resourceType, depth: 0);
            if (value is not null)
            {
                root[actualElementName] = value;
            }
            else if (child.IsRequired && !actualElement.Info.IsPrimitive)
            {
                // Placeholders satisfy required cardinality only; optional elements that
                // cannot generate a value are simply skipped.
                root[actualElementName] = new JsonObject();
            }
        }

        var json = root.ToJsonString();
        var resource = JsonSourceNodeFactory.Parse(json);
        ApplyTag(resource);
        return resource;
    }

    /// <summary>
    /// Generates a value for a specific element based on its type and cardinality.
    /// </summary>
    private JsonNode? GenerateElementValue(IType element, string parentResourceType, int depth = 0)
    {
        var elementName = element.Info.Name;

        // Handle collections
        if (element.IsCollection)
        {
            var arraySize = _random.Next(1, 3); // Generate 1-2 items
            var array = new JsonArray();
            for (int i = 0; i < arraySize; i++)
            {
                var item = GenerateSingleValue(element, elementName, parentResourceType, depth);
                if (item is not null)
                {
                    array.Add(item);
                }
            }

            // For required collections, ensure at least one item exists (even if empty)
            // This handles cases like DataElement.element (type ElementDefinition) where we can't generate content
            // but still need to satisfy min cardinality constraints
            if (array.Count == 0 && element.IsRequired)
            {
                // Add a minimal placeholder object for complex types
                if (!element.Info.IsPrimitive)
                {
                    array.Add(new JsonObject());
                }
            }

            return array.Count > 0 ? array : null;
        }

        return GenerateSingleValue(element, elementName, parentResourceType, depth);
    }

    /// <summary>
    /// Generates a single value based on element name and type heuristics.
    /// Uses binding information when available (ITypeExtended) to generate terminology-correct codes.
    /// </summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Used for pattern matching, not for display")]
    private JsonNode? GenerateSingleValue(IType element, string elementName, string parentResourceType, int depth = 0)
    {
        var info = element.Info;
        var lowerName = elementName.ToLowerInvariant();

        // Try binding-aware generation first for code elements
        if (TryGenerateFromBinding(element, out var bindingValue))
        {
            return bindingValue;
        }

        // Primitive types
        if (info.IsPrimitive)
        {
            return info.Primitive switch
            {
                FhirPrimitive.String => GenerateStringValue(lowerName),
                FhirPrimitive.Boolean => JsonValue.Create(_faker.Random.Bool()),
                FhirPrimitive.Integer => JsonValue.Create(_faker.Random.Int(0, 1000)),
                FhirPrimitive.Decimal => JsonValue.Create(_faker.Random.Decimal(0, 1000)),
                FhirPrimitive.Uri => JsonValue.Create($"http://example.org/{_faker.Random.Word()}"),
                FhirPrimitive.Url => JsonValue.Create($"http://example.org/{_faker.Random.Word()}"),
                FhirPrimitive.Canonical => JsonValue.Create($"http://example.org/{_faker.Random.Word()}"),
                FhirPrimitive.Date => JsonValue.Create(_faker.Date.Past(10).ToString("yyyy-MM-dd")),
                FhirPrimitive.DateTime => JsonValue.Create(_faker.Date.Past(5).ToString("o")),
                FhirPrimitive.Instant => JsonValue.Create(_faker.Date.Recent().ToString("o")),
                FhirPrimitive.Time => JsonValue.Create($"{_faker.Random.Int(0, 23):D2}:{_faker.Random.Int(0, 59):D2}:{_faker.Random.Int(0, 59):D2}"),
                FhirPrimitive.Code => GenerateCodeValue(lowerName, element),
                FhirPrimitive.Id => JsonValue.Create(_faker.Random.AlphaNumeric(8)),
                FhirPrimitive.Markdown => JsonValue.Create(_faker.Lorem.Sentence()),
                FhirPrimitive.Base64Binary => JsonValue.Create(Convert.ToBase64String(_faker.Random.Bytes(16))),
                FhirPrimitive.Oid => JsonValue.Create($"urn:oid:2.16.840.1.{_faker.Random.Int(100000, 999999)}"),
                FhirPrimitive.Uuid => JsonValue.Create($"urn:uuid:{_faker.Random.Guid()}"),
                FhirPrimitive.UnsignedInt => JsonValue.Create(_faker.Random.UInt(0, 1000)),
                FhirPrimitive.PositiveInt => JsonValue.Create(_faker.Random.Int(1, 1000)),
                _ => JsonValue.Create(_faker.Lorem.Word())
            };
        }

        // Complex types - use specialized generators with binding awareness
        // For child elements, Info.Name is the element name, not the type name.
        // Get the actual type from Types[0] or fallback to Info.Name for root resources.
        var childTypeName = element is ITypeExtended extended && extended.Types.Count > 0
            ? extended.Types[0].Code
            : element.Info.Name;

        return childTypeName switch
        {
            "HumanName" => GenerateHumanName(element),
            "Address" => GenerateAddress(element),
            "ContactPoint" => GenerateContactPoint(element),
            "Identifier" => GenerateIdentifier(element),
            "CodeableConcept" => GenerateCodeableConcept(lowerName, element),
            "Coding" => GenerateCoding(lowerName, element),
            "Reference" => GenerateReference(element, parentResourceType),
            "Period" => GeneratePeriod(),
            "Quantity" => GenerateQuantity(),
            "Range" => GenerateRange(),
            "Ratio" => GenerateRatio(),
            "Attachment" => GenerateAttachment(),
            "CodeableReference" => GenerateCodeableReference(lowerName, element, parentResourceType),
            "Signature" => GenerateSignature(element, depth),
            _ => GenerateGenericComplexType(element, parentResourceType, depth) // Generate complex types like BackboneElement
        };
    }

    /// <summary>
    /// Generates a Signature complex type.
    /// </summary>
    private JsonNode GenerateSignature(IType element, int depth)
    {
        // Get Signature type definition
        var signatureType = _schemaProvider.GetTypeDefinition("Signature");
        if (signatureType == null)
        {
            // Fallback to minimal signature
            return new JsonObject
            {
                ["type"] = new JsonArray { GenerateCoding("signature-type", element) },
                ["when"] = JsonValue.Create(_faker.Date.Recent().ToString("o")),
                ["who"] = new JsonObject { ["reference"] = $"Practitioner/{_faker.Random.Guid()}" }
            };
        }

        // Use generic generation for Signature type
        return GenerateGenericComplexType(signatureType, "Signature", depth) ?? new JsonObject();
    }

    /// <summary>
    /// Generates a generic complex type by recursively populating its children.
    /// Used for BackboneElement and other complex types that don't have specialized generators.
    /// </summary>
    private JsonNode? GenerateGenericComplexType(IType element, string parentResourceType, int depth)
    {
        // Prevent infinite recursion
        if (depth >= MaxRecursionDepth)
        {
            return null;
        }

        // Only generate complex types (primitives should have been handled earlier)
        if (element.Info.IsPrimitive)
        {
            return null;
        }

        // Get child elements
        var children = element.Children;

        // For BackboneElement types, the element itself has 0 children.
        // We need to look up the type definition (e.g., "Appointment.participant", "ConceptMap.group.element")
        var currentTypeName = parentResourceType;
        if (children.Count == 0 && element is ITypeExtended extended && extended.Types.Count > 0)
        {
            var typeName = extended.Types[0].Code;
            if (typeName == "BackboneElement")
            {
                // BackboneElement types are named as "ParentResource.ElementName" (PascalCase)
                // Element names in FHIR are camelCase, but type names are PascalCase
                var elementName = element.Info.Name;
                var pascalCaseElementName = char.ToUpperInvariant(elementName[0]) + elementName.Substring(1);
                currentTypeName = $"{parentResourceType}.{pascalCaseElementName}";
                var typeDefinition = _schemaProvider.GetTypeDefinition(currentTypeName);
                if (typeDefinition != null)
                {
                    children = typeDefinition.Children;
                }
            }
        }

        if (children.Count == 0)
        {
            return null;
        }

        var obj = new JsonObject();
        var hasContent = false;

        // Populate child elements
        // For nested BackboneElements, pass the current type name as the parent
        foreach (var child in children)
        {
            var childName = child.Info.Name;

            // Skip problematic elements
            // For BackboneElements, don't skip "language" (it's content, not metadata)
            if (IsSkippableElement(childName, isRootResourceLevel: false))
            {
                continue;
            }

            // Handle choice elements
            var (actualElementName, actualElement) = ResolveChoiceElement(child);

            // Minimal/Realistic: required only. Maximum: required plus optional.
            // The depth guard above bounds optional complex children too.
            if (!ShouldPopulate(child))
            {
                continue;
            }

            var value = GenerateElementValue(actualElement, currentTypeName, depth + 1);
            if (value is not null)
            {
                obj[actualElementName] = value;
                hasContent = true;
            }
            else if (child.IsRequired && !actualElement.Info.IsPrimitive)
            {
                // Placeholders satisfy required cardinality only; optional elements that
                // cannot generate a value are simply skipped.
                obj[actualElementName] = new JsonObject();
                hasContent = true;
            }
        }

        return hasContent ? obj : null;
    }

    /// <summary>
    /// Attempts to generate a value using binding information from ITypeExtended.
    /// This is the preferred method for generating codes as it produces terminology-correct values.
    /// </summary>
    /// <param name="element">The element to generate a value for.</param>
    /// <param name="value">The generated value if successful.</param>
    /// <returns>True if a binding-based value was generated, false otherwise.</returns>
    private bool TryGenerateFromBinding(IType element, out JsonNode? value)
    {
        value = null;

        // Check if element has extended metadata with binding information
        if (element is not ITypeExtended extendedType)
        {
            return false;
        }

        var binding = extendedType.Binding;
        if (binding is null || string.IsNullOrEmpty(binding.ValueSet))
        {
            return false;
        }

        // Try to get codes from our predefined constants
        if (!BindingCodeMapper.TryGetCodesForValueSet(binding.ValueSet, _schemaProvider.ValueSetProvider, out var codes) || codes.Length == 0)
        {
            return false;
        }

        // Pick a random code from the available codes
        var selectedCode = _faker.PickRandom(codes);

        // Determine output format based on element type
        var info = element.Info;

        // For primitive 'code' type, just return the code string
        if (info.IsPrimitive && info.Primitive == FhirPrimitive.Code)
        {
            value = JsonValue.Create(selectedCode.Code);
            return true;
        }

        // For CodeableConcept, return full structure
        if (info.Name == "CodeableConcept")
        {
            value = CreateCodeableConcept(selectedCode);
            return true;
        }

        // For Coding, return coding structure
        if (info.Name == "Coding")
        {
            value = CreateCoding(selectedCode);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a CodeableConcept JSON structure from a FhirCode.
    /// </summary>
    private static JsonObject CreateCodeableConcept(FhirCode code)
    {
        return new JsonObject
        {
            ["coding"] = new JsonArray(CreateCoding(code)),
            ["text"] = code.Display
        };
    }

    /// <summary>
    /// Creates a Coding JSON structure from a FhirCode.
    /// </summary>
    private static JsonObject CreateCoding(FhirCode code)
    {
        return new JsonObject
        {
            ["system"] = code.System,
            ["code"] = code.Code,
            ["display"] = code.Display
        };
    }

    /// <summary>
    /// Generates string values using element name heuristics.
    /// </summary>
    private JsonValue GenerateStringValue(string lowerName)
    {
        if (lowerName.Contains("name", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(_faker.Name.FullName());
        }
        if (lowerName.Contains("family", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(_faker.Name.LastName());
        }
        if (lowerName.Contains("given", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(_faker.Name.FirstName());
        }
        if (lowerName.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(_faker.Internet.Email());
        }
        if (lowerName.Contains("url", StringComparison.OrdinalIgnoreCase) || lowerName.Contains("uri", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create($"http://example.org/{_faker.Random.Word()}");
        }
        if (lowerName.Contains("city", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(_faker.Address.City());
        }
        if (lowerName.Contains("state", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(_faker.Address.State());
        }
        if (lowerName.Contains("country", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(_faker.Address.Country());
        }
        if (lowerName.Contains("postal", StringComparison.OrdinalIgnoreCase) || lowerName.Contains("zip", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(_faker.Address.ZipCode());
        }
        if (lowerName.Contains("phone", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(_faker.Phone.PhoneNumber());
        }

        // Default: random word or sentence
        return JsonValue.Create(_faker.Lorem.Word());
    }

    /// <summary>
    /// Generates code values using binding information (preferred) or element name heuristics (fallback).
    /// Always queries the ValueSetProvider when a binding is present before using heuristics.
    /// </summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "FHIR codes are lowercase by convention")]
    private JsonValue GenerateCodeValue(string lowerName, IType element)
    {
        // Try binding-aware generation first
        if (element is ITypeExtended extendedType && extendedType.Binding is { } binding)
        {
            if (BindingCodeMapper.TryGetCodesForValueSet(binding.ValueSet, _schemaProvider.ValueSetProvider, out var codes) && codes.Length > 0)
            {
                var selectedCode = _faker.PickRandom(codes);
                return JsonValue.Create(selectedCode.Code)!;
            }

            // Query the ValueSetProvider directly if BindingCodeMapper doesn't have the value set
            var normalizedUri = binding.ValueSet?.Contains('|', StringComparison.Ordinal) == true
                ? binding.ValueSet[..binding.ValueSet.IndexOf('|', StringComparison.Ordinal)]
                : binding.ValueSet;

            if (!string.IsNullOrEmpty(normalizedUri))
            {
                var providerCodes = _schemaProvider.ValueSetProvider.GetCodes(normalizedUri);
                if (providerCodes is { Count: > 0 })
                {
                    var randomIndex = _random.Next(providerCodes.Count);
                    var selectedCode = providerCodes[randomIndex];
                    return JsonValue.Create(selectedCode.Code);
                }
            }

            // For required bindings, we MUST use a code from the value set
            // If we still don't have codes, use safe fallbacks based on element patterns
            if (BindingCodeMapper.IsRequiredBinding(binding.Strength))
            {
                return GenerateFallbackCodeForRequiredBinding(binding.ValueSet, lowerName);
            }
        }

        // Fall back to heuristics (only when no binding present)
        // DO NOT use broad pattern matching for codes - too many version-specific variations
        // Only use heuristics for elements that truly have no binding information

        if (lowerName.Contains("gender", StringComparison.OrdinalIgnoreCase))
        {
            // administrative-gender is stable across all FHIR versions
            return JsonValue.Create(_faker.PickRandom(PatientBuilderConstants.Gender.BinaryOnly));
        }

        // Default: random alphanumeric code (only for truly unbound elements)
        return JsonValue.Create(_faker.Random.AlphaNumeric(6).ToLowerInvariant());
    }

    /// <summary>
    /// Generates a fallback code for required bindings when we don't have the value set defined in BindingCodeMapper.
    /// This should only be called after ValueSetProvider has been queried (done in GenerateCodeValue).
    /// Uses very conservative fallbacks based on well-known FHIR patterns.
    /// </summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Used for pattern matching, not for display")]
    private JsonValue GenerateFallbackCodeForRequiredBinding(string? valueSetUri, string elementName)
    {
        // Note: ValueSetProvider should have already been queried by the caller (GenerateCodeValue).
        // This method provides last-resort fallbacks only.

        var lowerName = elementName.ToLowerInvariant();

        // Very conservative fallbacks for well-known required bindings
        // Only use patterns that are known to be stable across all FHIR versions (STU3, R4, R4B, R5)

        if (lowerName.Contains("intent", StringComparison.OrdinalIgnoreCase))
        {
            // request-intent: "order" is in all versions
            return JsonValue.Create("order");
        }
        if (lowerName.Contains("gender", StringComparison.OrdinalIgnoreCase))
        {
            // administrative-gender: all versions have male/female/other/unknown
            return JsonValue.Create(_faker.PickRandom(PatientBuilderConstants.Gender.All));
        }

        // Ultimate fallback for required bindings we don't recognize
        // Use "unknown" as it's a common safe code across many value sets
        return JsonValue.Create(PatientBuilderConstants.Gender.Unknown);
    }

    #region Complex Type Generators

    /// <summary>
    /// Generates a HumanName with binding-aware 'use' code if available.
    /// </summary>
    private JsonObject GenerateHumanName(IType element)
    {
        // Try to get binding-aware 'use' code
        var useCode = TryGetChildBindingCode(element, "use") ?? _faker.PickRandom("official", "usual", "nickname");

        return new JsonObject
        {
            ["use"] = useCode,
            ["family"] = _faker.Name.LastName(),
            ["given"] = new JsonArray(JsonValue.Create(_faker.Name.FirstName()))
        };
    }

    /// <summary>
    /// Generates an Address with binding-aware 'use' and 'type' codes if available.
    /// </summary>
    private JsonObject GenerateAddress(IType element)
    {
        var useCode = TryGetChildBindingCode(element, "use") ?? _faker.PickRandom("home", "work", "temp");
        var typeCode = TryGetChildBindingCode(element, "type") ?? _faker.PickRandom("postal", "physical", "both");

        return new JsonObject
        {
            ["use"] = useCode,
            ["type"] = typeCode,
            ["line"] = new JsonArray(JsonValue.Create(_faker.Address.StreetAddress())),
            ["city"] = _faker.Address.City(),
            ["state"] = _faker.Address.State(),
            ["postalCode"] = _faker.Address.ZipCode(),
            ["country"] = _faker.Address.Country()
        };
    }

    /// <summary>
    /// Generates a ContactPoint with binding-aware 'system' and 'use' codes.
    /// </summary>
    private JsonObject GenerateContactPoint(IType element)
    {
        var systemCode = TryGetChildBindingCode(element, "system") ?? _faker.PickRandom("phone", "email", "fax");
        var useCode = TryGetChildBindingCode(element, "use") ?? _faker.PickRandom("home", "work", "mobile");

        var value = systemCode == "email" ? _faker.Internet.Email() : _faker.Phone.PhoneNumber();

        return new JsonObject
        {
            ["system"] = systemCode,
            ["value"] = value,
            ["use"] = useCode
        };
    }

    /// <summary>
    /// Generates an Identifier with binding-aware 'use' code if available.
    /// </summary>
    private JsonObject GenerateIdentifier(IType element)
    {
        var useCode = TryGetChildBindingCode(element, "use") ?? _faker.PickRandom("usual", "official", "temp");

        return new JsonObject
        {
            ["use"] = useCode,
            ["system"] = $"http://example.org/{_faker.Random.Word()}",
            ["value"] = _faker.Random.AlphaNumeric(10)
        };
    }

    /// <summary>
    /// Generates a CodeableConcept using binding information (preferred) or context heuristics (fallback).
    /// </summary>
    private JsonObject GenerateCodeableConcept(string context, IType element)
    {
        // Try binding-aware generation first
        if (element is ITypeExtended extendedType && extendedType.Binding is { } binding)
        {
            if (BindingCodeMapper.TryGetCodesForValueSet(binding.ValueSet, _schemaProvider.ValueSetProvider, out var codes) && codes.Length > 0)
            {
                var selectedCode = _faker.PickRandom(codes);
                return CreateCodeableConcept(selectedCode);
            }
        }

        // Fall back to heuristic-based generation
        return new JsonObject
        {
            ["coding"] = new JsonArray(GenerateCoding(context, element)),
            ["text"] = _faker.Lorem.Sentence(3)
        };
    }

    /// <summary>
    /// Generates a CodeableReference (R5+ type that combines CodeableConcept and Reference).
    /// This type allows either a concept or a reference to be provided.
    /// </summary>
    private JsonObject GenerateCodeableReference(string context, IType element, string? parentResourceType)
    {
        // CodeableReference can have either concept (CodeableConcept) or reference (Reference)
        // For fake data generation, we'll prefer concept since it's self-contained
        // But for medication contexts, we'll use a concept

        // Try to get binding information for concept generation
        if (element is ITypeExtended extendedType && extendedType.Binding is { } binding)
        {
            if (BindingCodeMapper.TryGetCodesForValueSet(binding.ValueSet, _schemaProvider.ValueSetProvider, out var codes) && codes.Length > 0)
            {
                var selectedCode = _faker.PickRandom(codes);
                return new JsonObject
                {
                    ["concept"] = CreateCodeableConcept(selectedCode)
                };
            }
        }

        // Fall back to heuristic-based concept generation
        if (TryGetCodeFromHeuristic(context, out var fhirCode))
        {
            return new JsonObject
            {
                ["concept"] = CreateCodeableConcept(fhirCode)
            };
        }

        // Ultimate fallback: generic medication code for medication-related contexts
        if (context.Contains("medication", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonObject
            {
                ["concept"] = CreateCodeableConcept(FhirCode.Medications.Ibuprofen400mg)
            };
        }

        // Generic fallback
        return new JsonObject
        {
            ["concept"] = new JsonObject
            {
                ["coding"] = new JsonArray(GenerateCoding(context, element)),
                ["text"] = _faker.Lorem.Sentence(3)
            }
        };
    }

    /// <summary>
    /// Generates a Coding using binding information (preferred) or context heuristics (fallback).
    /// </summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Used for pattern matching, not for display")]
    private JsonObject GenerateCoding(string context, IType element)
    {
        // Try binding-aware generation first
        if (element is ITypeExtended extendedType && extendedType.Binding is { } binding)
        {
            if (BindingCodeMapper.TryGetCodesForValueSet(binding.ValueSet, _schemaProvider.ValueSetProvider, out var codes) && codes.Length > 0)
            {
                var selectedCode = _faker.PickRandom(codes);
                return CreateCoding(selectedCode);
            }
        }

        // Fall back to heuristic-based FhirCode selection
        if (TryGetCodeFromHeuristic(context, out var fhirCode))
        {
            return CreateCoding(fhirCode);
        }

        // Ultimate fallback: generic SNOMED code
        return CreateGenericCoding();
    }

    /// <summary>
    /// Attempts to get a FhirCode based on context heuristics.
    /// </summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Used for pattern matching, not for display")]
    private bool TryGetCodeFromHeuristic(string context, out FhirCode fhirCode)
    {
        var lowerContext = context.ToLowerInvariant();

        // Match context to appropriate FhirCode constants
        if (lowerContext.Contains("condition", StringComparison.OrdinalIgnoreCase) ||
            lowerContext.Contains("diagnosis", StringComparison.OrdinalIgnoreCase))
        {
            fhirCode = FhirCode.Conditions.Hypertension;
            return true;
        }

        if (lowerContext.Contains("medication", StringComparison.OrdinalIgnoreCase))
        {
            fhirCode = FhirCode.Medications.Ibuprofen400mg;
            return true;
        }

        if (lowerContext.Contains("observation", StringComparison.OrdinalIgnoreCase) ||
            lowerContext.Contains("loinc", StringComparison.OrdinalIgnoreCase))
        {
            fhirCode = FhirCode.Observations.BodyWeight;
            return true;
        }

        if (lowerContext.Contains("procedure", StringComparison.OrdinalIgnoreCase))
        {
            // Use a generic procedure code (we don't have Appendectomy in our constants)
            var procedureCodes = BindingCodeMapper.GetAllProcedureCodes();
            if (procedureCodes.Length > 0)
            {
                fhirCode = _faker.PickRandom(procedureCodes);
                return true;
            }
        }

        if (lowerContext.Contains("allergy", StringComparison.OrdinalIgnoreCase))
        {
            var allergenCodes = BindingCodeMapper.GetAllAllergenCodes();
            if (allergenCodes.Length > 0)
            {
                fhirCode = _faker.PickRandom(allergenCodes);
                return true;
            }
        }

        if (lowerContext.Contains("vaccine", StringComparison.OrdinalIgnoreCase) ||
            lowerContext.Contains("immunization", StringComparison.OrdinalIgnoreCase))
        {
            var vaccineCodes = BindingCodeMapper.GetAllImmunizationCodes();
            if (vaccineCodes.Length > 0)
            {
                fhirCode = _faker.PickRandom(vaccineCodes);
                return true;
            }
        }

        fhirCode = default!;
        return false;
    }

    /// <summary>
    /// Creates a generic SNOMED coding as ultimate fallback.
    /// </summary>
    private JsonObject CreateGenericCoding()
    {
        return new JsonObject
        {
            ["system"] = FhirCode.Systems.SnomedCt,
            ["code"] = _faker.Random.Int(10000, 99999).ToString(),
            ["display"] = _faker.Lorem.Word()
        };
    }

    /// <summary>
    /// Tries to get a binding-aware code for a child element of a complex type.
    /// </summary>
    /// <param name="parentElement">The parent complex type element.</param>
    /// <param name="childName">The name of the child element (e.g., "use", "system").</param>
    /// <returns>A code string if binding found, null otherwise.</returns>
    private string? TryGetChildBindingCode(IType parentElement, string childName)
    {
        // Find the child element
        var childElement = parentElement.Children.FirstOrDefault(c =>
            string.Equals(c.Info.Name, childName, StringComparison.OrdinalIgnoreCase));

        if (childElement is ITypeExtended extendedChild && extendedChild.Binding is { } binding)
        {
            if (BindingCodeMapper.TryGetCodesForValueSet(binding.ValueSet, _schemaProvider.ValueSetProvider, out var codes) && codes.Length > 0)
            {
                return _faker.PickRandom(codes).Code;
            }
        }

        return null;
    }

    private JsonObject GenerateReference(IType element, string parentResourceType)
    {
        // Try to get reference targets from element (if ITypeExtended)
        var referenceType = element is ITypeExtended extended && extended.ReferenceTargets.Count > 0
            ? _faker.PickRandom(extended.ReferenceTargets.ToArray())
            : "Patient"; // Default to Patient

        return new JsonObject
        {
            ["reference"] = $"{referenceType}/{_faker.Random.Guid()}"
        };
    }

    private JsonObject GeneratePeriod()
    {
        var start = _faker.Date.Past(2);
        var end = _faker.Date.Between(start, DateTime.Now);

        return new JsonObject
        {
            ["start"] = start.ToString("yyyy-MM-dd"),
            ["end"] = end.ToString("yyyy-MM-dd")
        };
    }

    private JsonObject GenerateQuantity()
    {
        return new JsonObject
        {
            ["value"] = _faker.Random.Decimal(0, 1000),
            ["unit"] = _faker.PickRandom("kg", "g", "mg", "L", "mL", "cm", "m"),
            ["system"] = "http://unitsofmeasure.org"
        };
    }

    private JsonObject GenerateRange()
    {
        var low = _faker.Random.Decimal(0, 100);
        var high = _faker.Random.Decimal(low, 200);

        return new JsonObject
        {
            ["low"] = new JsonObject { ["value"] = low },
            ["high"] = new JsonObject { ["value"] = high }
        };
    }

    private JsonObject GenerateRatio()
    {
        return new JsonObject
        {
            ["numerator"] = new JsonObject { ["value"] = _faker.Random.Decimal(1, 10) },
            ["denominator"] = new JsonObject { ["value"] = _faker.Random.Decimal(1, 10) }
        };
    }

    private JsonObject GenerateAttachment()
    {
        return new JsonObject
        {
            ["contentType"] = _faker.PickRandom("image/png", "application/pdf", "text/plain"),
            ["data"] = Convert.ToBase64String(_faker.Random.Bytes(64)),
            ["title"] = _faker.Lorem.Sentence(3)
        };
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Determines whether an element should be populated for the current <see cref="Density"/>.
    /// Required elements are always populated; optional elements only under <see cref="GenerationDensity.Maximum"/>.
    /// </summary>
    private bool ShouldPopulate(IType child) => child.IsRequired || Density == GenerationDensity.Maximum;

    /// <summary>
    /// Applies the configured tag to a resource's meta.tag array.
    /// </summary>
    private void ApplyTag(ResourceJsonNode resource)
    {
        if (_tag is null) return;

        if (resource.MutableNode["meta"] is not JsonObject meta)
        {
            meta = new JsonObject();
            resource.MutableNode["meta"] = meta;
        }

        if (meta["tag"] is not JsonArray tagArray)
        {
            tagArray = [];
            meta["tag"] = tagArray;
        }

        tagArray.Add(new JsonObject { ["code"] = _tag });
    }

    /// <summary>
    /// Elements to skip during automatic generation.
    /// NOTE: "language" is only skipped at the root resource level (it's a metadata field).
    /// For BackboneElements like Patient.communication, "language" should NOT be skipped.
    /// </summary>
    /// <param name="elementName">The element name to check.</param>
    /// <param name="isRootResourceLevel">True if this is a root resource element (not in a BackboneElement).</param>
    /// <returns>True if the element should be skipped.</returns>
    private static bool IsSkippableElement(string elementName, bool isRootResourceLevel = true)
    {
        // Always skip these infrastructure elements regardless of level
        if (elementName is "meta" or "id" or "implicitRules" or "text" or "contained" or "extension" or "modifierExtension")
        {
            return true;
        }

        // Only skip "language" at root level (it's the resource's metadata language)
        // Don't skip it in BackboneElements (e.g., Patient.communication.language)
        if (elementName == "language" && isRootResourceLevel)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves choice elements (value[x] pattern) to their concrete element name with type suffix.
    /// For example: "deceased" (choice element) -> "deceasedBoolean"
    /// For non-choice elements, returns the original element unchanged.
    /// </summary>
    /// <param name="element">The element to resolve.</param>
    /// <returns>A tuple of (actualElementName, element) where actualElementName includes the type suffix for choice elements.</returns>
    private (string ActualElementName, IType ActualElement) ResolveChoiceElement(IType element)
    {
        var elementName = element.Info.Name;

        // Not a choice element - return as-is
        if (!element.Info.IsChoiceElement)
        {
            return (elementName, element);
        }

        // Choice element - need to pick a type and create the concrete element name
        if (element is ITypeExtended extended && extended.Types.Count > 0)
        {
            // Pick a random type from the available types (preferring simpler/common types)
            var selectedType = SelectChoiceType(extended.Types);
            var typeSuffix = FormatTypeSuffix(selectedType.Code);

            // Create the actual JSON element name (e.g., "deceased" + "Boolean" = "deceasedBoolean")
            var actualElementName = elementName + typeSuffix;

            return (actualElementName, element);
        }

        // Fallback: element is marked as choice but has no types info - use default type if available
        if (element is ITypeExtended extendedWithDefault && !string.IsNullOrEmpty(extendedWithDefault.DefaultTypeName))
        {
            var typeSuffix = FormatTypeSuffix(extendedWithDefault.DefaultTypeName);
            return (elementName + typeSuffix, element);
        }

        // Ultimate fallback - shouldn't happen with well-formed schemas
        return (elementName, element);
    }

    /// <summary>
    /// Selects a type from the available choice types for value generation.
    /// Prefers simpler/more common types that are easier to generate.
    /// </summary>
    private ITypeReference SelectChoiceType(IReadOnlyList<ITypeReference> types)
    {
        // Preference order for choice types (simpler types first)
        var preferenceOrder = new[] { "boolean", "string", "integer", "decimal", "code", "dateTime", "date", "Quantity", "CodeableConcept" };

        foreach (var preferred in preferenceOrder)
        {
            var match = types.FirstOrDefault(t => string.Equals(t.Code, preferred, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        // No preferred type found - pick randomly
        return _faker.PickRandom(types.ToArray());
    }

    /// <summary>
    /// Formats a type code as a FHIR JSON element suffix (capitalizes first letter).
    /// For example: "boolean" -> "Boolean", "dateTime" -> "DateTime"
    /// </summary>
    private static string FormatTypeSuffix(string typeCode)
    {
        if (string.IsNullOrEmpty(typeCode))
        {
            return string.Empty;
        }

        // Capitalize first letter for JSON element naming convention
        return char.ToUpperInvariant(typeCode[0]) + typeCode.Substring(1);
    }

    #endregion
}
