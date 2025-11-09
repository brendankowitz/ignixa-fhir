// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Constant string definitions for FHIR type names.
/// Used to avoid magic strings throughout the codebase.
/// </summary>
/// <remarks>
/// These constants represent FHIR type names as they appear in StructureDefinitions
/// and are used for type checking, validation, and FHIRPath evaluation.
///
/// Organized by category:
/// - Primitive types (string, integer, boolean, etc.)
/// - General-purpose data types (CodeableConcept, Identifier, etc.)
/// - Metadata types (ContactDetail, UsageContext, etc.)
/// - Special types (Reference, Extension, etc.)
/// - Resource base types (Resource, DomainResource, etc.)
/// </remarks>
public static class FhirTypeConstants
{
    // ===== PRIMITIVE TYPES =====
    // See: https://hl7.org/fhir/R4/datatypes.html#primitive

    /// <summary>true | false</summary>
    public const string Boolean = "boolean";

    /// <summary>Signed 32-bit integer</summary>
    public const string Integer = "integer";

    /// <summary>Unicode string (1 MB limit)</summary>
    public const string String = "string";

    /// <summary>Rational number with arbitrary precision</summary>
    public const string Decimal = "decimal";

    /// <summary>Universal Resource Identifier (RFC 3986)</summary>
    public const string Uri = "uri";

    /// <summary>URL (subset of uri) - Added in R4</summary>
    public const string Url = "url";

    /// <summary>Canonical URL (resource reference) - Added in R4</summary>
    public const string Canonical = "canonical";

    /// <summary>Base64-encoded binary data</summary>
    public const string Base64Binary = "base64Binary";

    /// <summary>Instant in time (ISO 8601 with timezone)</summary>
    public const string Instant = "instant";

    /// <summary>Date (YYYY-MM-DD)</summary>
    public const string Date = "date";

    /// <summary>Date with optional time (YYYY-MM-DD, YYYY-MM-DDThh:mm:ss+zz:zz)</summary>
    public const string DateTime = "dateTime";

    /// <summary>Time (HH:MM:SS)</summary>
    public const string Time = "time";

    /// <summary>Code from a controlled vocabulary</summary>
    public const string Code = "code";

    /// <summary>OID (urn:oid:...)</summary>
    public const string Oid = "oid";

    /// <summary>Resource identifier ([A-Za-z0-9\-\.]{1,64})</summary>
    public const string Id = "id";

    /// <summary>Markdown text (CommonMark subset)</summary>
    public const string Markdown = "markdown";

    /// <summary>Unsigned 32-bit integer (>= 0)</summary>
    public const string UnsignedInt = "unsignedInt";

    /// <summary>Positive 32-bit integer (>= 1)</summary>
    public const string PositiveInt = "positiveInt";

    /// <summary>UUID (urn:uuid:...) - Added in R4</summary>
    public const string Uuid = "uuid";

    /// <summary>Signed 64-bit integer (for large counters/file sizes) - Added in R5</summary>
    public const string Integer64 = "integer64";

    // ===== GENERAL-PURPOSE DATA TYPES =====
    // See: https://hl7.org/fhir/R4/datatypes.html

    /// <summary>Quantity with comparator (e.g., "5 mg", "> 10 cm")</summary>
    public const string Quantity = "Quantity";

    /// <summary>Quantity with units from UCUM (Unified Code for Units of Measure)</summary>
    public const string SimpleQuantity = "SimpleQuantity";

    /// <summary>Money amount with currency (ISO 4217)</summary>
    public const string Money = "Money";

    /// <summary>Time range defined by start/end</summary>
    public const string Range = "Range";

    /// <summary>Ratio of two Quantity values</summary>
    public const string Ratio = "Ratio";

    /// <summary>RatioRange: Range of ratios (R5+)</summary>
    public const string RatioRange = "RatioRange";

    /// <summary>Time period with start and end</summary>
    public const string Period = "Period";

    /// <summary>Human-readable name (family, given, prefix, suffix)</summary>
    public const string HumanName = "HumanName";

    /// <summary>Postal address</summary>
    public const string Address = "Address";

    /// <summary>Contact detail (phone, email, fax, etc.)</summary>
    public const string ContactPoint = "ContactPoint";

    /// <summary>Timing schedule (e.g., "twice daily", "every 4 hours")</summary>
    public const string Timing = "Timing";

    /// <summary>Signature (digital signature)</summary>
    public const string Signature = "Signature";

    /// <summary>Attachment (document, image, etc.)</summary>
    public const string Attachment = "Attachment";

    /// <summary>Business identifier</summary>
    public const string Identifier = "Identifier";

    /// <summary>Concept from a terminology (code + system + display)</summary>
    public const string Coding = "Coding";

    /// <summary>Concept with multiple codings (translations)</summary>
    public const string CodeableConcept = "CodeableConcept";

    /// <summary>CodeableReference: Reference with CodeableConcept (R5+)</summary>
    public const string CodeableReference = "CodeableReference";

    /// <summary>Age (specialized Quantity)</summary>
    public const string Age = "Age";

