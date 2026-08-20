// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Ignixa.DeId.Configuration;
using Ignixa.DeId.Models;
using Ignixa.DeId.Pipeline;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DeId.Tests.Pipeline;

/// <summary>
/// Pins <see cref="RuleMatchingHandler"/>'s deliberate hoist of <c>context.Element</c> outside the
/// per-rule try/catch (see the remarks on the <c>rootElement</c> local). Had that read stayed inside
/// the try, a getter throw would be caught, downgraded to a warning via <c>context.AddWarning</c>, and
/// the loop would continue - so the pipeline would return the resource un-de-identified with
/// <c>IsSuccess == true</c>. Hoisted, the same throw propagates out of <c>InvokeAsync</c> entirely,
/// past every per-rule catch, so the caller sees a failure instead of a silently unredacted success.
/// </summary>
public class RuleMatchingHandlerTests
{
    [Fact]
    public async Task GivenElementGetterThrows_WhenMatchingRules_ThenTheExceptionPropagatesRatherThanDowngradingToASilentSuccess()
    {
        // Arrange - Schema is deliberately null: DeIdContext's constructor does not validate it, so
        // construction succeeds, and the first read of context.Element (Resource.ToElement(Schema))
        // throws ArgumentNullException. This stands in for any getter failure the hoist is meant to
        // surface loudly instead of swallowing.
        var handler = new RuleMatchingHandler(NullLogger<RuleMatchingHandler>.Instance);
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1","name":[{"family":"Doe"}]}""");
        var options = new DeIdOptions
        {
            FhirVersion = "R4",
            Rules = [
                new FhirPathRule { Path = "Patient.name", Method = "REDACT", ResourceType = "Patient" }
            ]
        };
        var context = new DeIdContext(resource, schema: null!, new RequestOptions(), options);

        var nextHandlerWasInvoked = false;
        PipelineDelegate next = (ctx, ct) =>
        {
            nextHandlerWasInvoked = true;
            return ValueTask.FromResult(Result<DeIdResult>.Success(ctx.BuildResult()));
        };

        // Act & Assert - the exception propagates out of InvokeAsync; it is not caught by the per-rule
        // try/catch (which would have downgraded it to a warning) and the pipeline never reaches
        // nextHandler, so the caller cannot mistake this for a successful (but silently un-de-identified)
        // result.
        await Should.ThrowAsync<ArgumentNullException>(
            async () => await handler.InvokeAsync(context, next, CancellationToken.None));

        nextHandlerWasInvoked.ShouldBeFalse();
        context.Warnings.ShouldBeEmpty();
    }
}
