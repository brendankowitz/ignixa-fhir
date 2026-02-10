// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Ignixa.Anonymizer.Exceptions
{
    // Processing exception. A runtime exception thrown during anonymization process.
    // Customers can set the parameter in configuration file to skip processing the resource if this exception is thrown.
    public class AnonymizerProcessingException : Exception
    {
        public AnonymizerProcessingException(string message) : base(message)
        {
        }

        public AnonymizerProcessingException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
