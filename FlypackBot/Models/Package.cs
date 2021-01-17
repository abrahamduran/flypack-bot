using System;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlypackBot.Models
{
    public struct Package : IEquatable<Package>
    {
        [BsonId]
        [BsonElement("_id")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string MongoId { get; set; }
        public string Identifier { get; set; }
        public string Tracking { get; set; }
        public string Description { get; set; }
        public float Weight { get; set; }
        public PackageStatus Status { get; set; }

        public DateTime Delivered { get; set; }
        public DateTime LastUpdated { get; set; }

        public static bool operator ==(Package left, Package right)
            => left.Equals(right);
        public static bool operator !=(Package left, Package right)
            => !(left == right);

        public bool Equals([AllowNull] Package other) =>
            Delivered == other.Delivered &&
            Description == other.Description &&
            Identifier == other.Identifier &&
            LastUpdated == other.LastUpdated &&
            Status == other.Status &&
            Tracking == other.Tracking &&
            Weight == other.Weight;

        public override int GetHashCode() =>
            Tracking.GetHashCode() ^
            Description.GetHashCode() ^
            Delivered.GetHashCode() ^
            Weight.GetHashCode() ^
            Status.GetHashCode();

        public override bool Equals(object obj)
            => obj is PackageStatus status && Equals(status);
    }

    public struct PackageStatus
    {
        public string Description { get; set; }
        public string Percentage { get; set; }

        public static PackageStatus Delivered
            => new PackageStatus { Description = "Entregado", Percentage = "100%" };

        public override int GetHashCode() => Description.GetHashCode() ^ Percentage.GetHashCode();

        public static bool operator ==(PackageStatus left, PackageStatus right)
            => left.Description == right.Description && left.Percentage == right.Percentage;
        public static bool operator !=(PackageStatus left, PackageStatus right) => !(left == right);
        public override bool Equals(object obj)
            => obj is PackageStatus status &&
                   Description == status.Description && Percentage == status.Percentage;

        public override string ToString() => $"{Description}, {Percentage}";
    }
}
