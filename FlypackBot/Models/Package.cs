using System;

namespace FlypackBot.Models
{
    public struct Package
    {
        public string Identifier { get; set; }
        public string TrackingInformation { get; set; }
        public string Description { get; set; }
        public float Weight { get; set; }
        public PackageStatus Status { get; set; }

        public DateTime Delivered { get; set; }
        public DateTime LastUpdated { get; set; }

        public override int GetHashCode() =>
            TrackingInformation.GetHashCode() ^
            Description.GetHashCode() ^
            Delivered.GetHashCode() ^
            Weight.GetHashCode() ^
            Status.GetHashCode();
    }

    public struct PackageStatus
    {
        public string Description { get; set; }
        public string Percentage { get; set; }

        public override int GetHashCode() => Description.GetHashCode() ^ Percentage.GetHashCode();

        public static bool operator ==(PackageStatus left, PackageStatus right)
            => left.Description == right.Description && left.Percentage == right.Percentage;
        public static bool operator !=(PackageStatus left, PackageStatus right) => !(left == right);
        public override bool Equals(object obj)
            => obj is PackageStatus status &&
                   Description == status.Description && Percentage == status.Percentage;
    }
}
