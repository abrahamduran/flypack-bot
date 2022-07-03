using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Models;
using FlypackBot.Domain.Models;
using FlypackBot.Persistence;

namespace FlypackBot.Application.Services
{
    public interface UserLanguageUpdater
    {
        Task UpdateIfNeededAsync(long identifier, string languageCode, CancellationToken cancellationToken);
    }

    public interface UserLanguageProvider
    {
        Task<string> GetUserLanguageCodeAsync(long identifier, CancellationToken cancellationToken);
    }

    public class UserCacheService : UserLanguageProvider, UserLanguageUpdater
    {
        private IDictionary<long, LoggedUser> _loggedUsers = new Dictionary<long, LoggedUser>();
        private IDictionary<long, SecondaryUser> _secondaryUsers = new Dictionary<long, SecondaryUser>();
        private readonly UserRepository _repository;

        public UserCacheService(UserRepository userRepository)
            => _repository = userRepository;

        public async Task<TelegramUser> GetUserAsync(long identifier, CancellationToken cancellationToken)
        {
            if (_loggedUsers == null || !_loggedUsers.Any())
                await FetchUsers(cancellationToken);

            if (_loggedUsers.ContainsKey(identifier))
                return _loggedUsers[identifier];

            if (_secondaryUsers.ContainsKey(identifier))
                return _secondaryUsers[identifier];

            return null;
        }

        public async Task<LoggedUser> GetLoggedUserAsync(long identifier, CancellationToken cancellationToken)
        {
            var user = await GetUserAsync(identifier, cancellationToken);

            if (user is SecondaryUser secondary)
                return await GetLoggedUserAsync(secondary, cancellationToken);

            return user as LoggedUser;
        }

        public async Task<LoggedUser> GetLoggedUserAsync(SecondaryUser user, CancellationToken cancellationToken)
        {
            if (_loggedUsers == null || !_loggedUsers.Any())
                await FetchUsers(cancellationToken);

            return _loggedUsers
                .SingleOrDefault(x => x.Value.AuthorizedUsers.Any(a => a.Identifier == user.Identifier))
                .Value;
        }

        public async Task<IEnumerable<UserAndChannels>> GetLoggedUsersAsync(CancellationToken cancellationToken)
        {
            if (_loggedUsers == null || !_loggedUsers.Any())
                await FetchUsers(cancellationToken);

            return _loggedUsers.Values
                .Select(x =>
                    new UserAndChannels
                    {
                        User = x,
                        Channels = (x.AuthorizedUsers?.Select(a => a.ChatIdentifier) ?? new List<long>(1))
                            .Append(x.ChatIdentifier)
                            .ToList()
                    }
                );
        }

        public async Task<string> GetUserLanguageCodeAsync(long identifier, CancellationToken cancellationToken) =>
            (await GetUserAsync(identifier, cancellationToken))?.LanguageCode ?? "en";

        public async Task UpdateIfNeededAsync(long identifier, string languageCode, CancellationToken cancellationToken)
        {
            var cached = await GetUserAsync(identifier, cancellationToken);

            if (cached.LanguageCode != languageCode)
            {
                cached.LanguageCode = languageCode;
                AddOrUpdate(cached);
            }
        }

        public void AddOrUpdate(TelegramUser user)
        {
            if (user is LoggedUser logged)
                _loggedUsers[user.Identifier] = logged;

            if (user is SecondaryUser secondary)
            {
                _secondaryUsers[user.Identifier] = secondary;
                var loggedUser = _loggedUsers
                    .SingleOrDefault(x => x.Value.AuthorizedUsers.Any(a => a.Identifier == user.Identifier))
                    .Value;
                var secondaryUser = loggedUser.AuthorizedUsers.SingleOrDefault(x => x.Identifier == user.Identifier);
                secondaryUser.FirstName = secondary.FirstName;
                secondaryUser.LanguageCode = secondary.LanguageCode;
            }
        }

        public void Remove(long identifier)
        {
            _loggedUsers.Remove(identifier);
            _secondaryUsers.Remove(identifier);
        }

        public Task StoreAsync(CancellationToken cancellationToken = default) =>
            _repository.UpdateAsync(_loggedUsers.Values, cancellationToken);

        private async Task FetchUsers(CancellationToken cancellationToken)
        {
            var users = await _repository.GetListAsync(x => true, cancellationToken);
            if (users == null) return;

            _loggedUsers = users
                .ToDictionary(x => x.Identifier, x => x);

            _secondaryUsers = users
                .SelectMany(x => x.AuthorizedUsers)
                .ToDictionary(x => x.Identifier, x => x);
        }
    }
}
