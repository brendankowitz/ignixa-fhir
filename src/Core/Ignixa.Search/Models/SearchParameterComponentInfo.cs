// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Models;

public class SearchParameterComponentInfo
{
    public SearchParameterComponentInfo(Uri definitionUrl = null, string expression = null)
    {
        DefinitionUrl = definitionUrl;
        Expression = expression;
    }

    public Uri DefinitionUrl { get; }

    /// <summary>
    /// The component's FHIRPath expression, relative to the composite's. Set again only by
    /// <c>CompositeComponentDefinitionRepairs</c>, for a shipped definition whose expression is wrong.
    /// </summary>
    public string Expression { get; internal set; }

    public SearchParameterInfo ResolvedSearchParameter { get; set; }
}
