// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers.Syntax;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>Projects the internal scanned syntax into the public <see cref="SyntaxNode"/> shape.</summary>
internal static class SyntaxProjector
{
    public static SyntaxNode Project(SearchValueSyntax syntax) => syntax switch
    {
        AtomicValueSyntax a => new SyntaxNode("Atomic", a.Span, []),
        MissingValueSyntax m => new SyntaxNode("Missing", m.Span, []),
        OfTypeValueSyntax o => new SyntaxNode("OfType", o.Span, []),
        AlternativesValueSyntax alt => new SyntaxNode(
            "Alternatives", alt.Span, alt.Items.Select(Project).ToList()),
        CompositeValueSyntax c => new SyntaxNode(
            "Composite", c.Span, c.Components.Select(component => Project(component)).ToList()),
        _ => throw new NotSupportedException($"No syntax projection for {syntax.GetType().Name}."),
    };

    public static SyntaxNode Project(SearchKeySyntax syntax) => syntax switch
    {
        ParameterKeySyntax p => new SyntaxNode("ParameterKey", p.Span, []),
        ForwardChainKeySyntax f => new SyntaxNode("ForwardChain", f.Span, [Project(f.Next)]),
        ReverseChainKeySyntax r => new SyntaxNode("ReverseChain", r.Span, [Project(r.Next)]),
        IncludeKeySyntax i => new SyntaxNode("IncludeKey", i.Span, []),
        NotReferencedKeySyntax n => new SyntaxNode("NotReferencedKey", n.Span, []),
        _ => throw new NotSupportedException($"No syntax projection for {syntax.GetType().Name}."),
    };
}
