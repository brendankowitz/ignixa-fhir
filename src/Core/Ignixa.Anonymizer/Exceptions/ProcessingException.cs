// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Anonymizer.Exceptions;

public class ProcessingException : Exception
{
    public ProcessingException()
    {
    }

    public ProcessingException(string message)
        : base(message)
    {
    }

    public ProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
