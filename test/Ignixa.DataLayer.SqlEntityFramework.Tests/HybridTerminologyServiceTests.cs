// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

// ValidateBinding_RoutesToSql_WhenImported and ValidateBinding_RoutesToFallback_WhenNotImported were removed:
// HybridTerminologyService's constructor now requires a concrete SqlTerminologyService (not ITerminologyService)
// for its "sql" dependency, and SqlTerminologyService's members are not virtual, so NSubstitute can mock
// neither the parameter type nor the concrete class. A real SqlTerminologyService requires
// SqlEntityFrameworkRepositoryFactory, which only supports SQL Server (no in-memory provider) - there is no
// remaining way to unit-test this routing logic without a live SQL Server. See task-0b-report.md.
public class HybridTerminologyServiceTests
{
}
