using System.Collections.Generic;
using FlypackBot.Domain.Models;

namespace FlypackBot.Application.Models
{
    public struct PackageUpdate
    {
        public IEnumerable<Package> Updates { get; set; }
        public Dictionary<string, Package> Previous { get; set; }
        public IEnumerable<LanguageAndChannels> Channels { get; set; }
    }
}
