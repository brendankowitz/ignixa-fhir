/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using Hl7.Fhir.ElementModel;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.Evaluation.Parity;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Firely 5.11.4 throws a <see cref="FormatException"/> subtype as soon as the children of a typed element
/// are enumerated and one of them is absent from the model, yet answers a lookup by name for a modelled
/// sibling without complaint. The adapter therefore has to hand a named lookup to the wrapped element
/// instead of enumerating every child and filtering, or a resource carrying one unmodelled element becomes
/// unreadable. fhir-server hit this indexing the built-in R5 search parameters, where the
/// <c>_expiryDate</c> definition still carries the R4-only <c>xpath</c>.
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
}
