// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// Scans a raw search key string into a <see cref="Syntax.SearchKeySyntax"/> tree — handwritten and
/// schema-agnostic (structure only: parameter name, modifier, chain/include shape), with no parameter
/// resolution. Binding against the schema is <see cref="SearchKeyBinder"/>'s job.
/// </summary>
internal static class SearchKeySyntaxParser
{
    internal static SearchKeySyntax ParseParameter(string source)
    {
        var cursor = new Cursor(source, "search key");
        SearchKeySyntax syntax = cursor.ParseKey();
        cursor.RequireEnd();
        return syntax;
    }

    internal static IncludeKeySyntax ParseInclude(string source)
    {
        var cursor = new Cursor(source, "include key");
        IncludeKeySyntax syntax = cursor.ParseInclude();
        cursor.RequireEnd();
        return syntax;
    }

    internal static NotReferencedKeySyntax ParseNotReferenced(string source)
    {
        var cursor = new Cursor(source, "_not-referenced value");
        NotReferencedKeySyntax syntax = cursor.ParseNotReferenced();
        cursor.RequireEnd();
        return syntax;
    }

    private ref struct Cursor
    {
        private readonly string _source;
        private readonly string _subject;
        private int _offset;

        internal Cursor(string source, string subject)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(subject);

            _source = source;
            _subject = subject;
            _offset = 0;
        }

        internal bool AtEnd => _offset >= _source.Length;

        internal SearchKeySyntax ParseKey()
        {
            return TryParseReverse(out SearchKeySyntax? syntax)
                ? syntax
                : ParseParameterOrForward();
        }

        internal IncludeKeySyntax ParseInclude()
        {
            var start = _offset;
            var source = ParseIncludeSource();
            Require(':', "':'");

            if (ConsumeIf('*'))
            {
                return new IncludeKeySyntax(source, null, null, true)
                    { Span = new SourceSpan(SourceOrigin.Key, start, _offset - start) };
            }

            var parameter = ParseIdentifier("identifier");
            var targetResourceType = ConsumeIf(':')
                ? ParseIdentifier("identifier")
                : null;

            return new IncludeKeySyntax(source, parameter, targetResourceType, false)
                { Span = new SourceSpan(SourceOrigin.Key, start, _offset - start) };
        }

        internal NotReferencedKeySyntax ParseNotReferenced()
        {
            var start = _offset;
            var sourceResourceType = ConsumeIf('*')
                ? null
                : ParseIdentifier("identifier");

            Require(':', "':'");

            var referencePath = ParseNotReferencedPath();
            return new NotReferencedKeySyntax(sourceResourceType, referencePath)
                { Span = new SourceSpan(SourceOrigin.Key, start, _offset - start) };
        }

        internal void RequireEnd()
        {
            if (!AtEnd)
            {
                throw CreateError(_offset, $"end of {_subject}");
            }
        }

        private SearchKeySyntax ParseParameterOrForward()
        {
            var start = _offset;
            var name = ParseIdentifier("identifier");
            var qualifier = ConsumeIf(':')
                ? ParseIdentifier("identifier")
                : null;

            if (!ConsumeIf('.'))
            {
                return new ParameterKeySyntax(name, qualifier)
                    { Span = new SourceSpan(SourceOrigin.Key, start, _offset - start) };
            }

            return new ForwardChainKeySyntax(name, qualifier, ParseKey())
                { Span = new SourceSpan(SourceOrigin.Key, start, _offset - start) };
        }

        private string ParseIncludeSource()
        {
            return ConsumeIf('*')
                ? "*"
                : ParseIdentifier("identifier");
        }

        private string? ParseNotReferencedPath()
        {
            if (ConsumeIf('*'))
            {
                return null;
            }

            if (AtEnd || !IsAsciiLetter(_source[_offset]))
            {
                throw CreateError(_offset, "identifier");
            }

            return ParseIdentifier("identifier");
        }

        private string ParseIdentifier(string expectation)
        {
            if (AtEnd || !IsIdentifierStart(_source[_offset]))
            {
                throw CreateError(_offset, expectation);
            }

            var start = _offset;
            _offset++;

            while (!AtEnd && IsIdentifierPart(_source[_offset]))
            {
                _offset++;
            }

            return _source[start.._offset];
        }

        private bool TryParseReverse([NotNullWhen(true)] out SearchKeySyntax? syntax)
        {
            var start = _offset;
            var lookaheadOffset = _offset;

            if (!ConsumeLiteralIfAt("_has:", ref lookaheadOffset))
            {
                syntax = null;
                return false;
            }

            int sourceStart = lookaheadOffset;
            if (!TrySkipIdentifier(ref lookaheadOffset))
            {
                syntax = null;
                return false;
            }

            int sourceEnd = lookaheadOffset;
            if (!ConsumeIfAt(':', ref lookaheadOffset))
            {
                syntax = null;
                return false;
            }

            int referenceStart = lookaheadOffset;
            if (!TrySkipIdentifier(ref lookaheadOffset))
            {
                syntax = null;
                return false;
            }

            int referenceEnd = lookaheadOffset;
            if (!ConsumeIfAt(':', ref lookaheadOffset))
            {
                syntax = null;
                return false;
            }

            _offset = lookaheadOffset;
            string sourceResourceType = _source[sourceStart..sourceEnd];
            string referenceName = _source[referenceStart..referenceEnd];
            syntax = new ReverseChainKeySyntax(
                sourceResourceType,
                referenceName,
                ParseKey()) { Span = new SourceSpan(SourceOrigin.Key, start, _offset - start) };
            return true;
        }

        private static bool ConsumeIfAt(char value, ref int offset, string source)
        {
            if (offset >= source.Length || source[offset] != value)
            {
                return false;
            }

            offset++;
            return true;
        }

        private bool ConsumeIf(char value)
        {
            if (AtEnd || _source[_offset] != value)
            {
                return false;
            }

            _offset++;
            return true;
        }

        private bool ConsumeIfAt(char value, ref int offset)
        {
            return ConsumeIfAt(value, ref offset, _source);
        }

        private bool ConsumeLiteralIfAt(string literal, ref int offset)
        {
            if (!_source.AsSpan(offset).StartsWith(literal, StringComparison.Ordinal))
            {
                return false;
            }

            offset += literal.Length;
            return true;
        }

        private bool TrySkipIdentifier(ref int offset)
        {
            if (offset >= _source.Length || !IsIdentifierStart(_source[offset]))
            {
                return false;
            }

            offset++;

            while (offset < _source.Length && IsIdentifierPart(_source[offset]))
            {
                offset++;
            }

            return true;
        }

        private void Require(char value, string expectation)
        {
            if (!ConsumeIf(value))
            {
                throw CreateError(_offset, expectation);
            }
        }

        private InvalidSearchOperationException CreateError(int offset, string expectation)
        {
            return SearchSyntaxExceptionFactory.Create(
                _source,
                offset,
                _subject,
                $"expected {expectation}");
        }

        private static bool IsIdentifierStart(char value)
        {
            return IsAsciiLetter(value) || value == '_';
        }

        private static bool IsIdentifierPart(char value)
        {
            return IsIdentifierStart(value) || IsAsciiDigit(value) || value == '-';
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'A' && value <= 'Z')
                || (value >= 'a' && value <= 'z');
        }

        private static bool IsAsciiDigit(char value)
        {
            return value >= '0' && value <= '9';
        }
    }
}
