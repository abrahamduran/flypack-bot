using System;
using System.Collections.Generic;
using FlypackBot.Domain.Models;

namespace FlypackBot.Application.Models
{
    public struct PackageChanges
    {
        public IEnumerable<Package> Updates { get; set; }
        public IEnumerable<Package> Deletes { get; set; }
        public Dictionary<string, Package> Previous { get; set; }

        public static PackageChanges Empty => new PackageChanges
        {
            Updates = Array.Empty<Package>(),
            Deletes = Array.Empty<Package>(),
            Previous = new Dictionary<string, Package>()
        };
    }
}
