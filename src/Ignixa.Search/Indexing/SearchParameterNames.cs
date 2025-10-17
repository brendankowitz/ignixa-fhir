// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Indexing;

public static class SearchParameterNames
{
    public const string Id = "_id";

    public const string LastUpdated = "_lastUpdated";

    public const string ResourceType = "_type";

    public const string Date = "date";

    public const string Include = "_include";

    public static readonly Uri IdUri = new("http://hl7.org/fhir/SearchParameter/Resource-id");

    public static readonly Uri LastUpdatedUri = new("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated");

    public static readonly Uri ResourceTypeUri = new("http://hl7.org/fhir/SearchParameter/Resource-type");

    public static readonly Uri TypeUri = new("http://hl7.org/fhir/SearchParameter/type");

    public static readonly Uri ClinicalDateUri = new("http://hl7.org/fhir/SearchParameter/clinical-date");
}
