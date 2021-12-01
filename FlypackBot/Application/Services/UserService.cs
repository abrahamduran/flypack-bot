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
    public class UserService
    {
        private IDictionary<long, UserAndChannels> _users;
        private readonly UserRepository _repository;

        public UserService(UserRepository userRepository)
            => _repository = userRepository;

        public async Task<IEnumerable<UserAndChannels>> GetUsersAsync(CancellationToken cancellationToken)
        {
            if (_users == null || !_users.Any())
            {
                var users = await _repository.GetListAsync(x => true, cancellationToken);
                if (users == null) return null;

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
                

            return _users.Values;
        }

        public void AddOrUpdate(LoggedUser user)
            => _users[user.Identifier] = new UserAndChannels
            {
                User = user,
                Channels = (user.AuthorizedUsers?.Select(a => a.ChatIdentifier) ?? new List<long>(1))
                    .Append(user.ChatIdentifier)
            };
    }
}
