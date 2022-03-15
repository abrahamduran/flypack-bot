using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Telegram.Bot.Types;

namespace FlypackBot.Domain.Models
{
    public class ChatSession
    {
        [BsonId]
        [BsonElement("_id")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string MongoId { get; set; }
        public long ChatIdentifier { get; set; }
        public long UserIdentifier { get; set; }
        public SecondaryUser AttemptingUser { get; set; }

        public ICollection<SessionMessage> Messages { get; set; }
        public SessionScope Scope { get; set; }
        public DateTime LastUpdateAt { get; set; }

        public ChatSession(Message message, long userIdentifier, SessionScope scope)
        {
            ChatIdentifier = message.Chat.Id;
            UserIdentifier = userIdentifier;
            Messages = new List<SessionMessage> { new SessionMessage { Identifier = message.MessageId, Text = message.Text } };
            Scope = scope;
            LastUpdateAt = DateTime.Now;
        }

        public ChatSession(SecondaryUser attemptingUser, long chatIdentifier, long userIdentifier, SessionScope scope)
        {
            ChatIdentifier = chatIdentifier;
            UserIdentifier = userIdentifier;
            AttemptingUser = attemptingUser;
            Messages = new List<SessionMessage>();
            Scope = scope;
            LastUpdateAt = DateTime.Now;
        }
    }

    public class SessionMessage
    {
        public int Identifier { get; set; }
        public string Text { get; set; }
    }

    public enum SessionScope
    {
        Login, LoginAttempt, Stop
    }
}
