// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>Runs tests that assert on the process-wide expression caches without parallel interference.</summary>
[CollectionDefinition("SharedExpressionCache", DisableParallelization = true)]
public sealed class SharedExpressionCacheGroup;
