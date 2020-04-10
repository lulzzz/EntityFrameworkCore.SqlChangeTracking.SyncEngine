﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Monitoring;
using EntityFrameworkCore.SqlChangeTracking.SyncEngine.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EntityFrameworkCore.SqlChangeTracking.SyncEngine.Hosting
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddHostedSyncEngineService<TContext>(this IServiceCollection services, Action<SyncEngineOptions>? optionsBuilder, Func<Type, bool>? processorTypePredicate, params Assembly[] assemblies) where TContext : DbContext
        {
            var options = new SyncEngineOptions();

            optionsBuilder?.Invoke(options);

            processorTypePredicate ??= type => true;

            services.AddSingleton<IHostedService>(s => new SyncEngineHostedService<TContext>(s.GetRequiredService<ISyncEngine<TContext>>(), options));
            services.AddSyncEngine<TContext>(options.SyncContext, processorTypePredicate, assemblies);

            return services;
        }

        public static IServiceCollection AddHostedSyncEngineService<TContext>(this IServiceCollection services, params Assembly[] assemblies) where TContext : DbContext
        {
            return services.AddHostedSyncEngineService<TContext>(null, null, assemblies);
        }

        public static IServiceCollection AddHostedSyncEngineService<TContext>(this IServiceCollection services, Func<Type, bool> processorTypePredicate, params Assembly[] assemblies) where TContext : DbContext
        {
            return services.AddHostedSyncEngineService<TContext>(null, processorTypePredicate, assemblies);
        }
    }
}
