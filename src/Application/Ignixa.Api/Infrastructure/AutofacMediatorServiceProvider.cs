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

    object? IMediatorServiceProvider.GetService(Type serviceType)
    {
        return _context.Resolve(serviceType);
    }

    IEnumerable<T> IMediatorServiceProvider.GetServices<T>()
    {
        // Autofac returns empty collection if no services registered
        // Use ResolveOptional to handle missing registrations gracefully
        return _context.Resolve<IEnumerable<T>>() ?? Array.Empty<T>();
    }

    IEnumerable<object> IMediatorServiceProvider.GetServices(Type serviceType)
    {
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);
        var services = _context.Resolve(enumerableType);
        return services as IEnumerable<object> ?? Array.Empty<object>();
    }
}
