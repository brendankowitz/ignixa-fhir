// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Threading.Tasks;

namespace Ignixa.Anonymizer
{
    public interface IFhirDataReader<T>
    {
        Task<T> NextAsync();
    }
}
