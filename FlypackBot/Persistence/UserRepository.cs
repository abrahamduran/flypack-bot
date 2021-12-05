using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Domain.Models;
using MongoDB.Driver;

namespace FlypackBot.Persistence
{
    public class UserRepository
    {
        private const string COLLECTION_NAME_LOGGED_USERS = "loggedUsers";

        private readonly IMongoCollection<LoggedUser> _users;
        public UserRepository(MongoDbContext context)
        {
            _users = context.database.GetCollection<LoggedUser>(COLLECTION_NAME_LOGGED_USERS);

            #region Create Indices
            _users.CreateUniqueIndex(x => x.Identifier);
            _users.CreateUniqueIndex(x => x.Username);
            _users.CreateIndex("authorizedUsers.identifier");
            _users.CreateIndex("unauthorizedUsers.identifier");
            #endregion
        }

        public async Task<LoggedUser> GetAsync(Expression<Func<LoggedUser, bool>> filter, CancellationToken cancellationToken = default)
        {
            var result = await _users.FindAsync(filter, null, cancellationToken);
            return result.SingleOrDefault(cancellationToken);
        }

        public async Task<LoggedUser> GetByIdentifierAsync(long identifier, CancellationToken cancellationToken = default)
        {
            var filterUser = Builders<LoggedUser>.Filter.Eq(x => x.Identifier, identifier);
            var filterAuthorizedUser = Builders<LoggedUser>.Filter.Eq(x => x.AuthorizedUsers.ElementAt(-1).Identifier, identifier);
            var result = await _users.FindAsync(filterUser | filterAuthorizedUser, null, cancellationToken);
            return result.SingleOrDefault(cancellationToken);
        }

        public async Task<IEnumerable<LoggedUser>> GetListAsync(Expression<Func<LoggedUser, bool>> filter, CancellationToken cancellationToken = default)
        {
            var result = await _users.FindAsync(filter, null, cancellationToken);
            return result.ToEnumerable(cancellationToken);
        }

        public async Task<bool> ExistsAsync(long identifier, CancellationToken cancellationToken = default)
        {
            var count = await _users.CountDocumentsAsync(x => x.Identifier == identifier, null, cancellationToken)
                + await _users.CountDocumentsAsync(Builders<LoggedUser>.Filter.ElemMatch(x => x.AuthorizedUsers, x => x.Identifier == identifier));
            return count == 1;
        }

        public Task AddAsync(LoggedUser user, CancellationToken cancellationToken = default)
            => _users.InsertOneAsync(user, null, cancellationToken);

        public Task UpdateAsync(LoggedUser user, CancellationToken cancellationToken = default)
        {
            var filter = Builders<LoggedUser>.Filter.Eq(x => x.Identifier, user.Identifier);
            var update = Builders<LoggedUser>.Update
                .Set(x => x.ChatIdentifier, user.ChatIdentifier)
                .Set(x => x.FirstName, user.FirstName)
                .Set(x => x.Password, user.Password)
                .Set(x => x.Salt, user.Salt)
                .Set(x => x.AuthorizedUsers, user.AuthorizedUsers)
                .Set(x => x.UnauthorizedUsers, user.UnauthorizedUsers);

            return _users.UpdateOneAsync(filter, update, null, cancellationToken);
        }

        public Task UpdateAuthorizedUsersAsync(long identifier, SecondaryUser authorizedUser, CancellationToken cancellationToken = default)
        {
            var filter = Builders<LoggedUser>
                .Filter.Eq(x => x.Identifier, identifier);
            var removeUser = Builders<LoggedUser>.Update
                .PullFilter(x => x.AuthorizedUsers, x => x.Identifier == authorizedUser.Identifier)
                .PullFilter(x => x.UnauthorizedUsers, x => x.Identifier == authorizedUser.Identifier);
            var addUser = Builders<LoggedUser>.Update
                .AddToSet(x => x.AuthorizedUsers, authorizedUser);

            return _users.BulkWriteAsync(new[] { new UpdateOneModel<LoggedUser>(filter, removeUser), new UpdateOneModel<LoggedUser>(filter, addUser) }, null, cancellationToken);
        }

        public Task UpdateUnauthorizedUsersAsync(long identifier, SecondaryUser unauthorizedUser, CancellationToken cancellationToken = default)
        {
            var filter = Builders<LoggedUser>
                .Filter.Eq(x => x.Identifier, identifier);
            var removeUser = Builders<LoggedUser>.Update
                .PullFilter(x => x.AuthorizedUsers, x => x.Identifier == unauthorizedUser.Identifier)
                .PullFilter(x => x.UnauthorizedUsers, x => x.Identifier == unauthorizedUser.Identifier);
            var addUser = Builders<LoggedUser>.Update
                .AddToSet(x => x.UnauthorizedUsers, unauthorizedUser);

            return _users.BulkWriteAsync(new[] { new UpdateOneModel<LoggedUser>(filter, removeUser), new UpdateOneModel<LoggedUser>(filter, addUser) }, null, cancellationToken);
        }
    }
}
