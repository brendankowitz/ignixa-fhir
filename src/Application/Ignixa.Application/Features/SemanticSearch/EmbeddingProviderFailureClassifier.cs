// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.SemanticSearch;

/// <summary>
/// Classifies an exception from an <see cref="Microsoft.Extensions.AI.IEmbeddingGenerator{TInput, TEmbedding}"/>
/// call as a provider outage worth reporting through <see cref="EmbeddingUnavailableException"/>. Shared
/// by every caller that embeds through the provider directly -- <see cref="SemanticIndexer"/> on the
/// write path and <see cref="SemanticQueryPreparer"/> on the query path -- so the two never classify the
/// same failure differently.
/// </summary>
internal static class EmbeddingProviderFailureClassifier
{
    /// <summary>
    /// True for a provider failure that should be reported as <see cref="EmbeddingUnavailableException"/>:
    /// a transport or SDK-level failure (<see cref="HttpRequestException"/>,
    /// <see cref="System.ClientModel.ClientResultException"/>, <see cref="Azure.RequestFailedException"/>),
    /// or a cancellation the provider itself raised (a request timeout) rather than one caused by
    /// <paramref name="cancellationToken"/>. The caller's own cancellation must propagate unchanged, not be
    /// reported as a provider outage.
    /// </summary>
    public static bool IsProviderUnavailable(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        HttpRequestException => true,
        System.ClientModel.ClientResultException => true,
        Azure.RequestFailedException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false
    };
}
