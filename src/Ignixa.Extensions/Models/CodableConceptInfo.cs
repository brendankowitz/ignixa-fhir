// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Ignixa.Extensions.Models;

public class CodableConceptInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CodableConceptInfo"/> class.
    /// </summary>
    public CodableConceptInfo()
    {
        Coding = new List<CodingInfo>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CodableConceptInfo"/> class.
    /// </summary>
    /// <param name="coding">The Coding collection.</param>
    public CodableConceptInfo(IEnumerable<CodingInfo> coding)
    {
        EnsureArg.IsNotNull(coding);

        Coding = coding.ToList();
    }

    /// <summary>
    /// Gets the Coding collection.
    /// </summary>
    public ICollection<CodingInfo> Coding { get; }
}
