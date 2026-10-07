// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure.Audit;
using Ignixa.Domain.Exceptions;
using Ignixa.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Shouldly;

namespace Ignixa.Application.Tests.Infrastructure.Audit;

public class CustomAuditHeadersTests
{
    [Fact]
    public void GivenPrefixedHeadersInAnyCase_WhenReading_ThenOnlyThoseAreCapturedWithValues()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-IGNIXA-AUDIT-OPERATIONID"] = "op-1";
        context.Request.Headers["x-ignixa-audit-bundleid"] = "bundle-7";
        context.Request.Headers["X-IGNIXA-OTHER"] = "ignored";
        context.Request.Headers["Authorization"] = "Bearer secret";

        var headers = CustomAuditHeaders.Read(context);

        headers.Count.ShouldBe(2);
        headers["X-IGNIXA-AUDIT-OPERATIONID"].ShouldBe("op-1");
        headers["x-ignixa-audit-bundleid"].ShouldBe("bundle-7");
    }

    [Fact]
    public void GivenNoPrefixedHeaders_WhenReading_ThenResultIsEmpty()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Request-Id"] = "abc";

        CustomAuditHeaders.Read(context).ShouldBeEmpty();
    }

    [Fact]
    public void GivenAzureFhirCompatibilityHeaders_WhenReading_ThenTheyAreCapturedAlongsideIgnixaHeaders()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-MS-AZUREFHIR-AUDIT-OPERATIONID"] = "ahds-op";
        context.Request.Headers["x-ms-azurefhir-audit-bundleid"] = "ahds-bundle";
        context.Request.Headers["X-IGNIXA-AUDIT-SITE"] = "site-1";

        var headers = CustomAuditHeaders.Read(context);

        headers.Count.ShouldBe(3);
        headers["X-MS-AZUREFHIR-AUDIT-OPERATIONID"].ShouldBe("ahds-op");
        headers["x-ms-azurefhir-audit-bundleid"].ShouldBe("ahds-bundle");
        headers["X-IGNIXA-AUDIT-SITE"].ShouldBe("site-1");
    }

    [Fact]
    public void GivenElevenHeadersSplitAcrossBothPrefixes_WhenReading_ThenTheSharedLimitRejectsThem()
    {
        var context = new DefaultHttpContext();
        for (var i = 0; i < 6; i++)
        {
            context.Request.Headers[$"X-IGNIXA-AUDIT-H{i}"] = "v";
            if (i < 5)
            {
                context.Request.Headers[$"X-MS-AZUREFHIR-AUDIT-H{i}"] = "v";
            }
        }

        Should.Throw<AuditHeaderCountExceededException>(() => CustomAuditHeaders.Read(context))
            .ActualCount.ShouldBe(11);
    }

    [Fact]
    public void GivenOversizedAzureFhirCompatibilityHeader_WhenReading_Then431()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-MS-AZUREFHIR-AUDIT-SITE"] = new string('x', CustomAuditHeaders.MaximumValueLength + 1);

        Should.Throw<AuditHeaderTooLargeException>(() => CustomAuditHeaders.Read(context))
            .HeaderName.ShouldBe("X-MS-AZUREFHIR-AUDIT-SITE");
    }

    [Fact]
    public void GivenMultiValuedHeader_WhenReading_ThenValuesAreCommaJoined()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-IGNIXA-AUDIT-SITE"] = new StringValues(["a", "b"]);

        CustomAuditHeaders.Read(context)["X-IGNIXA-AUDIT-SITE"].ShouldBe("a,b");
    }

    [Fact]
    public void GivenMaximumHeaderCount_WhenReading_ThenAllAreCaptured()
    {
        var context = ContextWithHeaders(CustomAuditHeaders.MaximumCount);

        CustomAuditHeaders.Read(context).Count.ShouldBe(10);
    }

    [Fact]
    public void GivenOneHeaderTooMany_WhenReading_Then431WithAhdsMessage()
    {
        var context = ContextWithHeaders(CustomAuditHeaders.MaximumCount + 1);

        var exception = Should.Throw<AuditHeaderCountExceededException>(() => CustomAuditHeaders.Read(context));

        exception.StatusCode.ShouldBe(431);
        exception.Message.ShouldBe(
            "The maximum number of custom audit headers allowed is 10. The number of custom audit headers supplied is 11.");
        var issue = exception.OperationOutcome.Issue.ShouldHaveSingleItem();
        issue.SeverityCode.ShouldBe(OperationOutcomeIssue.IssueSeverityCode.Error);
        issue.IssueTypeCode.ShouldBe(OperationOutcomeIssue.IssueTypeCommon.Invalid);
    }

    [Fact]
    public void GivenValueAtMaximumLength_WhenReading_ThenItIsCaptured()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-IGNIXA-AUDIT-SITE"] = new string('a', CustomAuditHeaders.MaximumValueLength);

        CustomAuditHeaders.Read(context)["X-IGNIXA-AUDIT-SITE"].Length.ShouldBe(2048);
    }

    [Fact]
    public void GivenValueOverMaximumLength_WhenReading_Then431NamingHeaderAndLengthButNotValue()
    {
        var context = new DefaultHttpContext();
        var value = "secret-" + new string('z', CustomAuditHeaders.MaximumValueLength);
        context.Request.Headers["X-IGNIXA-AUDIT-SITE"] = value;

        var exception = Should.Throw<AuditHeaderTooLargeException>(() => CustomAuditHeaders.Read(context));

        exception.StatusCode.ShouldBe(431);
        exception.Message.ShouldBe(
            $"The maximum length of a custom audit header value is 2048. The supplied custom audit header 'X-IGNIXA-AUDIT-SITE' has length of {value.Length}.");
        exception.Message.ShouldNotContain("secret-");
        exception.OperationOutcome.Issue.ShouldHaveSingleItem().IssueTypeCode
            .ShouldBe(OperationOutcomeIssue.IssueTypeCommon.Invalid);
    }

    [Fact]
    public void GivenHeadersAlreadyRead_WhenRequestHeadersChange_ThenCachedResultIsReturned()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-IGNIXA-AUDIT-A"] = "1";
        var first = CustomAuditHeaders.Read(context);

        context.Request.Headers["X-IGNIXA-AUDIT-B"] = "2";

        CustomAuditHeaders.Read(context).ShouldBeSameAs(first);
    }

    [Fact]
    public void GivenMixedHeaders_WhenCopying_ThenOnlyCustomAuditHeadersAreCopied()
    {
        var source = new HeaderDictionary
        {
            ["X-IGNIXA-AUDIT-OPERATIONID"] = "op-1",
            ["Authorization"] = "Bearer secret",
            ["X-TTL"] = "P1D",
            ["X-MS-AZUREFHIR-AUDIT-BUNDLEID"] = "ahds-bundle"
        };
        var target = new HeaderDictionary();

        CustomAuditHeaders.CopyTo(source, target);

        target.Count.ShouldBe(2);
        target["X-IGNIXA-AUDIT-OPERATIONID"].ToString().ShouldBe("op-1");
        target["X-MS-AZUREFHIR-AUDIT-BUNDLEID"].ToString().ShouldBe("ahds-bundle");
    }

    [Fact]
    public void GivenHeadersWithControlCharacters_WhenFormatting_ThenOrderedPairsHaveNoLineBreaks()
    {
        var formatted = CustomAuditHeaders.Format(new Dictionary<string, string>
        {
            ["X-IGNIXA-AUDIT-B"] = "two\r\nFAKE=entry",
            ["X-IGNIXA-AUDIT-A"] = "one"
        });

        formatted.ShouldBe("X-IGNIXA-AUDIT-A=one;X-IGNIXA-AUDIT-B=two  FAKE=entry");
    }

    private static DefaultHttpContext ContextWithHeaders(int count)
    {
        var context = new DefaultHttpContext();
        for (var i = 0; i < count; i++)
        {
            context.Request.Headers[$"X-IGNIXA-AUDIT-H{i}"] = $"v{i}";
        }

        return context;
    }
}
