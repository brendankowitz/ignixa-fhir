// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Ignixa.Anonymizer.Exceptions
{
    public class InvalidInputException : Exception
    {
        public InvalidInputException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
