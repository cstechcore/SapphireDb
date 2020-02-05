﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SapphireDb.Connection;
using SapphireDb.Internal;
using SapphireDb.Models;
using SapphireDb.Models.SapphireApiBuilder;

namespace SapphireDb.Extensions
{
    public class SapphireDatabaseBuilder
    {
        private readonly IServiceCollection serviceCollection;

        public SapphireDatabaseBuilder(IServiceCollection serviceCollection)
        {
            this.serviceCollection = serviceCollection;
        }

        public SapphireDatabaseBuilder AddContext<TContextType>(
            Action<DbContextOptionsBuilder> dbContextOptions = null,
            string contextName = "Default")
            where TContextType : SapphireDbContext
        {
            DbContextTypeContainer contextTypes = (DbContextTypeContainer)serviceCollection
                .FirstOrDefault(s => s.ServiceType == typeof(DbContextTypeContainer))?.ImplementationInstance;

            // ReSharper disable once PossibleNullReferenceException
            contextTypes.AddContext(contextName, typeof(TContextType));

            serviceCollection.AddDbContext<TContextType>(dbContextOptions, ServiceLifetime.Transient);

            return this;
        }
        
        public SapphireDatabaseBuilder AddActionHandlerConfiguration<T>() where T : class, ISapphireActionConfiguration
        {
            serviceCollection.AddTransient<ISapphireActionConfiguration, T>();
            return this;
        }
        
        public SapphireDatabaseBuilder AddModelConfiguration<T>() where T : class, ISapphireModelConfiguration
        {
            serviceCollection.AddTransient<ISapphireModelConfiguration, T>();
            return this;
        }

        public SapphireDatabaseBuilder AddMessageFilter(string name, Func<HttpInformation, object[], bool> filter)
        {
            SapphireMessageSender.registeredMessageFilter.Add(name.ToLowerInvariant(), filter);
            return this;
        }
        
        public SapphireDatabaseBuilder AddMessageFilter(string name, Func<HttpInformation, bool> filter)
        {
            SapphireMessageSender.registeredMessageFilter.Add(name.ToLowerInvariant(), filter);
            return this;
        }
    }
}
