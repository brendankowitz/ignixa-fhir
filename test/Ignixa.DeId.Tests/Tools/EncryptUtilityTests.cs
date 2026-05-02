// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Text;
using Ignixa.DeId.Tools;

namespace Ignixa.DeId.Tests.Tools;

public class EncryptToolTests
{
    private byte[] Key => Encoding.UTF8.GetBytes("704ab12c8e3e46d4bea600ef62a6bec7");

    public static IEnumerable<object[]> GetTextDataToEncrypt()
    {
        yield return [null!];
        yield return [""];
        yield return ["abc"];
        yield return ["This is for test"];
        yield return ["!@)(*&%^!@#$%@"];
    }

    public static IEnumerable<object[]> GetTextDataToDecrypt()
    {
        yield return [null!, null!];
        yield return ["", ""];
        yield return ["qpxGp6T9DP7wB0EYPQwOYVrScQ/pq3c0D+JQ+hjnfkY=", "abc"];
        yield return ["GI99peR2SpPfcqEgzr7/z7gxYym6qyVPPzvmGc8o8SSwqMpCsW0CRj3v6ZsxFCef", "This is for test"];
        yield return ["JxNRbbxL6pYYpbrtHWZ+1gPTPcIVrLWmrugPiUR9d6k=", "!@)(*&%^!@#$%@"];
    }

    public static IEnumerable<object[]> GetInvalidTextDataToDecrypt()
    {
        yield return ["YWJj"];
        yield return ["U29mdHdhcmUgdGVzdGluZyBpcyBhbiBpbnZlc3RpZ2F0aW9uIGNvbmR1Y3RlZCB0byBwcm92aWRlIGNvbmZpZGVuY2Uu="];
        yield return ["QXMgdGhlIG51bWJlciBvZiBwb3NzaWJsZSB0ZXN0cyBmb3IgZXZlbiBzaW1wbGUgc29m**&&^^"];
    }

    [Theory]
    [MemberData(nameof(GetTextDataToEncrypt))]
    public void GivenOriginalText_WhenEncrypt_ThenResultShouldBeDecryptable(string originalText)
    {
        // Act
        var cipherText = EncryptTool.EncryptTextToBase64WithAes(originalText, Key);
        var plainText = EncryptTool.DecryptTextFromBase64WithAes(cipherText, Key);

        // Assert
        plainText.ShouldBe(originalText);
    }

    [Theory]
    [MemberData(nameof(GetTextDataToDecrypt))]
    public void GivenEncryptedBase64Text_WhenDecrypt_ThenOriginalTextShouldBeReturned(string cipherText, string originalText)
    {
        // Act
        var plainText = EncryptTool.DecryptTextFromBase64WithAes(cipherText, Key);

        // Assert
        plainText.ShouldBe(originalText);
    }

    [Theory]
    [MemberData(nameof(GetInvalidTextDataToDecrypt))]
    public void GivenInvalidBase64Text_WhenDecrypt_ThenFormatExceptionIsThrown(string cipherText)
    {
        Should.Throw<FormatException>(() => EncryptTool.DecryptTextFromBase64WithAes(cipherText, Key));
    }
}
