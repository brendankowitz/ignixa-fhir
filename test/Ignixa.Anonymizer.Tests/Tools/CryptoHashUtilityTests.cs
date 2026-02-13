// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Anonymizer.Tools;

namespace Ignixa.Anonymizer.Tests.Tools;

public class CryptoHashToolTests
{
    private const string TestHashKey = "123";

    public static IEnumerable<object[]> GetHmacHashData()
    {
        yield return [null!, null!];
        yield return ["", ""];
        yield return ["abc", "8f16771f9f8851b26f4d460fa17de93e2711c7e51337cb8a608a0f81e1c1b6ae"];
        yield return ["&*^%$@()=-,/", "33f6f7d6b3602bf5354dcb4b8d988982602349355f50f86798d8ce1ffd61521b"];
        yield return ["abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()=-", "352b5a4af5adb81fa616c2a5b5c492d0b0b544c188a9aa003767a2b5efbd1478"];
    }

    [Theory]
    [MemberData(nameof(GetHmacHashData))]
    public void GivenAString_WhenComputeHmacSHA256_ThenCorrectHashIsReturned(string input, string expectedHash)
    {
        // Act
        string hash = CryptoHashTool.ComputeHmacSHA256Hash(input, TestHashKey);

        // Assert
        hash.ShouldBe(expectedHash);
    }
}
