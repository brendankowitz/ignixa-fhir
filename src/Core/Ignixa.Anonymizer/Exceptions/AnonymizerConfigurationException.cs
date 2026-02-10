// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation + Ignixa Contributors
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Ignixa.Anonymizer.Exceptions
{
    public class AnonymizerConfigurationException : Exception
    {
        public AnonymizerConfigurationException(string message) : base(message)
        {
        }

        public AnonymizerConfigurationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
