using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Telegram.Bot.Types;

namespace FlypackBot.Models
{
    public class LoggedUser
    {
        [BsonId]
        [BsonElement("_id")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string MongoId { get; set; }
        public long Identifier { get; set; }
        public long ChatIdentifier { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Salt { get; set; }
        public string FirstName { get; set; }

        public ICollection<SecondaryUser> AuthorizedUsers { get; set; }
        public ICollection<SecondaryUser> UnauthorizedUsers { get; set; }

        public LoggedUser(Message message, string username, SaltAndPassword saltAndPassword)
        {
            Identifier = message.From.Id;
            ChatIdentifier = message.Chat.Id;
            FirstName = message.From.FirstName;
            Username = username;
            Password = saltAndPassword.Password;
            Salt = saltAndPassword.Salt;
        }
    }

    public class SecondaryUser
    {
        public long Identifier { get; set; }
        public long ChatIdentifier { get; set; }
        public string FirstName { get; set; }
    }
}
