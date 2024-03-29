﻿using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Telegram.Bot.Types;

namespace FlypackBot.Domain.Models
{
    public abstract class TelegramUser
    {
        public long Identifier { get; set; }
        public long ChatIdentifier { get; set; }
        public string FirstName { get; set; }
        public string LanguageCode { get; set; }
    }

    public class LoggedUser : TelegramUser
    {
        [BsonId]
        [BsonElement("_id")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string MongoId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Salt { get; set; }

        public ICollection<SecondaryUser> AuthorizedUsers { get; set; }
        public ICollection<SecondaryUser> UnauthorizedUsers { get; set; }

        public LoggedUser() { }
        public LoggedUser(Message message, string username, string password, string salt)
        {
            Identifier = message.From.Id;
            ChatIdentifier = message.Chat.Id;
            LanguageCode = message.From.LanguageCode;
            FirstName = message.From.FirstName;
            Username = username;
            Password = password;
            Salt = salt;
        }
    }

    public class SecondaryUser : TelegramUser { }
}
