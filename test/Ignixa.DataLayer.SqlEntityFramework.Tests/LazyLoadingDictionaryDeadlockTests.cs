// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Shouldly;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests;

/// <summary>
/// Tests to detect potential deadlock issues in LazyLoadingDictionary sync-over-async pattern.
/// The LazyLoadingDictionary uses .GetAwaiter().GetResult() which can deadlock in certain contexts.
/// </summary>
public class LazyLoadingDictionaryDeadlockTests : TestBase
{
    [Fact]
    public void GivenSystemMappings_WhenCalledFromSyncContext_ThenDoesNotDeadlock()
    {
        var systemUri = "http://loinc.org";
        var systemEntity = new SystemEntity { Value = systemUri };
        Context.Systems.Add(systemEntity);
        Context.SaveChanges();

        var stopwatch = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(5);

        bool completed = false;
        int? resultId = null;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                var result = Cache.SystemMappings.TryGetValue(systemUri, out var systemId);
                if (result)
                {
                    resultId = systemId;
                }
                completed = true;
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        thread.Start();
        thread.Join(timeout);

        stopwatch.Stop();

        capturedException.ShouldBeNull($"Exception occurred: {capturedException?.Message}");
        completed.ShouldBeTrue($"Operation timed out after {timeout.TotalSeconds}s - possible deadlock detected");
        resultId.ShouldNotBeNull();
        resultId.Value.ShouldBeGreaterThan(0);
        stopwatch.Elapsed.ShouldBeLessThan(timeout, "Operation should complete quickly without blocking");
    }

    [Fact]
    public void GivenSystemMappings_WhenCalledConcurrently_ThenAllCallsComplete()
    {
        var systemUri = "http://snomed.info/sct";
        var systemEntity = new SystemEntity { Value = systemUri };
        Context.Systems.Add(systemEntity);
        Context.SaveChanges();

        var timeout = TimeSpan.FromSeconds(10);
        var completedCount = 0;
        var results = new int?[5];
        var exceptions = new Exception?[5];
        var threads = new Thread[5];

        for (int i = 0; i < 5; i++)
        {
            var index = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    var result = Cache.SystemMappings.TryGetValue(systemUri, out var systemId);
                    if (result)
                    {
                        results[index] = systemId;
                        Interlocked.Increment(ref completedCount);
                    }
                }
                catch (Exception ex)
                {
                    exceptions[index] = ex;
                }
            });
        }

        var stopwatch = Stopwatch.StartNew();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join(timeout);
        }

        stopwatch.Stop();

        for (int i = 0; i < exceptions.Length; i++)
        {
            exceptions[i].ShouldBeNull($"Thread {i} threw exception: {exceptions[i]?.Message}");
        }

        completedCount.ShouldBe(5, "All threads should complete successfully");
        results.ShouldAllBe(r => r > 0, "All threads should get valid IDs");
        stopwatch.Elapsed.ShouldBeLessThan(timeout, "All operations should complete without deadlock");
    }

    [Fact]
    public void GivenNewSystem_WhenLazyLoadingCreatesEntry_ThenSucceeds()
    {
        var systemUri = "http://example.org/new-system";
        var timeout = TimeSpan.FromSeconds(5);

        bool completed = false;
        int? resultId = null;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                var result = Cache.SystemMappings.TryGetValue(systemUri, out var systemId);
                if (result)
                {
                    resultId = systemId;
                }
                completed = true;
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        var stopwatch = Stopwatch.StartNew();
        thread.Start();
        thread.Join(timeout);
        stopwatch.Stop();

        capturedException.ShouldBeNull($"Exception occurred: {capturedException?.Message}");
        completed.ShouldBeTrue($"Create operation timed out after {timeout.TotalSeconds}s - possible deadlock");
        resultId.ShouldNotBeNull();
        resultId.Value.ShouldBeGreaterThan(0);
        stopwatch.Elapsed.ShouldBeLessThan(timeout);

        var dbEntry = Context.Systems.FirstOrDefault(s => s.Value == systemUri);
        dbEntry.ShouldNotBeNull("Entry should be persisted");
        dbEntry!.SystemId.ShouldBe(resultId.Value);
    }

    [Fact]
    public void GivenResourceTypeMappings_WhenCalledFromSyncContext_ThenDoesNotDeadlock()
    {
        var timeout = TimeSpan.FromSeconds(5);

        bool completed = false;
        short? resultId = null;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                var result = Cache.ResourceTypeMappings.TryGetValue("Patient", out var resourceTypeId);
                if (result)
                {
                    resultId = resourceTypeId;
                }
                completed = true;
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        var stopwatch = Stopwatch.StartNew();
        thread.Start();
        thread.Join(timeout);
        stopwatch.Stop();

        capturedException.ShouldBeNull($"Exception occurred: {capturedException?.Message}");
        completed.ShouldBeTrue($"Operation timed out after {timeout.TotalSeconds}s - possible deadlock detected");
        resultId.ShouldNotBeNull();
        resultId.ShouldBe((short)1, "Patient should have ResourceTypeId 1");
        stopwatch.Elapsed.ShouldBeLessThan(timeout);
    }

    [Fact]
    public void GivenSearchParameterMappings_WhenCalledFromSyncContext_ThenDoesNotDeadlock()
    {
        var searchParamUri = "http://hl7.org/fhir/SearchParameter/Patient-name";
        var timeout = TimeSpan.FromSeconds(5);

        bool completed = false;
        short? resultId = null;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                var result = Cache.SearchParameterMappings.TryGetValue(searchParamUri, out var searchParamId);
                if (result)
                {
                    resultId = searchParamId;
                }
                completed = true;
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        var stopwatch = Stopwatch.StartNew();
        thread.Start();
        thread.Join(timeout);
        stopwatch.Stop();

        capturedException.ShouldBeNull($"Exception occurred: {capturedException?.Message}");
        completed.ShouldBeTrue($"Operation timed out after {timeout.TotalSeconds}s - possible deadlock detected");
        resultId.ShouldNotBeNull();
        resultId.ShouldBe((short)1, "Patient-name should have SearchParamId 1");
        stopwatch.Elapsed.ShouldBeLessThan(timeout);
    }

    [Fact]
    public void GivenQuantityCodeMappings_WhenCalledFromSyncContext_ThenDoesNotDeadlock()
    {
        var code = "mg";
        var quantityCodeEntity = new QuantityCodeEntity { Value = code };
        Context.QuantityCodes.Add(quantityCodeEntity);
        Context.SaveChanges();

        var timeout = TimeSpan.FromSeconds(5);

        bool completed = false;
        int? resultId = null;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                var result = Cache.QuantityCodeMappings.TryGetValue(code, out var codeId);
                if (result)
                {
                    resultId = codeId;
                }
                completed = true;
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        var stopwatch = Stopwatch.StartNew();
        thread.Start();
        thread.Join(timeout);
        stopwatch.Stop();

        capturedException.ShouldBeNull($"Exception occurred: {capturedException?.Message}");
        completed.ShouldBeTrue($"Operation timed out after {timeout.TotalSeconds}s - possible deadlock detected");
        resultId.ShouldNotBeNull();
        resultId.Value.ShouldBeGreaterThan(0);
        stopwatch.Elapsed.ShouldBeLessThan(timeout);
    }
}
