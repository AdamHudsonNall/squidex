﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschränkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using NodaTime;
using Squidex.Domain.Apps.Core.HandleRules;
using Squidex.Domain.Apps.Core.Rules;
using Squidex.Domain.Apps.Entities.Rules;
using Squidex.Domain.Apps.Entities.Rules.Repositories;
using Squidex.Infrastructure;
using Squidex.Infrastructure.MongoDb;
using Squidex.Infrastructure.Reflection;
using Squidex.Infrastructure.Tasks;

namespace Squidex.Domain.Apps.Entities.MongoDb.Rules
{
    public sealed class MongoRuleEventRepository : MongoRepositoryBase<MongoRuleEventEntity>, IRuleEventRepository
    {
        private readonly MongoRuleStatisticsCollection statisticsCollection;

        public MongoRuleEventRepository(IMongoDatabase database)
            : base(database)
        {
            statisticsCollection = new MongoRuleStatisticsCollection(database);
        }

        protected override string CollectionName()
        {
            return "RuleEvents";
        }

        protected override async Task SetupCollectionAsync(IMongoCollection<MongoRuleEventEntity> collection, CancellationToken ct = default)
        {
            await statisticsCollection.InitializeAsync(ct);

            await collection.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<MongoRuleEventEntity>(
                    Index.Ascending(x => x.NextAttempt)),

                new CreateIndexModel<MongoRuleEventEntity>(
                    Index.Ascending(x => x.AppId).Descending(x => x.Created)),

                new CreateIndexModel<MongoRuleEventEntity>(
                    Index
                        .Ascending(x => x.Expires),
                    new CreateIndexOptions
                    {
                        ExpireAfter = TimeSpan.Zero
                    })
            }, ct);
        }

        public Task QueryPendingAsync(Instant now, Func<IRuleEventEntity, Task> callback, CancellationToken ct = default)
        {
            return Collection.Find(x => x.NextAttempt < now).ForEachAsync(callback, ct);
        }

        public async Task<IResultList<IRuleEventEntity>> QueryByAppAsync(DomainId appId, DomainId? ruleId = null, int skip = 0, int take = 20)
        {
            var filter = Filter.Eq(x => x.AppId, appId);

            if (ruleId.HasValue && ruleId.Value != DomainId.Empty)
            {
                filter = Filter.And(filter, Filter.Eq(x => x.RuleId, ruleId.Value));
            }

            var taskForItems = Collection.Find(filter).Skip(skip).Limit(take).SortByDescending(x => x.Created).ToListAsync();
            var taskForCount = Collection.Find(filter).CountDocumentsAsync();

            var (items, total) = await AsyncHelper.WhenAll(taskForItems, taskForCount);

            return ResultList.Create(total, items);
        }

        public async Task<IRuleEventEntity> FindAsync(DomainId id)
        {
            var ruleEvent =
                await Collection.Find(x => x.DocumentId == id)
                    .FirstOrDefaultAsync();

            return ruleEvent;
        }

        public Task EnqueueAsync(DomainId id, Instant nextAttempt)
        {
            return Collection.UpdateOneAsync(x => x.DocumentId == id, Update.Set(x => x.NextAttempt, nextAttempt));
        }

        public async Task EnqueueAsync(RuleJob job, Instant? nextAttempt, CancellationToken ct = default)
        {
            var entity = SimpleMapper.Map(job, new MongoRuleEventEntity { Job = job, Created = job.Created, NextAttempt = nextAttempt });

            await Collection.InsertOneIfNotExistsAsync(entity, ct);
        }

        public Task CancelAsync(DomainId id)
        {
            return Collection.UpdateOneAsync(x => x.DocumentId == id,
                Update
                    .Set(x => x.NextAttempt, null)
                    .Set(x => x.JobResult, RuleJobResult.Cancelled));
        }

        public Task UpdateAsync(RuleJob job, RuleJobUpdate update)
        {
            Guard.NotNull(job, nameof(job));
            Guard.NotNull(update, nameof(update));

            return Task.WhenAll(
                UpdateStatisticsAsync(job, update),
                UpdateEventAsync(job, update));
        }

        private Task UpdateEventAsync(RuleJob job, RuleJobUpdate update)
        {
            return Collection.UpdateOneAsync(x => x.DocumentId == job.Id,
                Update
                    .Set(x => x.Result, update.ExecutionResult)
                    .Set(x => x.LastDump, update.ExecutionDump)
                    .Set(x => x.JobResult, update.JobResult)
                    .Set(x => x.NextAttempt, update.JobNext)
                    .Inc(x => x.NumCalls, 1));
        }

        private async Task UpdateStatisticsAsync(RuleJob job, RuleJobUpdate update)
        {
            if (update.ExecutionResult == RuleResult.Success)
            {
                await statisticsCollection.IncrementSuccess(job.AppId, job.RuleId, update.Finished);
            }
            else
            {
                await statisticsCollection.IncrementFailed(job.AppId, job.RuleId, update.Finished);
            }
        }

        public Task<IReadOnlyList<RuleStatistics>> QueryStatisticsByAppAsync(DomainId appId)
        {
            return statisticsCollection.QueryByAppAsync(appId);
        }
    }
}
