﻿using System;
using FlypackBot.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace FlypackBot.Persistence
{
    public class MongoDbContext
    {
        private readonly MongoClient _client;
        internal readonly IMongoDatabase database;

        public MongoDbContext(IOptions<MongoDb> settings) : this(settings.Value) { }

        public MongoDbContext(MongoDb settings)
        {
            var mongoUrl = new MongoUrl(settings.ConnectionString);
            var mongoSettings = MongoClientSettings.FromUrl(mongoUrl);
#if DEBUG
            Console.WriteLine("DEBUG");
            mongoSettings.ClusterConfigurator = cb => {
                cb.Subscribe<CommandStartedEvent>(e => {
                    Console.WriteLine($"{e.CommandName} - {e.Command.ToJson()}");
                });
            };
#endif
            _client = new MongoClient(mongoSettings);
            database = _client.GetDatabase(settings.DatabaseName);
        }
    }
}
