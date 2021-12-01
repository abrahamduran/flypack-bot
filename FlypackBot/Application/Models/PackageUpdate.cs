using System.Collections.Generic;
using FlypackBot.Domain.Models;

namespace FlypackBot.Application.Models
{
    public struct PackageUpdate
    {
        public IEnumerable<long> Channels { get; set; }
        public IEnumerable<Package> Updates { get; set; }
        public Dictionary<string, Package> Previous { get; set; }
    }
}
