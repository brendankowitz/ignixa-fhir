/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Serialization;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.Evaluation.Parity;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Firely 5.11.4 throws a <see cref="FormatException"/> subtype when the children of a typed element are
/// enumerated and one is absent from the model, yet answers a named lookup for a modelled sibling. The
/// adapter therefore hands a named lookup to the wrapped element instead of enumerating and filtering.
/// fhir-server hit this indexing the built-in R5 <c>_expiryDate</c> search parameter, which still carries
/// <c>xpath</c>, an element R5 removed. Firely 5 also matches choice names by prefix, so these tests pin
/// the adapter's exact-name filter and adapter sharing too.
/// </summary>
public class IgnixaElementAdapterNamedLookupTests
{
    private const string Url = "http://example.org/SearchParameter/sp1";

    private const string BundleWithUnmodelledElementJson = """
    {
      "resourceType": "Bundle",
      "type": "collection",
      "entry": [
        {
          "resource": {
            "resourceType": "SearchParameter",
            "id": "sp1",
            "url": "http://example.org/SearchParameter/sp1",
            "name": "sp1",
            "status": "active",
            "description": "d",
            "code": "sp1",
            "base": [ "Patient" ],
            "type": "string",
            "notAnElement": "x"
          }
        }
      ]
    }
    """;

    private const string PatientJson = """
    {
      "resourceType": "Patient",
      "name": [ { "family": "a" }, { "family": "b" } ],
      "active": true,
      "deceasedBoolean": false
    }
    """;

    [Fact]
    public void GivenResourceWithUnmodelledElement_WhenLookingUpModelledChildByName_ThenReturnsTheChild()
    {
        var bundle = new IgnixaElementAdapter(FirelyEngine.Parse(BundleWithUnmodelledElementJson).ToScopedNode());
        var resource = bundle.Children("entry")[0].Children("resource")[0];

        var url = resource.Children("url");

        url.Count.ShouldBe(1);
        url[0].Value.ShouldBe(Url);
    }

    [Fact]
    public void GivenResourceWithUnmodelledElement_WhenEvaluatingAPathThroughTheAdapter_ThenReturnsTheValue()
    {
        var bundle = new IgnixaElementAdapter(FirelyEngine.Parse(BundleWithUnmodelledElementJson).ToScopedNode());
        var expression = new FhirPathParser().Parse("entry.resource.url");

        var results = new FhirPathEvaluator().Evaluate(bundle, expression).ToList();

        results.Count.ShouldBe(1);
        results[0].Value.ShouldBe(Url);
    }

    [Theory]
    [InlineData("entry.resource.subsetOf(entry.children())")]
    [InlineData("entry.children().subsetOf(entry.resource)")]
    public void GivenResourceWithUnmodelledElement_WhenComparingItReachedByNameAndByEnumeration_ThenItEqualsItself(string expression)
    {
        var bundle = new IgnixaElementAdapter(FirelyEngine.Parse(BundleWithUnmodelledElementJson).ToScopedNode());

        var results = new FhirPathEvaluator().Evaluate(bundle, new FhirPathParser().Parse(expression)).ToList();

        results.Count.ShouldBe(1);
        results[0].Value.ShouldBe(true);
    }

    [Fact]
    public void GivenChildrenLookedUpByName_WhenEnumeratingAllChildren_ThenReturnsTheSameInstances()
    {
        var patient = new IgnixaElementAdapter(FirelyEngine.Parse(PatientJson).ToScopedNode());
        var names = patient.Children("name");
        var deceased = patient.Children("deceased");

        var all = patient.Children();

        ShouldBeSameInstances(all, "name", names, expectedCount: 2);
        ShouldBeSameInstances(all, "deceased", deceased, expectedCount: 1);
    }

    [Fact]
    public void GivenAllChildrenEnumerated_WhenLookingUpChildrenByName_ThenReturnsTheSameInstances()
    {
        var patient = new IgnixaElementAdapter(FirelyEngine.Parse(PatientJson).ToScopedNode());
        var all = patient.Children();

        var names = patient.Children("name");
        var deceased = patient.Children("deceased");

        ShouldBeSameInstances(all, "name", names, expectedCount: 2);
        ShouldBeSameInstances(all, "deceased", deceased, expectedCount: 1);
    }

    [Fact]
    public void GivenInterleavedSiblings_WhenLookingThemUpByNameBeforeEnumerating_ThenEachLookupKeepsItsValues()
    {
        var name = ParseInterleavedHumanName();

        var given = name.Children("given");
        var all = name.Children();

        given.Select(child => child.Value).ShouldBe(["a", "b", "c"]);
        all.Select(child => child.Value).ShouldBe(["a", "x", "b", "c"]);
    }

