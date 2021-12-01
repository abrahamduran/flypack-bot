using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Domain.Models;
using MongoDB.Driver;

namespace FlypackBot.Persistence
{
    public class PackagesRepository
    {
        private const string COLLECTION_NAME_PACKAGES = "packages";
        private const string COLLECTION_NAME_HISTORY = "packagesHistory";

        private readonly IMongoCollection<Package> _packages;
        private readonly IMongoCollection<PackageHistory> _history;
        public PackagesRepository(MongoDbContext context)
        {
            _packages = context.database.GetCollection<Package>(COLLECTION_NAME_PACKAGES);
            _history = context.database.GetCollection<PackageHistory>(COLLECTION_NAME_HISTORY);

            #region Create Indices
            _history.CreateUniqueIndex(x => x.Identifier);
            _history.CreateUniqueIndex(x => x.Tracking);
            _history.CreateIndex(x => x.Username);
            _packages.CreateUniqueIndex(x => x.Identifier);
            _packages.CreateUniqueIndex(x => x.Tracking);
            _packages.CreateIndex(x => x.Username);
            _packages.CreateIndex(x => x.Status.Description);
            _packages.CreateIndex(x => x.Status.Percentage);
            #endregion
        }

        public async Task<Package> GetAsync(string id, CancellationToken cancellationToken = default)
            => (await _packages.FindAsync(x => x.MongoId == id || x.Identifier == id || x.Tracking == id, null, cancellationToken))
                .SingleOrDefault(cancellationToken);

        public async Task<List<Package>> GetPendingAsync(CancellationToken cancellationToken = default)
        {
            var sort = new FindOptions<Package, Package>();
            sort.Sort = Builders<Package>.Sort.Ascending(x => x.DeliveredAt);

            var result = await _packages.FindAsync(x => x.Status.Description != PackageStatus.Delivered.Description, sort, cancellationToken);
            return result.ToList(cancellationToken);
        }

        public async Task UpsertAsync(IEnumerable<Package> packages, CancellationToken cancellationToken = default)
        {
            var updatedAt = DateTime.Now;
            var replacements = packages.Select(x =>
            {
                x.UpdatedAt = updatedAt;
                var filter = Builders<Package>.Filter.Eq(x => x.Identifier, x.Identifier);
                var update = Builders<Package>.Update
                    .Set(s => s.Identifier, x.Identifier)
                    .Set(s => s.Username, x.Username)
                    .Set(s => s.Tracking, x.Tracking)
                    .Set(s => s.Description, x.Description)
                    .Set(s => s.Weight, x.Weight)
                    .Set(s => s.Status, x.Status)
                    .Set(s => s.DeliveredAt, x.DeliveredAt)
                    .Set(s => s.UpdatedAt, x.UpdatedAt);
                return new UpdateOneModel<Package>(filter, update) { IsUpsert = true };
            }).ToList();

            var updates = packages.Select(x =>
            {
                var change = new PackageChange { Status = x.Status.Description, Percentage = x.Status.Percentage, Weight = x.Weight, Date = updatedAt };
                var filter = Builders<PackageHistory>.Filter.Eq(x => x.Identifier, x.Identifier);
                var update = Builders<PackageHistory>.Update
                    .Set(s => s.Identifier, x.Identifier)
                    .Set(s => s.Username, x.Username)
                    .Set(s => s.Tracking, x.Tracking)
                    .AddToSet(u => u.Changes, change);

                return new UpdateOneModel<PackageHistory>(filter, update) { IsUpsert = true };
            }).ToList();

            var task1 = _packages.BulkWriteAsync(replacements, null, cancellationToken);
            var task2 = _history.BulkWriteAsync(updates, null, cancellationToken);

            await Task.WhenAll(task1, task2);
        }
    }
}
