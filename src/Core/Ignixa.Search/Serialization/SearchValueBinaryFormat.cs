// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Serialization;

/// <summary>The tags shared by <see cref="SearchValueBinaryWriter"/> and <see cref="SearchValueBinaryReader"/>.</summary>
internal static class SearchValueBinaryFormat
{
    public const int NullString = 0;
    public const int NewString = 1;
    public const int FirstStringReference = 2;

    public const byte Token = 1;
    public const byte Quantity = 2;
    public const byte Number = 3;
    public const byte String = 4;
    public const byte Reference = 5;
    public const byte DateTime = 6;
    public const byte Uri = 7;
    public const byte Composite = 8;

    public const byte IsMinFlag = 1;
    public const byte IsMaxFlag = 2;
}
