using System.Collections.Generic;
using FlypackBot.Domain.Models;

namespace FlypackBot.Application.Models
{
    public struct UserAndChannels
    {
        public LoggedUser User { get; set; }
        public IEnumerable<LanguageAndChannels> Channels { get; set; }
    }

    public struct LanguageAndChannels
    {
        public string LanguageCode { get; set; }
        public IEnumerable<long> Channels { get; set; }
    }
}
