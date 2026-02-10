// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Linq;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Extensions;
using Xunit;

namespace Ignixa.Anonymizer.Core.UnitTests.Extensions
{
    /// <summary>
    /// FhirPathSymbolExtensions was emptied in the Ignixa migration since
    /// nodesByType/nodesByName are now built into Ignixa FhirPath.
    /// These tests verify the FhirPath expressions work directly via IElement.Select.
    /// </summary>
    public class FhirPathSymbolExtensionsTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        // Note: nodesByName() was removed in v2.0.0 as it has no standard FHIRPath equivalent
        // and was never used in production configurations. See docs/migrations/anonymizer-v1-to-v2.md

        [Fact]
        public void GivenAPatient_WhenNavigateWithNodesByType_MatchNodeShouldBeReturned()
        {
            var json = """{"resourceType":"Patient","active":true,"address":[{"city":"Test0"}],"contact":[{"address":{"city":"Test1"}}]}""";
            var resourceNode = ResourceJsonNode.Parse(json);
            var element = resourceNode.ToElement(_schema);

            var results = element.Select("descendants().ofType(Address)").ToList();
            Assert.Equal(2, results.Count);
        }
    }
}
