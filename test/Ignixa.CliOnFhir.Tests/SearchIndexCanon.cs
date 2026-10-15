// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// A canonical string for indexed resources that spells out every public field of every search value type,
/// so two indexes compare equal only if nothing a search could observe differs. The values' own
/// <c>Equals</c>/<c>ToString</c> are not used: they ignore case, flags and sub-fields.
/// </summary>
internal static class SearchIndexCanon
{
    public static IReadOnlyList<string> Describe(ResourceTypeIndex index) =>
    [
        .. index.Resources.Select(resource =>
            $"{resource.Key.ResourceType}/{resource.Key.Id} @{resource.Entry.FilePath}:{resource.Entry.Offset}+{resource.Entry.Length}\n  "
            + string.Join("\n  ", resource.Index.Select(Describe))),
    ];

    public static string Describe(SearchIndexEntry entry)
    {
        var parameter = entry.SearchParameter;
        var writer = new Writer();
        entry.Value.AcceptVisitor(writer);
        return $"{parameter.Code}|{parameter.Url}|{parameter.Type}|{parameter.Name}|{parameter.Expression}|{parameter.SortStatus} => {writer}";
    }

    public static string Describe(ISearchValue value)
    {
        var writer = new Writer();
        value.AcceptVisitor(writer);
        return writer.ToString();
    }

    private static string Quote(string? value) => value is null ? "<null>" : $"\"{value}\"";

    private static string Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "<null>";

    private sealed class Writer : ISearchValueVisitor
    {
        private readonly StringBuilder _text = new();

        public override string ToString() => _text.ToString();

        public void Visit(CompositeIndexSearchValue composite)
        {
            _text.Append("Composite[");
            foreach (var component in composite.Components)
            {
                _text.Append('(');
                foreach (var value in component)
                {
                    value.AcceptVisitor(this);
                    _text.Append(';');
                }

                _text.Append(')');
            }

            _text.Append(']');
        }

        public void Visit(DateTimeSearchValue dateTime) =>
            _text.Append(CultureInfo.InvariantCulture, $"Date[{dateTime.Start:o}..{dateTime.End:o} min={dateTime.IsMin} max={dateTime.IsMax} text={Quote(dateTime.ToString())}]");

        public void Visit(NumberSearchValue number) =>
            _text.Append($"Number[{Number(number.Low)}..{Number(number.High)}]");

        public void Visit(QuantitySearchValue quantity) =>
            _text.Append($"Quantity[{Number(quantity.Low)}..{Number(quantity.High)} system={Quote(quantity.System)} code={Quote(quantity.Code)}]");

        public void Visit(ReferenceSearchValue reference) =>
            _text.Append($"Reference[{reference.Kind} base={Quote(reference.BaseUri?.OriginalString)} type={Quote(reference.ResourceType)} id={Quote(reference.ResourceId)}]");

        public void Visit(StringSearchValue s) =>
            _text.Append($"String[{Quote(s.String)} min={s.IsMin} max={s.IsMax}]");

        public void Visit(TokenSearchValue token) =>
            _text.Append($"Token[system={Quote(token.System)} code={Quote(token.Code)} text={Quote(token.Text)} typeSystem={Quote(token.IdentifierTypeSystem)} typeCode={Quote(token.IdentifierTypeCode)}]");

        public void Visit(OfTypeTokenSearchValue ofTypeToken) =>
            _text.Append($"OfType[{Quote(ofTypeToken.IdentifierValue)} {Quote(ofTypeToken.TypeSystem)} {Quote(ofTypeToken.TypeCode)}]");

        public void Visit(UriSearchValue uri) =>
            _text.Append($"Uri[{Quote(uri.Uri)} version={Quote(uri.Version)} fragment={Quote(uri.Fragment)}]");
    }
}
