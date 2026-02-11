// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Anonymizer.Exceptions;

public class CustomProcessorException : Exception
{
    public CustomProcessorException()
    {
    }

    public CustomProcessorException(string message)
        : base(message)
    {
    }

    public CustomProcessorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
