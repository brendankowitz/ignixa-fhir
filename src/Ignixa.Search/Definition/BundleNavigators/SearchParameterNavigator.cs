// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Domain.Constants;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Definition.BundleNavigators;

internal class SearchParameterNavigator
{
    private readonly Lazy<IReadOnlyList<string>> _base;
    private readonly Lazy<string> _code;
    private readonly Lazy<IReadOnlyList<ITypedElement>> _component;
    private readonly Lazy<string> _description;
    private readonly Lazy<string> _expression;
    private readonly Lazy<string> _name;
    private readonly Lazy<IReadOnlyList<string>> _target;
    private readonly Lazy<string> _url;
    private readonly Lazy<string> _type;

    public SearchParameterNavigator(ITypedElement searchParameter)
    {
        EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
        EnsureArg.Is(KnownResourceTypes.SearchParameter, searchParameter.InstanceType, StringComparison.Ordinal, nameof(searchParameter));

        _name = new Lazy<string>(() => searchParameter.Scalar("name")?.ToString());
        _code = new Lazy<string>(() => searchParameter.Scalar("code")?.ToString());
        _description = new Lazy<string>(() => searchParameter.Scalar("description")?.ToString());
        _url = new Lazy<string>(() => searchParameter.Scalar("url")?.ToString());
        _expression = new Lazy<string>(() => searchParameter.Scalar("expression")?.ToString());
        _type = new Lazy<string>(() => searchParameter.Scalar("type")?.ToString());

        _base = new Lazy<IReadOnlyList<string>>(() => searchParameter.Select("base")?.AsStringValues().ToArray() ?? Array.Empty<string>());
        _component = new Lazy<IReadOnlyList<ITypedElement>>(() => searchParameter.Select("component")?.ToArray() ?? Array.Empty<ITypedElement>());
        _target = new Lazy<IReadOnlyList<string>>(() => searchParameter.Select("target")?.AsStringValues().ToArray() ?? Array.Empty<string>());
    }

    public string Name => _name.Value;

    public string Code => _code.Value;

    public string Description => _description.Value;

    public string Url => _url.Value;

    public string Type => _type.Value;

    public string Expression => _expression.Value;

    public IReadOnlyList<string> Base => _base.Value;

    public IReadOnlyList<string> Target => _target.Value;

    public IReadOnlyList<ITypedElement> Component => _component.Value;
}
