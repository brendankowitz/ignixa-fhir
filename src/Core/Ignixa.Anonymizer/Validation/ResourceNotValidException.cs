// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Ignixa.Anonymizer.Validation
{
    public class ResourceNotValidException : Exception
    {
        public ResourceNotValidException(string message) : base(message)
        {
        }
    }
}
