/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * A Patient with repeating elements, shared by the argument-cardinality and fuzz tests.
 */

using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.TestHelpers;

/// <summary>
/// A Patient whose <c>name.given</c>, <c>name</c> and <c>telecom</c> repeat, so a path over them is a
/// multi-item collection, as repeating data is in production.
/// </summary>
internal static class FuzzPatient
{
    private const string Json = """
        {
          "resourceType": "Patient",
          "id": "p1",
          "active": true,
          "gender": "male",
          "birthDate": "1974-12-25",
          "multipleBirthInteger": 2,
          "name": [
            { "use": "official", "family": "Chalmers", "given": ["Peter", "James"] },
            { "use": "usual", "family": "Windsor", "given": ["Jim"] }
          ],
          "telecom": [
            { "system": "phone", "value": "555-1" },
            { "system": "email", "value": "a@b.example" }
          ]
        }
        """;

    public static ISchema Schema { get; } = FhirVersion.R4.GetSchemaProvider();

    public static IElement Create() => ResourceJsonNode.Parse(Json).ToElement(Schema);
}
