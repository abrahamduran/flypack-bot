using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Domain.Models;
using MongoDB.Driver;

namespace FlypackBot.Persistence
{
    public class ChatSessionRepository
    {
        private const string COLLECTION_NAME_SESSIONS = "chatSessions";

        private readonly IMongoCollection<ChatSession> _sessions;
        public ChatSessionRepository(MongoDbContext context)
        {
            _sessions = context.database.GetCollection<ChatSession>(COLLECTION_NAME_SESSIONS);

            #region Create Indices
            _sessions.CreateUniqueIndex(x => x.ChatIdentifier);
            _sessions.CreateIndex(x => x.UserIdentifier);
            _sessions.CreateIndex(x => x.LastUpdateAt);
            #endregion
        }

        public async Task<IEnumerable<ChatSession>> GetAsync(CancellationToken cancellationToken = default)
        {
            var result = await _sessions.FindAsync(x => true, null, cancellationToken);
            return result.ToEnumerable(cancellationToken);
        }

        public Task UpsertAsync(IEnumerable<ChatSession> sessions, CancellationToken cancellationToken = default)
        {
            var updates = sessions.Select(x =>
            {
                var filter = Builders<ChatSession>.Filter.Eq(filter => filter.ChatIdentifier, x.ChatIdentifier);
                var update = Builders<ChatSession>.Update
                    .Set(s => s.Scope, x.Scope)
                    .Set(s => s.UserIdentifier, x.UserIdentifier)
                    .Set(s => s.Messages, x.Messages)
                    .Set(s => s.AttemptingUser, x.AttemptingUser)
                    .Set(s => s.LastUpdateAt, x.LastUpdateAt);
                return new UpdateOneModel<ChatSession>(filter, update) { IsUpsert = true };
            }).ToList();

            return _sessions.BulkWriteAsync(updates, null, cancellationToken);
        }

        public Task DeleteAsync(long chatIdentifier, CancellationToken cancellationToken = default)
            => _sessions.FindOneAndDeleteAsync(x => x.ChatIdentifier == chatIdentifier, null, cancellationToken);
    }
}
