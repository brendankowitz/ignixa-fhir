// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="Code"/> to a list of <see cref="TokenSearchValue"/>.
/// </summary>
public class CodeToTokenSearchValueConverter : FhirElementToSearchValueConverter<TokenSearchValue>
{
    private readonly ICodeSystemResolver _codeSystemResolver;

    public CodeToTokenSearchValueConverter(ICodeSystemResolver codeSystemResolver)
        : base("code", "codeOfT", "System.Code")
    {
        EnsureArg.IsNotNull(codeSystemResolver, nameof(codeSystemResolver));

        _codeSystemResolver = codeSystemResolver;
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        string code = value.Scalar("code") as string ?? value?.Value as string;
        string system = value.Scalar("system") as string;

        // From spec: http://hl7.org/fhir/terminologies.html#4.1
        // The instance represents the code only.
        // The system is implicit - it is defined as part of
        // the definition of the element, and not carried in the instance.
        if (string.IsNullOrWhiteSpace(system) && !string.IsNullOrWhiteSpace(code))
        {
            string lookupSystem = _codeSystemResolver.ResolveSystem(value?.Location);
            if (!string.IsNullOrWhiteSpace(lookupSystem)) system = lookupSystem;
        }

        if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(system))
        {
            yield break;
        }

        yield return new TokenSearchValue(system, code, null);
    }
}
