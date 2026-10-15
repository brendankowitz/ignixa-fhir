// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Terminology;

/// <summary>An insertion-ordered set of expansion codes keyed by system and code.</summary>
internal sealed class ExpansionBuilder
{
    private readonly List<ExpansionCode> _codes = [];
    private readonly HashSet<(string System, string Code)> _keys = [];

    public int Count => _codes.Count;

    public IReadOnlyList<ExpansionCode> Codes => _codes;

    public bool Contains(ExpansionCode code) => _keys.Contains((code.System, code.Code));

    public void Add(ExpansionCode code)
    {
        if (_keys.Add((code.System, code.Code)))
        {
            _codes.Add(code);
        }
    }

    public void RemoveWhere(Func<ExpansionCode, bool> predicate)
    {
        _codes.RemoveAll(code => predicate(code) && _keys.Remove((code.System, code.Code)));
    }
}
