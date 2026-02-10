// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Ignixa.Anonymizer.Exceptions
{
    public class AddCustomProcessorException : Exception
    {
        public AddCustomProcessorException(string message) : base(message)
        {
        }

        public AddCustomProcessorException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
