// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Validation.Services.Terminology;

/// <summary>
/// IValueSetProvider decorator that queries additional providers (in declared order)
/// before falling back to the primary. Returns null only when no provider in the
/// chain knows the ValueSet.
/// </summary>
internal sealed class LayeredValueSetProvider : IValueSetProvider
{
    private readonly IValueSetProvider _primary;
    private readonly IReadOnlyList<IValueSetProvider> _additional;

    public LayeredValueSetProvider(IValueSetProvider primary, IEnumerable<IValueSetProvider> additional)
    {
        _primary = primary;
        _additional = additional.ToArray();
    }

    public IReadOnlyList<FhirCode>? GetCodes(string valueSetUrl)
    {
        foreach (var src in _additional)
        {
            var codes = src.GetCodes(valueSetUrl);
            if (codes is not null)
            {
                return codes;
            }
        }
        return _primary.GetCodes(valueSetUrl);
    }

    public bool IsKnownValueSet(string valueSetUrl)
    {
        foreach (var src in _additional)
        {
            if (src.IsKnownValueSet(valueSetUrl))
            {
                return true;
            }
        }
        return _primary.IsKnownValueSet(valueSetUrl);
    }

    public bool? IsValidCode(string valueSetUrl, string code)
    {
        foreach (var src in _additional)
        {
            var result = src.IsValidCode(valueSetUrl, code);
            if (result.HasValue)
            {
                return result;
            }
        }
        return _primary.IsValidCode(valueSetUrl, code);
    }
}