    [Fact]
    public void GivenInterleavedSiblings_WhenEnumeratingBeforeLookingThemUpByName_ThenEachLookupKeepsItsValues()
    {
        var name = ParseInterleavedHumanName();

        var all = name.Children();
        var given = name.Children("given");

        given.Select(child => child.Value).ShouldBe(["a", "b", "c"]);
        all.Select(child => child.Value).ShouldBe(["a", "x", "b", "c"]);
    }

    [Fact]
    public void GivenResourceWithUnmodelledElement_WhenLookingUpTheUnmodelledElementByName_ThenReturnsNothing()
    {
        var resource = ParseFirstEntryResource(BundleWithUnmodelledElementJson);

        resource.Children("notAnElement").ShouldBeEmpty();
    }

    [Fact]
    public void GivenResourceWithUnmodelledElement_WhenEnumeratingAllChildren_ThenFirelyErrorSurfaces()
    {
        var resource = ParseFirstEntryResource(BundleWithUnmodelledElementJson);

        Should.Throw<FormatException>(() => resource.Children());
    }

    [Fact]
    public void GivenPassthroughUnmodelledElement_WhenLookingItUpByNameBeforeAndAfterEnumerating_ThenBothReturnNothing()
    {
        var namedFirst = ParsePassthroughFirstEntryResource(BundleWithUnmodelledElementJson);
        var enumeratedFirst = ParsePassthroughFirstEntryResource(BundleWithUnmodelledElementJson);

        var namedBeforeEnumerating = namedFirst.Children("notAnElement");
        var all = enumeratedFirst.Children();
        var namedAfterEnumerating = enumeratedFirst.Children("notAnElement");

        all.ShouldContain(child => child.Name == "notAnElement");
        namedBeforeEnumerating.ShouldBeEmpty();
        namedAfterEnumerating.ShouldBeEmpty();
    }

    [Fact]
    public void GivenChoiceAndSiblingSharingItsPrefix_WhenLookingUpTheChoiceByBaseName_ThenReturnsOnlyTheChoice()
    {
        const string json = """
        {
          "resourceType": "SubstanceSpecification",
          "relationship": [
            {
              "amountQuantity": { "value": 1 },
              "amountType": { "text": "t" }
            }
          ]
        }
        """;
        var relationship = new IgnixaElementAdapter(FirelyEngine.Parse(json).ToScopedNode()).Children("relationship")[0];

        var amount = relationship.Children("amount");

        amount.Count.ShouldBe(1);
        amount[0].Name.ShouldBe("amount");
        amount[0].InstanceType.ShouldBe("Quantity");
    }

    // XML allows repeated elements to be split by a sibling, and Firely restarts the Location index of
    // enumerated children at each run of equal names, so the third given is given[1] there but given[2]
    // in a named lookup.
    private static IElement ParseInterleavedHumanName()
    {
        const string xml = """
        <Patient xmlns="http://hl7.org/fhir">
          <name>
            <given value="a"/>
            <family value="x"/>
            <given value="b"/>
            <given value="c"/>
          </name>
        </Patient>
        """;
        var patient = FhirXmlNode.Parse(xml)
            .ToTypedElement(ModelInspector.ForAssembly(typeof(Hl7.Fhir.Model.Patient).Assembly))
            .ToScopedNode();

        return new IgnixaElementAdapter(patient).Children("name")[0];
    }

    private static IElement ParseFirstEntryResource(string bundleJson) =>
        new IgnixaElementAdapter(FirelyEngine.Parse(bundleJson).ToScopedNode())
            .Children("entry")[0]
            .Children("resource")[0];

    private static IElement ParsePassthroughFirstEntryResource(string bundleJson)
    {
        var settings = new TypedElementSettings { ErrorMode = TypedElementSettings.TypeErrorMode.Passthrough };
        var bundle = FhirJsonNode.Parse(bundleJson)
            .ToTypedElement(ModelInspector.ForAssembly(typeof(Hl7.Fhir.Model.Patient).Assembly), null, settings)
            .ToScopedNode();

        return new IgnixaElementAdapter(bundle).Children("entry")[0].Children("resource")[0];
    }

    private static void ShouldBeSameInstances(
        IReadOnlyList<IElement> allChildren,
        string name,
        IReadOnlyList<IElement> namedChildren,
        int expectedCount)
    {
        var enumerated = allChildren.Where(child => child.Name == name).ToList();

        namedChildren.Count.ShouldBe(expectedCount);
        enumerated.Count.ShouldBe(expectedCount);
        for (int i = 0; i < expectedCount; i++)
        {
            enumerated[i].ShouldBeSameAs(namedChildren[i]);
        }
    }
}
