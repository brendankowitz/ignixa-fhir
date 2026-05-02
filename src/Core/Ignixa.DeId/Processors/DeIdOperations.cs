// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;

namespace Ignixa.DeId.Processors
{
    /// <summary>
    /// Constant strings identifying each supported de-identification operation type.
    /// </summary>
    public static class DeIdOperations
    {
        public const string Redact = "REDACT";
        public const string Abstract = "ABSTRACT";
        public const string Perturb = "PERTURB";
        public const string CryptoHash = "CRYPTOHASH";
        public const string Encrypt = "ENCRYPT";
        public const string Substitute = "SUBSTITUTE";
        public const string Generalize = "GENERALIZE";
        public const string DateShift = "DATESHIFT";
        public const string Keep = "KEEP";
    }
}
