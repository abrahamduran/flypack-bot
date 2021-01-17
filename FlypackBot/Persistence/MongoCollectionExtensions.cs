using System;
using System.Linq.Expressions;
using MongoDB.Driver;

namespace FlypackBot.Persistence
{
    public static class MongoCollectionExtensions
    {
        public static string CreateIndex<TDocument>(this IMongoCollection<TDocument> collection, Expression<Func<TDocument, object>> field)
            => collection.CreateIndex(Builders<TDocument>.IndexKeys.Ascending(field), new CreateIndexOptions());

        public static string CreateIndex<TDocument>(this IMongoCollection<TDocument> collection, FieldDefinition<TDocument> field)
            => collection.CreateIndex(Builders<TDocument>.IndexKeys.Ascending(field), new CreateIndexOptions());

        public static string CreateTextIndex<TDocument>(this IMongoCollection<TDocument> collection, Expression<Func<TDocument, object>> field)
            => collection.CreateTextIndex(Builders<TDocument>.IndexKeys.Text(field));

        public static string CreateTextIndex<TDocument>(this IMongoCollection<TDocument> collection, FieldDefinition<TDocument> field)
            => collection.CreateTextIndex(Builders<TDocument>.IndexKeys.Text(field));

        public static string CreateTextIndex<TDocument>(this IMongoCollection<TDocument> collection, IndexKeysDefinition<TDocument> keys)
            => collection.CreateIndex(keys, new CreateIndexOptions { DefaultLanguage = "spanish" });

        public static string CreateUniqueIndex<TDocument>(this IMongoCollection<TDocument> collection, Expression<Func<TDocument, object>> field)
            => collection.CreateUniqueIndex(Builders<TDocument>.IndexKeys.Ascending(field));

        public static string CreateUniqueIndex<TDocument>(this IMongoCollection<TDocument> collection, FieldDefinition<TDocument> field)
            => collection.CreateUniqueIndex(Builders<TDocument>.IndexKeys.Ascending(field));

        public static string CreateUniqueIndex<TDocument>(this IMongoCollection<TDocument> collection, IndexKeysDefinition<TDocument> keys)
            => collection.CreateIndex(keys, new CreateIndexOptions() { Unique = true });

        private static string CreateIndex<TDocument>(this IMongoCollection<TDocument> collection, IndexKeysDefinition<TDocument> keys, CreateIndexOptions options)
        {
            options.Background = true;
            var model = new CreateIndexModel<TDocument>(keys, options);
            return collection.Indexes.CreateOne(model);
        }
    }
}
