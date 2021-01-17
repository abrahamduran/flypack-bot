using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Models;
using MongoDB.Driver;

namespace FlypackBot.Persistence
{
    public class PackagesRepository
    {
        private const string COLLECTION_NAME = "packages";

        private readonly IMongoCollection<Package> _packages;
        public PackagesRepository(MongoDbContext context)
        {
            _packages = context.database.GetCollection<Package>(COLLECTION_NAME);

            #region Create Indices
            _packages.CreateUniqueIndex(x => x.Identifier);
            _packages.CreateUniqueIndex(x => x.Tracking);
            _packages.CreateIndex(x => x.Status.Description);
            _packages.CreateIndex(x => x.Status.Percentage);
            #endregion
        }

        public IList<Package> Get()
            => _packages.Find(packages => true)
                .SortByDescending(x => x.Delivered)
                .ToList();

        public async Task<Package> GetAsync(string id, CancellationToken token = default)
            => (await _packages.FindAsync(x => x.MongoId == id || x.Identifier == id || x.Tracking == id, null, token))
                .SingleOrDefault();

        public async Task<IList<Package>> GetPendingAsync(CancellationToken token = default)
            => (await _packages.FindAsync(x => x.Status.Description != PackageStatus.Delivered.Description, null, token)).ToList();

        public async Task<bool> UpsertAsync(IEnumerable<Package> packages, CancellationToken token = default)
        {
            
            var lastUpdated = DateTime.Now;
            var replacements = packages.Select(x =>
            {
                x.LastUpdated = lastUpdated;
                var filter = Builders<Package>.Filter.Eq(x => x.Identifier, x.Identifier);
                return new ReplaceOneModel<Package>(filter, x) { IsUpsert = true };
            }).ToList();

            var write = await _packages.BulkWriteAsync(replacements, null, token);
            return true;
        }
    }
}
