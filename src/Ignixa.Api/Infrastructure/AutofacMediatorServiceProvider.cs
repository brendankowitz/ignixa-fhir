// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Autofac;
using Medino;

namespace Ignixa.Api.Infrastructure;

/// <summary>
/// Autofac service provider adapter for Medino.
/// </summary>
public sealed class AutofacMediatorServiceProvider : IMediatorServiceProvider
{
    private readonly IComponentContext _context;

    public AutofacMediatorServiceProvider(IComponentContext context)
    {
        _context = context;
    }

    T IMediatorServiceProvider.GetService<T>()
    {
        return _context.Resolve<T>();
    }

    IEnumerable<T> IMediatorServiceProvider.GetServices<T>()
    {
        return _context.Resolve<IEnumerable<T>>();
    }
}
