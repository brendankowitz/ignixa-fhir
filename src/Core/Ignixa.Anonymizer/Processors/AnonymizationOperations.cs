using System;
using System.Collections.Generic;
using System.Text;

namespace Ignixa.Anonymizer.Processors
{
    public static class AnonymizationOperations
    {
        public const string Redact = "REDACT";
        public const string Abstract = "ABSTRACT";
        public const string Perturb = "PERTURB";
        public const string CryptoHash = "CRYPTOHASH";
        public const string Encrypt = "ENCRYPT";
        public const string Substitute = "SUBSTITUTE";
        public const string Generalize = "GENERALIZE";
    }
}
