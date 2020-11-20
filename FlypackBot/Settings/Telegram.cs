using System;
namespace FlypackBot.Settings
{
    public class Telegram
    {
        public string AccessToken { get; set; }
        public int[] AuthorizedUsers { get; set; }
        public long ChannelIdentifier { get; set; }
    }
}
