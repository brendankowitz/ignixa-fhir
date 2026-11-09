#nullable enable

using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions;

/// <summary>
/// One reference parameter an <c>_include=*</c> or <c>_revinclude=*</c> follows, on the resource type that holds
/// the reference. Searchability is per type: a parameter hidden on one type can share its storage id with one
/// that is searchable on another, so the pair, not the parameter, is what a wildcard may follow.
/// </summary>
public sealed record WildcardIncludeParameter(string ResourceType, SearchParameterInfo Parameter);
