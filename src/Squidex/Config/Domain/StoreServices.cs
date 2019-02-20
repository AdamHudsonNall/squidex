﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschränkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Migrate_01.Migrations;
using MongoDB.Driver;
using Squidex.Domain.Apps.Entities;
using Squidex.Domain.Apps.Entities.Assets.Repositories;
using Squidex.Domain.Apps.Entities.Assets.State;
using Squidex.Domain.Apps.Entities.Contents.Repositories;
using Squidex.Domain.Apps.Entities.Contents.State;
using Squidex.Domain.Apps.Entities.History.Repositories;
using Squidex.Domain.Apps.Entities.MongoDb.Assets;
using Squidex.Domain.Apps.Entities.MongoDb.Contents;
using Squidex.Domain.Apps.Entities.MongoDb.History;
using Squidex.Domain.Apps.Entities.MongoDb.Rules;
using Squidex.Domain.Apps.Entities.Rules.Repositories;
using Squidex.Domain.Users;
using Squidex.Domain.Users.MongoDb;
using Squidex.Domain.Users.MongoDb.Infrastructure;
using Squidex.Infrastructure;
using Squidex.Infrastructure.Configuration;
using Squidex.Infrastructure.DependencyInjection;
using Squidex.Infrastructure.Diagnostics;
using Squidex.Infrastructure.EventSourcing;
using Squidex.Infrastructure.Json;
using Squidex.Infrastructure.Migrations;
using Squidex.Infrastructure.MongoDb;
using Squidex.Infrastructure.States;
using Squidex.Infrastructure.UsageTracking;

namespace Squidex.Config.Domain
{
    public static class StoreServices
    {
        public static void AddMyStoreServices(this IServiceCollection services, IConfiguration config)
        {
            config.ConfigureByOption("store:type", new Options
            {
                ["MongoDB"] = () =>
                {
                    BsonJsonConvention.Register(SerializationServices.DefaultJsonSerializer);

                    var mongoConfiguration = config.GetRequiredValue("store:mongoDb:configuration");
                    var mongoDatabaseName = config.GetRequiredValue("store:mongoDb:database");
                    var mongoContentDatabaseName = config.GetOptionalValue("store:mongoDb:contentDatabase", mongoDatabaseName);

                    var mongoClient = Singletons<IMongoClient>.GetOrAdd(mongoConfiguration, s => new MongoClient(s));
                    var mongoDatabase = mongoClient.GetDatabase(mongoDatabaseName);
                    var mongoContentDatabase = mongoClient.GetDatabase(mongoContentDatabaseName);

                    services.AddSingleton(typeof(ISnapshotStore<,>), typeof(MongoSnapshotStore<,>));

                    services.AddSingletonAs(mongoDatabase)
                        .As<IMongoDatabase>();

                    services.AddHealthChecks()
                        .AddCheck<MongoDBHealthCheck>("MongoDB", tags: new[] { "node" });

                    services.AddSingletonAs<MongoMigrationStatus>()
                        .As<IMigrationStatus>();

                    services.AddTransientAs<ConvertOldSnapshotStores>()
                        .As<IMigration>();

                    services.AddTransientAs<ConvertRuleEventsJson>()
                        .As<IMigration>();

                    services.AddTransientAs(c => new DeleteContentCollections(mongoContentDatabase))
                        .As<IMigration>();

                    services.AddSingletonAs<MongoUsageRepository>()
                        .AsOptional<IUsageRepository>();

                    services.AddSingletonAs<MongoRuleEventRepository>()
                        .AsOptional<IRuleEventRepository>();

                    services.AddSingletonAs<MongoHistoryEventRepository>()
                        .AsOptional<IHistoryEventRepository>();

                    services.AddSingletonAs<MongoPersistedGrantStore>()
                        .AsOptional<IPersistedGrantStore>();

                    services.AddSingletonAs<MongoRoleStore>()
                        .AsOptional<IRoleStore<IdentityRole>>();

                    services.AddSingletonAs<MongoUserStore>()
                        .AsOptional<IUserStore<IdentityUser>>()
                        .AsOptional<IUserFactory>();

                    services.AddSingletonAs<MongoAssetRepository>()
                        .AsOptional<IAssetRepository>()
                        .AsOptional<ISnapshotStore<AssetState, Guid>>();

                    services.AddSingletonAs(c => new MongoContentRepository(mongoContentDatabase, c.GetRequiredService<IAppProvider>(), c.GetRequiredService<IJsonSerializer>()))
                        .AsOptional<IContentRepository>()
                        .AsOptional<ISnapshotStore<ContentState, Guid>>()
                        .AsOptional<IEventConsumer>();
                }
            });

            services.AddSingleton(typeof(IStore<>), typeof(Store<>));
        }
    }
}
