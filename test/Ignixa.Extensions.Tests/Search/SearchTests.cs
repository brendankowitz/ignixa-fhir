// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ignixa.Domain.ElementModel;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Domain.Specification;
using Hl7.FhirPath;
using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Domain;
using Ignixa.SourceNodeSerialization;
using Ignixa.Specification;
using Ignixa.SourceNodeSerialization;
using Ignixa.Search.Data;
using Ignixa.Search.Indexing;
using Ignixa.Specification.Schema;
using Xunit;

namespace Microsoft.Health.Fhir.Extensions.Tests.Search;

public class SearchTests
{
    private readonly string _patientJson = @"{
  ""resourceType"" : ""Patient"",
  ""name"" : [{
    ""id"" : ""f2"",
    ""use"" : ""official"" ,
    ""given"" : [ ""Karen"", ""May"" ],
    ""_given"" : [ null, {""id"" : ""middle""} ],
    ""family"" :  ""Van"",
    ""_family"" : {""id"" : ""a2""}
   }],
  ""text"" : {
    ""status"" : ""generated"" ,
    ""div"" : ""<div xmlns=\""http://www.w3.org/1999/xhtml\""><p>...</p></div>""
  }
}";

    private static (ISearchIndexer Indexer, IFhirSchemaProvider FhirSchemaProvider) SetupSearchIndexer(FhirSpecification fhirSpecification)
    {
        var schema = new FhirJsonSchemaStructureDefinitionSummaryProvider(fhirSpecification);

        return (SearchIndexerFactory.CreateInstance(schema, NullLoggerFactory.Instance).Result, schema);
    }

    [Fact]
    public void Indexer2()
    {
        using Stream stream = DataLoader.OpenVersionedFileStream(FhirSpecification.R4, "search-parameters.json");
        using var reader = new StreamReader(stream);
        var official = new OfficialFhirSchemaProvider();
        string json = reader.ReadToEnd();

        ITypedElement bundle = FhirJsonNode.Parse(json)
            .ToTypedElement(ModelInfo.ModelInspector);

        ITypedElement[] items = bundle.Select("Bundle.entry[37].resource.component[0].definition.reference").ToArray();

        FhirSpecification fhirSpecification = FhirSpecification.R4;
        var schema2 = new FhirJsonSchemaStructureDefinitionSummaryProvider(fhirSpecification);

        ITypedElement bundle2 = JsonSourceNodeFactory.Parse(json).ToTypedElement(schema2);
        ITypedElement bundle3 = FhirJsonNode.Parse(json).ToTypedElement(schema2);

        ITypedElement[] items2 = bundle2.Select("Bundle.entry[37].resource.component[0].definition").ToArray();
    }

    [Fact]
    public void Indexer()
    {
        (ISearchIndexer Indexer, IFhirSchemaProvider FhirSchemaProvider) context = SetupSearchIndexer(FhirSpecification.R4);

        ITypedElement patient = JsonSourceNodeFactory.Parse(_patientJson).ToTypedElement(context.FhirSchemaProvider);

        IReadOnlyCollection<SearchIndexEntry> indexes = context.Indexer.Extract(patient);
    }

    [Fact]
    public void IndexerR4B()
    {
        (ISearchIndexer Indexer, IFhirSchemaProvider FhirSchemaProvider) contextR4 = SetupSearchIndexer(FhirSpecification.R4);
        (ISearchIndexer Indexer, IFhirSchemaProvider FhirSchemaProvider) contextR4B = SetupSearchIndexer(FhirSpecification.R4B);

        ISourceNode sourceNode = JsonSourceNodeFactory.Parse(_patientJson);
        ITypedElement patientR4 = sourceNode.ToTypedElement(contextR4.FhirSchemaProvider);
        ITypedElement patientR4B = sourceNode.ToTypedElement(contextR4B.FhirSchemaProvider);

        IReadOnlyCollection<SearchIndexEntry> indexesR4 = contextR4.Indexer.Extract(patientR4);
        IReadOnlyCollection<SearchIndexEntry> indexesR4B = contextR4B.Indexer.Extract(patientR4B);
    }

    public class OfficialFhirSchemaProvider : IFhirSchemaProvider
    {
        private readonly ModelInspector _models;

        public OfficialFhirSchemaProvider()
        {
            _models = new ModelInspector(FhirRelease.R4);
            _models.Import(typeof(Patient).Assembly);
        }

        public IStructureDefinitionSummary Provide(string canonical)
        {
            return _models.Provide(canonical);
        }

        public FhirSpecification Version { get; } = FhirSpecification.R4;

        public IReadOnlySet<string> ResourceTypeNames { get; } = ModelInfo.SupportedResources.ToHashSet();
    }
}
