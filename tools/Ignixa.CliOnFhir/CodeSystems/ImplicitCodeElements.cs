// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>
/// The FHIR <c>code</c> elements that a FHIR version's resources and their backbone elements declare with a binding, by
/// path (<c>Patient.gender</c>, <c>Patient.contact.gender</c>), each with the code system its binding implies when Ignixa's
/// indexer knows one (<see cref="CodeSystemResolver"/>), else none. Read from the version's schema, so an element whose
/// binding implies no single system (R4 <c>Task.intent</c>) is still an implicit code element.
/// </summary>
internal sealed class ImplicitCodeElements
{
    private readonly Dictionary<string, string?> _systems;

    private ImplicitCodeElements(Dictionary<string, string?> systems)
    {
        _systems = systems;
    }

    public static ImplicitCodeElements From(IFhirSchemaProvider schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var resolver = new CodeSystemResolver(schema.Version);
        var systems = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string resourceType in schema.ResourceTypeNames.Order(StringComparer.Ordinal))
        {
            IType definition = schema.GetTypeDefinition(resourceType)
                ?? throw new InvalidOperationException($"The {schema.Version} schema lists resource type {resourceType} but does not define it.");
            Add(schema, resolver, resourceType, definition, systems);
        }

        return new ImplicitCodeElements(systems);
    }

    public bool Contains(string element) => _systems.ContainsKey(element);

    /// <summary>The system <paramref name="element"/>'s binding implies, or null when it implies none; the element must be one of these.</summary>
    public string? SystemOf(string element) => _systems[element];

    // A backbone element has its own type definition under its path (Patient.contact); a datatype element (Patient.name) and
    // an element repeating another's definition (Questionnaire.item.item) have none, so the walk does not enter them.
    private static void Add(IFhirSchemaProvider schema, CodeSystemResolver resolver, string path, IType definition, Dictionary<string, string?> systems)
    {
        foreach (IType child in definition.Children)
        {
            string childPath = $"{path}.{child.Info.Name}";
            if (child.Info.Primitive == FhirPrimitive.Code && child is ITypeExtended { Binding: not null })
            {
                systems[childPath] = resolver.ResolveSystem(childPath);
            }
            else if (schema.GetTypeDefinition(childPath) is { } backbone)
            {
                Add(schema, resolver, childPath, backbone, systems);
            }
        }
    }
}
