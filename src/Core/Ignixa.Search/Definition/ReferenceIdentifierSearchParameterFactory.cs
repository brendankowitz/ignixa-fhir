// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Definition;

public static partial class ReferenceIdentifierSearchParameterFactory
{
    private const string IdentifierSuffix = ":identifier";
    private const string IdentifierFragment = "#identifier";
    private const string StructureDefinitionBase = "http://hl7.org/fhir/StructureDefinition/";

    public static Uri DeriveUrl(SearchParameterInfo searchParameter)
    {
        ArgumentNullException.ThrowIfNull(searchParameter);

        return new Uri($"{searchParameter.Url}{IdentifierFragment}", UriKind.RelativeOrAbsolute);
    }

    public static string DeriveCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return $"{code}{IdentifierSuffix}";
    }

    public static SearchParameterInfo Create(SearchParameterInfo searchParameter)
    {
        ArgumentNullException.ThrowIfNull(searchParameter);

        if (searchParameter.Type != SearchParamType.Reference)
        {
            throw new ArgumentException("Only reference search parameters have an identifier derivative.", nameof(searchParameter));
        }

        string derivedCode = DeriveCode(searchParameter.Code);
        return new SearchParameterInfo(
            name: derivedCode,
            code: derivedCode,
            searchParamType: SearchParamType.Token,
            url: DeriveUrl(searchParameter),
            expression: DeriveExpression(searchParameter.Expression),
            targetResourceTypes: [],
            baseResourceTypes: searchParameter.BaseResourceTypes)
        {
            IsDerived = true,
            IsSearchable = true,
            IsSupported = true,

            // The base constructor defaults SortStatus to Enabled for every Token parameter, but sorting by
            // an Identifier derived purely to make :identifier searchable has no defined meaning (there is
            // no natural order across identifier system|value pairs) and SearchOptionsBuilder.ParseSortParameters
            // looks sort fields up by their raw code, so an unqualified "_sort=subject:identifier" would
            // otherwise silently be accepted.
            SortStatus = SortParameterStatus.Disabled,
        };
    }

    /// <summary>
    /// Narrows the source expression to the inline <c>Reference.identifier</c> it selects, so the derived
    /// parameter indexes <c>Identifier</c> elements through the ordinary Identifier-to-token conversion.
    /// </summary>
    /// <remarks>
    /// The navigation is deliberately in the expression rather than in a <c>Reference</c>-to-token converter.
    /// Converters are looked up by element type and search value type alone, so such a converter would also
    /// answer for every declared token parameter or composite component that selects a Reference - and the
    /// composite path only falls back to type inference when that lookup fails, which is how
    /// <c>DocumentReference-relationship</c> indexes its reference component. <c>ofType(Reference)</c> limits
    /// the modifier to what the specification defines it over: reference parameters also select canonicals,
    /// uris, attachments and resources, none of which carries a <c>Reference.identifier</c>.
    /// <para>
    /// <c>ofType(CodeableReference).reference</c> is unioned in alongside <c>ofType(Reference)</c> so the
    /// same derived parameter also covers a reference parameter whose own expression selects the whole
    /// R5+ <c>CodeableReference</c> element rather than narrowing to its <c>.reference</c> child - the shape
    /// <c>CodeableReferenceToReferenceSearchValueConverter</c> (Indexing/Converters) exists to index.
    /// No shipped R5 or R6 reference search parameter does this today: every generated one that targets a
    /// <c>CodeableReference</c>-typed element (e.g. <c>CarePlan.addresses</c>, <c>ServiceRequest.code</c>)
    /// already carries the published <c>.reference</c> suffix in its own expression, confirmed by walking
    /// every generated schema element against every generated reference parameter's expression. This is
    /// therefore defensive, for a custom or IG-package reference parameter that selects the bare element.
    /// <c>ofType(CodeableReference)</c> is safe to evaluate unconditionally, including against STU3/R4/R4B
    /// schemas where the type does not exist: <c>TypeMatcher.EnsureTypeIdentifierResolves</c>
    /// (<c>Ignixa.FhirPath.Evaluation</c>), which <c>ofType()</c> calls to enforce "must resolve to a model
    /// type", no-ops when the evaluation context's schema is null, and <c>ElementSearchIndexer.Extract</c>
    /// deliberately builds its evaluation context without one - so this never throws on the write path,
    /// it simply matches nothing on a version that has no such type. <c>.select(a | b)</c> evaluates the
    /// parenthesized source expression exactly once, then applies both branches to each of its elements,
    /// so navigation with side effects or repeated work is not duplicated per branch.
    /// </para>
    /// </remarks>
    private static string DeriveExpression(string sourceExpression) =>
        string.IsNullOrWhiteSpace(sourceExpression)
            ? sourceExpression
            : $"({HonorReferenceType(sourceExpression)}).select(ofType(Reference) | ofType(CodeableReference).reference).identifier";

    /// <summary>
    /// Rewrites every <c>resolve() is TypeName</c> type test in <paramref name="sourceExpression"/> so a
    /// reference also qualifies when its own <c>Reference.type</c> names <c>TypeName</c>, without requiring
    /// <c>resolve()</c> to succeed: <c>resolve() is X</c> becomes
    /// <c>(resolve() is X or type = 'X' or type = 'http://hl7.org/fhir/StructureDefinition/X')</c>. This
    /// applies to every Reference-typed search parameter's derived <c>:identifier</c> expression, including
    /// custom and IG-package parameters reached through
    /// <see cref="ISearchParameterDefinitionManager"/> / <c>CompositeSearchParameterDefinitionManager</c>'s
    /// derived-parameter inclusion - not only the generated base parameters this type's shape census was
    /// built from - so the rewrite must not corrupt a shape the census never saw. See
    /// <see cref="ResolveTypeTestOrLiteralPattern"/> for how FHIRPath comments, string and identifier
    /// literals, and a dotted <c>resolve()</c> receiver are recognized and left alone rather than rewritten.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is needed:</b> a type-filtered reference parameter such as <c>clinical-patient</c>
    /// (<c>Encounter.subject.where(resolve() is Patient)</c>) relies on <c>resolve()</c> to decide whether a
    /// polymorphic reference targets the expected type. <c>resolve()</c> needs <c>Reference.reference</c>
    /// (see <c>FhirSpecificFunctions.Resolve</c>/<c>ExtractReferenceValue</c> and
    /// <c>LightweightReferenceToElementResolver</c>); a LOGICAL reference that carries only
    /// <c>identifier</c> (optionally with <c>type</c>) has nothing for <c>resolve()</c> to look up, so it
    /// never satisfies the type test and its identifier is never indexed under <c>patient:identifier</c> -
    /// the main real-world use of the <c>:identifier</c> modifier
    /// (<c>GET /Encounter?patient:identifier=http://example.org/facilityA|1234</c>). Per the spec,
    /// <c>Reference.type</c> ("the expected type of the target", https://hl7.org/fhir/references.html#Reference)
    /// is exactly the signal needed to honor the filter without resolving anything. This is applied only to
    /// the DERIVED <c>:identifier</c> expression: the engine, <c>resolve()</c>, the lightweight resolver, and
    /// every source (non-derived) reference parameter's own expression are unchanged - a type-filtered
    /// reference parameter without the modifier still requires <c>resolve()</c> to succeed, as the spec
    /// defines.
    /// </para>
    /// <para>
    /// <b>Why text rewriting over AST rewriting:</b> surveying every generated base search parameter across
    /// STU3/R4/R4B/R5/R6 found exactly one shape: the infix operator <c>resolve() is TypeName</c>, always
    /// immediately inside a <c>.where(...)</c> call and never combined with another predicate in the same
    /// <c>where()</c> (232 occurrences total - 0 STU3, 50 R4, 53 R4B, 64 R5, 65 R6 - across 8 distinct target
    /// types: Patient, Group, Location, Practitioner, RelatedPerson, Encounter, Device,
    /// MedicinalProductDefinition; see reference-identifier-search.md for the full census). Parsing the
    /// whole source expression into an <c>Ignixa.FhirPath</c> AST, rewriting the matching <c>BinaryExpression</c>
    /// nodes, and reserializing would need to preserve the existing surface syntax of every untouched node
    /// too - <see cref="Expressions.BinaryExpression.ToString"/> (and its siblings) always re-parenthesize,
    /// so a full round-trip would reformat every reference parameter's derived expression, including the
    /// ~96% that contain no <c>resolve() is</c> test at all, making "unchanged apart from the existing
    /// wrapping" impossible to pin and inflating the parity-corpus and E2E validation surface for no
    /// behavioral gain. A targeted, compiled regex touches only the matched substrings, leaves every other
    /// character of the source expression untouched, and is simple to pin with one test per shape.
    /// </para>
    /// <para>
    /// <b>Shapes deliberately left unrewritten</b> (none occur in any generated base parameter today; a
    /// future one would fail safe by not matching, rather than being rewritten incorrectly):
    /// <list type="bullet">
    ///   <item><description><c>resolve().is(X)</c> (function-call form) - the regex requires literal
    ///   <c>resolve() is</c> (infix form with surrounding whitespace); <c>resolve().is(</c> has a <c>.</c>
    ///   immediately after <c>resolve()</c> and never matches.</description></item>
    ///   <item><description><c>resolve() as X</c> - <c>as</c> yields a value, not the boolean this rewrite
    ///   extends; no generated <c>where()</c> clause uses it with <c>resolve()</c>.</description></item>
    ///   <item><description>Namespaced <c>resolve() is FHIR.X</c> - the negative lookahead after the
    ///   captured type name refuses to match when immediately followed by <c>.</c>, so a namespaced form is
    ///   left untouched rather than being rewritten against only its first segment.</description></item>
    ///   <item><description><c>resolve() is X or resolve() is Y</c> inside one <c>where()</c> - does not
    ///   occur (every multi-target filter is instead written as separate <c>.where(...)</c> clauses joined
    ///   by <c>|</c> at the path level, e.g. <c>Appointment.participant.actor.where(resolve() is Group) |
    ///   Appointment.subject.where(resolve() is Group)</c>); if it occurred, each <c>resolve() is</c>
    ///   occurrence is still rewritten independently by this regex, which is correct for a disjunction
    ///   (<c>(resolve() is X or ...) or (resolve() is Y or ...)</c> is equivalent to the un-rewritten
    ///   disjunction with each side extended).</description></item>
    ///   <item><description><c>resolve()</c> used purely for navigation with no type test at all (e.g. R6
    ///   <c>Specimen.container.device.resolve().location</c>) - there is no <c>is X</c> test to extend, so
    ///   nothing matches and the expression is unchanged, as intended.</description></item>
    ///   <item><description>A dotted receiver, e.g. <c>Observation.where(subject.resolve() is Patient).subject</c>
    ///   - the negative lookbehind before <c>resolve</c> in <see cref="ResolveTypeTestOrLiteralPattern"/>
    ///   refuses to match when the character immediately before it is a word character, <c>.</c>, <c>$</c>,
    ///   or <c>%</c>, so this form is left untouched. Rewriting it naively would splice a parenthesized
    ///   expression directly after a <c>.</c> (<c>subject.(resolve() is Patient or ...)</c>), which does not
    ///   parse; even a corrected rewrite would need <c>type</c> to be evaluated against <c>subject</c>'s
    ///   focus rather than whatever <c>where(...)</c>'s own focus is, which this regex has no way to
    ///   determine. No generated base parameter uses this shape.</description></item>
    /// </list>
    /// No type-filtered shape sees a <c>CodeableReference</c> focus: every base path preceding
    /// <c>.where(resolve() is X)</c> across all five versions (e.g. <c>.subject</c>, <c>.actor</c>,
    /// <c>.target</c>, <c>.for</c>, <c>.careManager</c>, <c>.who</c>, <c>.what</c>, <c>.device</c>) is a
    /// plain identity/participant <c>Reference</c> element per the FHIR spec in every version that defines
    /// it - none of the elements the spec changed to <c>CodeableReference</c> in R5 (reason/addresses-style
    /// dual code-or-reference fields) appear here, and every one already has <c>resolve()</c> declared with
    /// <c>SupportedContexts = "Reference-Resource"</c>, so <c>type</c> is evaluated directly against the
    /// same <c>Reference</c> focus <c>resolve()</c> already operates on.
    /// </para>
    /// </remarks>
    private static string HonorReferenceType(string sourceExpression)
    {
        try
        {
            return ResolveTypeTestOrLiteralPattern().Replace(
                sourceExpression,
                static match => match.Groups["type"].Success
                    ? $"(resolve() is {match.Groups["type"].Value} or type = '{match.Groups["type"].Value}' or type = '{StructureDefinitionBase}{match.Groups["type"].Value}')"
                    : match.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail safe rather than cost unbounded time per Create() call: an expression pathological
            // enough to still exceed the match timeout set on ResolveTypeTestOrLiteralPattern (every
            // comment and literal alternative below is already atomic/backtracking-free, so this is
            // defense in depth, not the expected path) is returned unchanged. The derived parameter then
            // behaves exactly as it did before this rewrite existed: a type-filtered reference parameter
            // without the modifier still requires resolve() to succeed. There is no ILogger available in
            // this static factory to record the fallback, hence this comment instead of a log line.
            return sourceExpression;
        }
    }

    /// <summary>
    /// Matches, at each position .NET regex scanning tries in turn: a whole <c>//</c> line comment, a whole
    /// <c>/* */</c> block comment, a whole single-quoted string literal, a whole legacy double-quoted
    /// delimited identifier, a whole backtick-delimited identifier, or - only once none of those match - the
    /// infix <c>resolve() is TypeName</c> type test outside of a dotted receiver.
    /// <see cref="HonorReferenceType"/> rewrites only a match that populated the <c>type</c> group; every
    /// other alternative is returned unchanged, so the literal text <c>resolve() is TypeName</c> appearing
    /// inside a comment, a string, or a delimited identifier - rather than as actual FHIRPath syntax - is
    /// never touched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why comments and literals are matched here too:</b> .NET regex's leftmost-match scanning tries the
    /// alternatives in order at each position and commits to whichever first succeeds
    /// (<see cref="Regex.Replace(string, MatchEvaluator)"/>), so a comment's opening <c>//</c> or <c>/*</c>,
    /// or a literal's opening quote or backtick, is claimed by its own alternative before the
    /// <c>resolve() is</c> alternative ever gets a chance to look inside it; the matched span is then
    /// consumed as a whole and scanning resumes immediately after it, correctly treating everything between
    /// the delimiters - including a <c>resolve() is</c> run - as opaque text. Each of the five
    /// skip-alternatives wraps its body in an atomic group (<c>(?&gt;...)</c>), so once a character has been
    /// consumed into the run the engine can never backtrack into it character-by-character while searching
    /// for a closing delimiter that turns out not to exist; an unclosed comment or literal instead falls
    /// through to matching end-of-input (<c>\z</c>) directly, consuming the remainder of the input in one
    /// step. This is what keeps matching linear in input length: the prior pattern had no comment
    /// alternative at all and used ordinary backtracking repetition for its string alternative, so an
    /// unclosed quote inside what is actually a <c>//</c> comment (legal FHIRPath, e.g.
    /// <c>Observation.subject // '\'\'\'...</c>) made the engine retry the quote alternative at every
    /// position up to end-of-input - quadratic in the comment's length. As defense in depth against any
    /// input this reasoning missed, the <see cref="GeneratedRegexAttribute"/> below also sets a match
    /// timeout; <see cref="HonorReferenceType"/> catches <see cref="RegexMatchTimeoutException"/> and
    /// returns the source expression unchanged rather than risk unbounded matching time.
    /// </para>
    /// <para>
    /// The string alternative, <c>'(?&gt;(?:''|\\[\s\S]?|[^'\\])*)(?:'|\z)</c>, accepts both escape styles
    /// <c>FhirPathTokenizer</c>'s own string-literal regex accepts - a doubled quote <c>''</c> and a
    /// backslash followed by any single character - as a unit, so an escaped quote cannot be mistaken for
    /// the closing delimiter. Per the FHIRPath N1 grammar, a backslash escape is <c>\</c> followed by one of
    /// <c>` ' " \ / f n r t</c> or a <c>\uXXXX</c> sequence; <c>''</c> is not a spec-defined escape at all -
    /// it is tolerated only because <c>FhirPathTokenizer</c>'s own string-literal regex accepts it, and this
    /// pattern mirrors that tokenizer rather than the spec grammar so a tokenizable expression is never
    /// miscategorized here. The double-quote alternative, <c>"(?&gt;(?:[^"\\]|\\[\s\S]?)*)(?:"|\z)</c>,
    /// mirrors the tokenizer's legacy double-quoted delimited-identifier regex the same way. The backtick
    /// alternative, <c>`[^`]*(?:`|\z)</c>, deliberately has no escape handling, because
    /// <c>FhirPathTokenizer</c>'s own delimited-identifier regex (<c>`[^`]*`</c>) has none either: the first
    /// backtick after the opening one always ends a delimited identifier in this engine.
    /// </para>
    /// <para>
    /// The <c>resolve() is TypeName</c> alternative requires that the character immediately before
    /// <c>resolve</c> is not a word character, <c>.</c>, <c>$</c>, or <c>%</c> (<c>(?&lt;![\w.$%])</c>), so a
    /// dotted receiver such as <c>subject.resolve() is Patient</c> is left unrewritten (see
    /// <see cref="HonorReferenceType"/>'s "Shapes deliberately left unrewritten" list for why). It also
    /// captures the type name inside an atomic group <c>(?&gt;...)</c> so the trailing <c>(?!\.)</c> cannot
    /// be satisfied by backtracking to a shorter prefix of the name (without the atomic group,
    /// <c>FHIR.Patient</c> would backtrack from <c>FHIR</c> down to <c>FHI</c>, pass the lookahead there, and
    /// get wrongly rewritten against only that truncated prefix). With the atomic group, the full identifier
    /// run is matched once and committed; if a <c>.</c> immediately follows, the whole match fails at that
    /// position and the namespaced form - which does not occur in any generated base parameter today - is
    /// left untouched.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"//[^\r\n]*|/\*(?>(?:[^*]|\*(?!/))*)(?:\*/|\z)|'(?>(?:''|\\[\s\S]?|[^'\\])*)(?:'|\z)|" +
        @"""(?>(?:[^""\\]|\\[\s\S]?)*)(?:""|\z)|`[^`]*(?:`|\z)|" +
        @"(?<![\w.$%])resolve\(\)\s+is\s+(?>(?<type>[A-Za-z][A-Za-z0-9]*))(?!\.)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ResolveTypeTestOrLiteralPattern();

    /// <summary>
    /// Reverses <see cref="DeriveUrl"/>: given a derived identifier URL (<c>{url}#identifier</c>), returns
    /// the source reference parameter's URL without the fragment.
    /// </summary>
    public static bool TryGetSourceUrl(Uri derivedUrl, out Uri sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(derivedUrl);

        string originalString = derivedUrl.OriginalString;
        if (originalString.EndsWith(IdentifierFragment, StringComparison.Ordinal))
        {
            sourceUrl = new Uri(originalString[..^IdentifierFragment.Length], UriKind.RelativeOrAbsolute);
            return true;
        }

        sourceUrl = null;
        return false;
    }

    public static bool TryResolve(
        ISearchParameterDefinitionManager definitionManager,
        SearchParameterInfo searchParameter,
        out SearchParameterInfo derivedSearchParameter)
    {
        ArgumentNullException.ThrowIfNull(definitionManager);
        ArgumentNullException.ThrowIfNull(searchParameter);

        return definitionManager.TryGetSearchParameter(DeriveUrl(searchParameter), out derivedSearchParameter);
    }
}
