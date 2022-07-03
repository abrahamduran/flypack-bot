using System.Collections.Generic;
using FlypackBot.Domain.Models;

namespace FlypackBot.Application.Models
{
    public struct UserAndChannels
    {
        public LoggedUser User { get; set; }
        public IEnumerable<long> Channels { get; set; }
    }
}