    /// <summary>Count (specialized Quantity)</summary>
    public const string Count = "Count";

    /// <summary>Distance (specialized Quantity)</summary>
    public const string Distance = "Distance";

    /// <summary>Duration (specialized Quantity)</summary>
    public const string Duration = "Duration";

    /// <summary>Sampled data (waveforms, curves)</summary>
    public const string SampledData = "SampledData";

    /// <summary>Annotation (text note with author)</summary>
    public const string Annotation = "Annotation";

    // ===== METADATA TYPES =====
    // See: https://hl7.org/fhir/R4/metadatatypes.html

    /// <summary>Contact details for a person or organization</summary>
    public const string ContactDetail = "ContactDetail";

    /// <summary>Contributor information</summary>
    public const string Contributor = "Contributor";

    /// <summary>Data requirement (for knowledge artifacts)</summary>
    public const string DataRequirement = "DataRequirement";

    /// <summary>Parameter definition</summary>
    public const string ParameterDefinition = "ParameterDefinition";

    /// <summary>Related artifact (citation, dependency, etc.)</summary>
    public const string RelatedArtifact = "RelatedArtifact";

    /// <summary>Trigger definition (event-based)</summary>
    public const string TriggerDefinition = "TriggerDefinition";

    /// <summary>Usage context (clinical context, user type, etc.)</summary>
    public const string UsageContext = "UsageContext";

    /// <summary>Dosage instruction</summary>
    public const string Dosage = "Dosage";

    /// <summary>Expression (CQL, FHIRPath, etc.)</summary>
    public const string Expression = "Expression";

    // ===== SPECIAL TYPES =====

    /// <summary>Reference to another resource</summary>
    public const string Reference = "Reference";

    /// <summary>Metadata about a resource</summary>
    public const string Meta = "Meta";

    /// <summary>Extension (modifier or regular)</summary>
    public const string Extension = "Extension";

    /// <summary>Narrative (human-readable summary)</summary>
    public const string Narrative = "Narrative";

    /// <summary>Element (base type for all elements)</summary>
    public const string Element = "Element";

    /// <summary>BackboneElement (base for nested elements in resources)</summary>
    public const string BackboneElement = "BackboneElement";

    /// <summary>BackboneType (R5+ base for reusable backbone structures)</summary>
    public const string BackboneType = "BackboneType";

    // ===== RESOURCE BASE TYPES =====

    /// <summary>Base Resource type (all resources inherit from this)</summary>
    public const string Resource = "Resource";

    /// <summary>DomainResource (resources with narrative)</summary>
    public const string DomainResource = "DomainResource";

    /// <summary>Bundle entry component</summary>
    public const string BundleEntry = "Bundle.entry";

    /// <summary>Parameters parameter component</summary>
    public const string ParametersParameter = "Parameters.parameter";

    // ===== COMMON RESOURCE TYPES =====
    // Note: Not exhaustive - add more as needed
    // Full list: https://hl7.org/fhir/R4/resourcelist.html

    /// <summary>Patient resource</summary>
    public const string Patient = "Patient";

    /// <summary>Observation resource</summary>
    public const string Observation = "Observation";

    /// <summary>Condition resource</summary>
    public const string Condition = "Condition";

    /// <summary>Procedure resource</summary>
    public const string Procedure = "Procedure";

    /// <summary>Encounter resource</summary>
    public const string Encounter = "Encounter";

    /// <summary>Practitioner resource</summary>
    public const string Practitioner = "Practitioner";

    /// <summary>Organization resource</summary>
    public const string Organization = "Organization";

    /// <summary>Medication resource</summary>
    public const string Medication = "Medication";

    /// <summary>MedicationRequest resource</summary>
    public const string MedicationRequest = "MedicationRequest";

    /// <summary>AllergyIntolerance resource</summary>
    public const string AllergyIntolerance = "AllergyIntolerance";

    /// <summary>DiagnosticReport resource</summary>
    public const string DiagnosticReport = "DiagnosticReport";

    /// <summary>ServiceRequest resource</summary>
    public const string ServiceRequest = "ServiceRequest";

    /// <summary>Bundle resource</summary>
    public const string Bundle = "Bundle";

    /// <summary>Parameters resource</summary>
    public const string Parameters = "Parameters";

    /// <summary>OperationOutcome resource</summary>
    public const string OperationOutcome = "OperationOutcome";

    /// <summary>CapabilityStatement resource</summary>
    public const string CapabilityStatement = "CapabilityStatement";

    /// <summary>StructureDefinition resource</summary>
    public const string StructureDefinition = "StructureDefinition";

    /// <summary>ValueSet resource</summary>
    public const string ValueSet = "ValueSet";

    /// <summary>CodeSystem resource</summary>
    public const string CodeSystem = "CodeSystem";

    /// <summary>SearchParameter resource</summary>
    public const string SearchParameter = "SearchParameter";

    /// <summary>Questionnaire resource</summary>
    public const string Questionnaire = "Questionnaire";

    /// <summary>QuestionnaireResponse resource</summary>
    public const string QuestionnaireResponse = "QuestionnaireResponse";
}
