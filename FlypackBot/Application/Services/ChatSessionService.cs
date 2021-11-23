using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Models;
using FlypackBot.Persistence;
using Telegram.Bot.Types;

namespace FlypackBot.Application.Services
{
    public class ChatSessionService
    {
        private readonly ChatSessionRepository _repository;
        private readonly IDictionary<long, ChatSession> _sessions;

        public ChatSessionService(ChatSessionRepository repository)
        {
            _repository = repository;
            _sessions = new Dictionary<long, ChatSession>();
        }
        
        public void Add(Message message, long userIdentifier, SessionScope scope)
        {
            ChatSession session;
            if (_sessions.ContainsKey(message.Chat.Id))
            {
                session = _sessions[message.Chat.Id];
                session.LastUpdateAt = DateTime.Now;
                session.Scope = scope;
                session.Messages.Add(new SessionMessage { Identifier = message.MessageId, Text = message.Text });
            }
            else
                session = new ChatSession(message, userIdentifier, scope);

            _sessions[session.ChatIdentifier] = session;
        }

        public void Add(SecondaryUser attemptingUser, long chatIdentifier, long userIdentifier, SessionScope scope)
        {
            ChatSession session;
            if (_sessions.ContainsKey(chatIdentifier))
            {
                session = _sessions[chatIdentifier];
                session.LastUpdateAt = DateTime.Now;
                session.Scope = scope;
                session.AttemptingUser = attemptingUser;
            }
            else
                session = new ChatSession(attemptingUser, chatIdentifier, userIdentifier, scope);

            _sessions[session.ChatIdentifier] = session;
        }

        public ChatSession Get(long chatIdentifier) => _sessions.ContainsKey(chatIdentifier) ? _sessions[chatIdentifier] : null;

        public IEnumerable<SessionMessage> GetMessages(long chatIdentifier) => _sessions.ContainsKey(chatIdentifier) ? _sessions[chatIdentifier].Messages : null;

        public Task RemoveAsync(long chatIdentifier, CancellationToken cancellationToken = default)
        {
            _sessions.Remove(chatIdentifier);
            return _repository.DeleteAsync(chatIdentifier, cancellationToken);
        }

        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            var sessions = await _repository.GetAsync(cancellationToken);
            foreach (var session in sessions)
                _sessions[session.ChatIdentifier] = session;
        }

        public Task StoreAsync(CancellationToken cancellationToken = default) => _repository.UpsertAsync(_sessions.Values, cancellationToken);
    }
}
