using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Models;
using FlypackBot.Domain.Models;
using FlypackBot.Persistence;

namespace FlypackBot.Application.Services
{
    public class UserCacheService
    {
        private IDictionary<long, UserAndChannels> _users = new Dictionary<long, UserAndChannels>();
        private readonly UserRepository _repository;

        public UserCacheService(UserRepository userRepository)
            => _repository = userRepository;

        public async Task<UserAndChannels> GetUserAsync(long identifier, CancellationToken cancellationToken)
        {
            if (_users == null || !_users.Any())
                await FetchUsers(cancellationToken);

            var secondaryUser = _users.SingleOrDefault(x => x.Value.User.AuthorizedUsers?.Any(a => a.Identifier == identifier) ?? false).Value;

            return _users.ContainsKey(identifier)
                ? _users[identifier]
                : secondaryUser;
        }

        public async Task<IEnumerable<UserAndChannels>> GetUsersAsync(CancellationToken cancellationToken)
        {
            if (_users == null || !_users.Any())
                await FetchUsers(cancellationToken);

            return _users.Values;
        }

        public void AddOrUpdate(LoggedUser user)
            => _users[user.Identifier] = new UserAndChannels
            {
                User = user,
                Channels = (user.AuthorizedUsers?.Select(a => a.ChatIdentifier) ?? new List<long>(1))
                    .Append(user.ChatIdentifier)
            };

        public void Remove(long identifier) => _users.Remove(identifier);

        private async Task FetchUsers(CancellationToken cancellationToken)
        {
            var users = await _repository.GetListAsync(x => true, cancellationToken);
            if (users == null) return;

            _users = users.ToDictionary(
                x => x.Identifier,
                x => new UserAndChannels
                {
                    User = x,
                    Channels = (x.AuthorizedUsers?.Select(a => a.ChatIdentifier) ?? new List<long>(1))
                        .Append(x.ChatIdentifier)
                }
            );
        }
    }
}
