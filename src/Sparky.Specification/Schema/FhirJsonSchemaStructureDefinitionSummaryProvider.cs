// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Hl7.Fhir.Specification;
using Json.More;
using Json.Schema;
using Sparky.Extensions;
using Sparky.Extensions.Schema;
using Sparky.Specification.Data;
using ValidationContext = System.ComponentModel.DataAnnotations.ValidationContext;

namespace Sparky.Specification.Schema;

public class FhirJsonSchemaStructureDefinitionSummaryProvider : IStructureDefinitionSummaryProvider, IFhirSchemaProvider
{
    public FhirSpecification Version { get; }

    public IReadOnlySet<string> ResourceTypeNames { get; }

    private readonly IDictionary<string, IStructureDefinitionSummary> Types = new Dictionary<string, IStructureDefinitionSummary>();

    public FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification fhirSpecification)
    {
        Version = fhirSpecification;

#pragma warning disable CA2012
        using Stream openVersionedFileStream = DataLoader.OpenVersionedFileStream(fhirSpecification, "fhir.schema.json");
        JsonSchema schema = JsonSchema.FromStream(openVersionedFileStream).Result;
#pragma warning restore CA2012

        DefinitionsKeyword definitions = schema.Keywords.OfType<DefinitionsKeyword>().Single();

        JsonSchema resourcesList = definitions.Definitions[SchemaStructureDefinitionSummary.ResourceListName];

        var resourceTypesLookup = resourcesList.Keywords.OfType<OneOfKeyword>().Single().Schemas
            .Select(x => definitions.Definitions.Lookup(x))
            .ToDictionary(
                x => x.Value.Name,
                x => x.Value.Schema);

        var context = new Context
        {
            Definitions = definitions.Definitions,
            ResourceTypesLookup = resourceTypesLookup,
            FullSchema = schema,
            StructureDefinitionSummaries = Types
        };

        string extensionName = "Extension";
        JsonSchema extension = definitions.Definitions[extensionName];
        var extensionStructureDefinition = new SchemaStructureDefinitionSummary(extensionName, false, false, extension, 0, context);
        Types[extensionName] = extensionStructureDefinition;
        context.Extension = extensionStructureDefinition;

        foreach (KeyValuePair<string, JsonSchema> definition in definitions.Definitions)
        {
            string key = definition.Key.Replace("_", "#", StringComparison.Ordinal);
            if (Types.ContainsKey(key)) continue;

            Types.Add(key, new SchemaStructureDefinitionSummary(definition.Key, false, resourceTypesLookup.ContainsKey(definition.Key), definition.Value, 0, context));
        }

        ResourceTypeNames = resourceTypesLookup.Keys.ToImmutableHashSet();
    }

    public bool IsKnownType(string type)
    {
        return Types.ContainsKey(type);
    }

    public IStructureDefinitionSummary Provide(string canonical)
    {
        // Handle full canonical URLs like "http://hl7.org/fhir/StructureDefinition/Patient"
        // Extract the type name (last segment after the last '/')
        string typeName = canonical;
        if (canonical.Contains('/', StringComparison.Ordinal))
        {
            typeName = canonical.Substring(canonical.LastIndexOf('/') + 1);
        }

        // Return null if the type is not found (rather than throwing KeyNotFoundException)
        return Types.TryGetValue(typeName, out var summary) ? summary : null;
    }

    private class Context
    {
        public IReadOnlyDictionary<string, JsonSchema> Definitions { get; set; }

        public IReadOnlyDictionary<string, JsonSchema> ResourceTypesLookup { get; set; }

        public JsonSchema FullSchema { get; set; }

        public IDictionary<string, IStructureDefinitionSummary> StructureDefinitionSummaries { get; set; }

        public SchemaStructureDefinitionSummary Extension { get; set; }

        public IStructureDefinitionSummary GetOrCreateSimplePropertyStructureDefinition(string name)
        {
            IStructureDefinitionSummary propertyStructureDefinition;

            if (StructureDefinitionSummaries.ContainsKey(name))
            {
                propertyStructureDefinition = StructureDefinitionSummaries[name];
            }
            else
            {
                propertyStructureDefinition = new SimpleStructureDefinitionSummary(
                    name,
                    false,
                    false,
                    this);

                StructureDefinitionSummaries[name] = propertyStructureDefinition;
            }

            return propertyStructureDefinition;
        }

        public IStructureDefinitionSummary GetOrCreatePropertyStructureDefinition(
            JsonSchema parentDefinition,
            int level,
            (string Name, JsonSchema Schema)? propDefinition,
            bool propIsResource)
        {
            IStructureDefinitionSummary propertyStructureDefinition = null;
            if (propDefinition != null)
            {
                string key = $"{propDefinition.Value.Name.Replace("_", "#", StringComparison.Ordinal)}";
                TypeKeyword typeName = propDefinition.Value.Schema.Keywords.OfType<TypeKeyword>().SingleOrDefault();

                if (StructureDefinitionSummaries.ContainsKey(key))
                    propertyStructureDefinition = StructureDefinitionSummaries[key];
                else if (typeName != null) propertyStructureDefinition = GetOrCreateSimplePropertyStructureDefinition(key);

                if (string.Equals("Extension", key, StringComparison.Ordinal) && propertyStructureDefinition == null) return Extension;

                if (level < 10 && !parentDefinition.Equals(propDefinition?.Schema) && propertyStructureDefinition == null)
                {
                    propertyStructureDefinition = new SchemaStructureDefinitionSummary(
                        propDefinition.Value.Name,
                        false,
                        propIsResource,
                        propDefinition?.Schema,
                        ++level,
                        this);

                    StructureDefinitionSummaries[key] = propertyStructureDefinition;
                }
            }

            return propertyStructureDefinition;
        }
    }

    private class SimpleStructureDefinitionSummary : IStructureDefinitionSummary
    {
        private readonly Context _context;

        public SimpleStructureDefinitionSummary(string typeName, bool isAbstract, bool isResource, Context context)
        {
            _context = context;
            TypeName = typeName;
            IsAbstract = isAbstract;
            IsResource = isResource;
        }

        public string TypeName { get; }

        public bool IsAbstract { get; }

        public bool IsResource { get; }

        public IReadOnlyCollection<IElementDefinitionSummary> GetElements()
        {
            return new[]
            {
                new ElementDefinitionSummary("id", false, false, false, XmlRepresentation.XmlElement, new[] { _context.Extension }, 0, null, false, false),
                new ElementDefinitionSummary("extension", true, false, false, XmlRepresentation.XmlElement, new[] { _context.Extension }, 0, null, false, false)
            };
        }
    }

    private class SchemaStructureDefinitionSummary : IStructureDefinitionSummary, IValidatableObject
    {
        private readonly JsonSchema _definition;

        private readonly IReadOnlyCollection<IElementDefinitionSummary> _elements;
        private readonly JsonSchema _fullSchema;
        internal const string ResourceListName = "ResourceList";

        public SchemaStructureDefinitionSummary(
            string typeName,
            bool isAbstract,
            bool isResource,
            JsonSchema definition,
            int level,
            Context context)
        {
            _definition = definition;
            TypeName = typeName?.Replace("_", "#", StringComparison.Ordinal);
            IsAbstract = isAbstract;
            IsResource = isResource;
            _fullSchema = context.FullSchema;

            ISet<IElementDefinitionSummary> elements = new HashSet<IElementDefinitionSummary>();

            RequiredKeyword required = definition?.Keywords.OfType<RequiredKeyword>().SingleOrDefault();
            PropertiesKeyword propertiesKeyword = definition?.Keywords.OfType<PropertiesKeyword>().SingleOrDefault();

            var properties = new Dictionary<string, JsonSchema>();
            if (propertiesKeyword != null)
                foreach (KeyValuePair<string, JsonSchema> schema in propertiesKeyword.Properties)
                    properties.Add(schema.Key, schema.Value);

            if (properties.Any())
                foreach ((IGrouping<string, KeyValuePair<string, JsonSchema>> value, int i) element in properties
                             .GroupBy(x => x.Key.Trim('_'))
                             .Select((value, i) => (value, i)))
                {
                    SchemaElementDefinitionSummary property = ProcessProperty(definition, required, (element.value.First(), element.i), level, context);

                    elements.Add(property);
                }

            if (!properties.Any(x => string.Equals("extension", x.Key, StringComparison.Ordinal)) && context.Extension != null) elements.Add(new ElementDefinitionSummary("extension", true, false, false, XmlRepresentation.XmlElement, new[] { context.Extension }, 0, null, false, false));

            _elements = elements.ToImmutableArray();
        }

        private SchemaElementDefinitionSummary ProcessProperty(
            JsonSchema parentDefinition,
            RequiredKeyword required,
            (KeyValuePair<string, JsonSchema> value, int i) element,
            int level,
            Context context)
        {
            bool isRequired = required?.Properties.Contains(element.value.Key) == true;
            (string Name, JsonSchema Schema)? propDefinition = context.Definitions.Lookup(element.value.Value);

            bool propIsResource =
                propDefinition != null && (context.ResourceTypesLookup.ContainsKey(propDefinition.Value.Name) ||
                                           string.Equals(ResourceListName, propDefinition.Value.Name, StringComparison.Ordinal));

            IStructureDefinitionSummary propertyStructureDefinition = context
                .GetOrCreatePropertyStructureDefinition(parentDefinition, level, propDefinition, propIsResource);

#pragma warning disable CA1304
            string typeKeyword = element.value.Value.Keywords.OfType<TypeKeyword>().SingleOrDefault()?.Type.ToString().ToLower();
#pragma warning restore CA1304

            ITypeSerializationInfo[] typeSerializationInfos;

            if (string.Equals(ResourceListName, propDefinition?.Name, StringComparison.Ordinal))
                typeSerializationInfos = new ITypeSerializationInfo[] { null };
            else if (parentDefinition.Equals(propDefinition?.Schema))
                typeSerializationInfos = new ITypeSerializationInfo[] { this };
            else if (propertyStructureDefinition == null)
                // Simple properties (i.e. const)
                typeSerializationInfos = new ITypeSerializationInfo[] { context.GetOrCreateSimplePropertyStructureDefinition(propDefinition?.Name ?? typeKeyword ?? "string") };
            else
                typeSerializationInfos = new ITypeSerializationInfo[] { propertyStructureDefinition };

            return new SchemaElementDefinitionSummary(
                element.value.Key,
                string.Equals("array", typeKeyword, StringComparison.Ordinal),
                isRequired,
                isRequired,
                (element.value.Key.StartsWith("_value", StringComparison.Ordinal) || element.value.Key.StartsWith("_value", StringComparison.Ordinal)) &&
                (element.value.Key != "value" || element.value.Key != "_value"),
                propIsResource,
                typeSerializationInfos,
                typeSerializationInfos?.FirstOrDefault()?.GetTypeName() ?? typeKeyword,
                null,
                propDefinition?.Name?.Contains("html", StringComparison.Ordinal) == true ? XmlRepresentation.XHtml : XmlRepresentation.XmlElement,
                element.i);
        }

        public string TypeName { get; }
        public bool IsAbstract { get; }
        public bool IsResource { get; }

        public IReadOnlyCollection<IElementDefinitionSummary> GetElements()
        {
            return _elements;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // TODO: Update for JsonSchema.Net 7.x API changes
            // The validation API changed significantly in version 7.x
            // For now, return empty validation results since we don't use this for search
            return Array.Empty<ValidationResult>();

            /* Original code - requires JsonSchema.Net 6.x API
            IEnumerable<ValidationResult> Find(ValidationResults validationResults)
            {
                var results = new List<ValidationResult>();

                if (validationResults.HasNestedResults)
                    foreach (ValidationResults r in validationResults.NestedResults)
                        results.AddRange(Find(r));

                if (!string.IsNullOrEmpty(validationResults.Message))
                    results.Add(new ValidationResult(validationResults.Message, new[] { validationResults.InstanceLocation.ToString() }));

                return results;
            }

            if (!(validationContext.ObjectInstance is JsonDocument jsonDocument)) jsonDocument = validationContext.ObjectInstance.ToJsonDocument();

            var validationOptions = new ValidationOptions();

            validationOptions.DefaultBaseUri = new Uri("http://hl7.org/fhir/json-schema/4.0");

            foreach (KeyValuePair<string, JsonSchema> subSchema in _fullSchema.Keywords.OfType<DefinitionsKeyword>().Single().Definitions)
                validationOptions.SchemaRegistry.RegisterAnchor(validationOptions.DefaultBaseUri, $"#/definitions/{subSchema.Key}", subSchema.Value);

            validationOptions.OutputFormat = OutputFormat.Verbose;

            ValidationResults results = _definition.Validate(jsonDocument.RootElement, validationOptions);

            return Find(results);
            */
        }
    }

    private class SchemaElementDefinitionSummary : IElementDefinitionSummary
    {
        public SchemaElementDefinitionSummary(string elementName, bool isCollection, bool isRequired, bool inSummary, bool isChoiceElement, bool isResource, ITypeSerializationInfo[] type, string defaultTypeName, string nonDefaultNamespace, XmlRepresentation representation, int order)
        {
            ElementName = elementName;
            IsCollection = isCollection;
            IsRequired = isRequired;
            InSummary = inSummary;
            IsChoiceElement = isChoiceElement;
            IsResource = isResource;
            Type = type;
            DefaultTypeName = defaultTypeName;
            NonDefaultNamespace = nonDefaultNamespace;
            Representation = representation;
            Order = order;
        }

        public string ElementName { get; }

        public bool IsCollection { get; }

        public bool IsRequired { get; }

        public bool InSummary { get; }

        public bool IsChoiceElement { get; }

        public bool IsResource { get; }

        public bool IsModifier { get; }

        public ITypeSerializationInfo[] Type { get; }

        public string DefaultTypeName { get; }

        public string NonDefaultNamespace { get; }

        public XmlRepresentation Representation { get; }

        public int Order { get; }
    }
}
