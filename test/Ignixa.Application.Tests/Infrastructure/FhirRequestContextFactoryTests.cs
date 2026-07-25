// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

/// <summary>
/// A bundle entry is the same request as its parent as far as reference reconciliation is concerned. If the
/// entry context does not inherit the parent's service bases, <c>PUT /Patient/p1</c> inside a transaction
/// stores <c>https://host/Patient/p1</c> as an external reference while the identical standalone request
/// collapses it to internal — and nothing reports the difference.
/// </summary>
public class FhirRequestContextFactoryTests
{
    [Fact]
    public void GivenAParentContextWithServiceBases_WhenCreatingABundleEntryContext_ThenTheEntryInheritsThem()
    {
        var parent = new FhirRequestContext
        {
            TenantId = 1,
            BaseUri = new Uri("https://fhir.example.org/"),
            ServiceBaseUris =
            [
                new Uri("https://fhir.example.org/"),
                new Uri("https://fhir.example.org/tenant/1/")
            ]
        };

        var entry = FhirRequestContextFactory.CreateBundleEntryContext(parent, entryIndex: 0, resourceType: "Patient");

        entry.BaseUri.ShouldBe(parent.BaseUri);
        entry.ServiceBaseUris.ShouldBe(parent.ServiceBaseUris);
    }

    [Fact]
    public void GivenABackgroundContext_WhenCreated_ThenItCarriesNoServiceBasesOfItsOwn()
    {
        var context = FhirRequestContextFactory.CreateBackgroundContext(tenantId: 1);

        context.BaseUri.ShouldBeNull();
        context.ServiceBaseUris.ShouldBeEmpty();
    }
}
