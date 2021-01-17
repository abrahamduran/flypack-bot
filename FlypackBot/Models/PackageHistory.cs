using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlypackBot.Models
{
    public class PackageHistory
    {
        [BsonId]
        [BsonElement("_id")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string MongoId { get; set; }
        public string Identifier { get; set; }
        public string Tracking { get; set; }
        public IEnumerable<PackageChange> Changes { get; set; }
    }

    public class PackageChange
    {
        public string Status { get; set; }
        public string Percentage { get; set; }
        public float Weight { get; set; }
        public DateTime Date { get; set; }
    }
}
