// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Ignixa.DeId.Tools
{
    /// <summary>
    /// Provides HMAC-SHA256 hashing for de-identification of FHIR element values.
    /// </summary>
    internal class CryptoHashTool
    {
        public static string ComputeHmacSHA256Hash(string input, string hashKey)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            var key = Encoding.UTF8.GetBytes(hashKey);
            using var hmac = new HMACSHA256(key);
            var plainData = Encoding.UTF8.GetBytes(input);
            var hashData = hmac.ComputeHash(plainData);

            return string.Concat(hashData.Select(b => b.ToString("x2")));
        }
    }
}
