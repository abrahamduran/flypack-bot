using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Infraestructure;
using FlypackBot.Domain.Models;
using FlypackBot.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FlypackSettings = FlypackBot.Settings.Flypack;
using FlypackBot.Application.Models;
using FlypackBot.Application.Handlers;

namespace FlypackBot.Application.Services
{
    public class FlypackService
    {
        private const int MAX_RETRIES = 3;
        private IDictionary<string, IEnumerable<Package>> _currentPackages;
        private IDictionary<string, IEnumerable<long>> _channels = new Dictionary<string, IEnumerable<long>>();
        private IDictionary<string, string> _paths = new Dictionary<string, string>();

        private readonly ILogger<FlypackService> _logger;
        private readonly FlypackScrapper _flypack;
        private readonly FlypackSettings _settings;
        private readonly PackagesRepository _repository;
        private readonly UserCacheService _userService;
        private readonly PasswordDecrypterService _decrypterService;
        private readonly TimeSpan _fetchInterval;

        public FlypackService(FlypackScrapper flypack, PackagesRepository repository, UserCacheService userService, PasswordDecrypterService decrypterService, IOptions<FlypackSettings> options, ILogger<FlypackService> logger)
        {
            _logger = logger;
            _flypack = flypack;
            _repository = repository;
            _userService = userService;
            _decrypterService = decrypterService;
            _settings = options.Value;
            _fetchInterval = TimeSpan.FromMinutes(_settings.FetchInterval);
        }

        public void StartReceiving(FlypackUpdateHandler handler, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting FlypackService");
            IEnumerable<int> foo = new List<int>();

            Task.Run(async () =>
            {
                _currentPackages = (await _repository.GetPendingAsync(cancellationToken))
                        .GroupBy(x => x.Username)
                        .ToDictionary(x => x.Key, x => x.AsEnumerable());

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var users = await _userService.GetUsersAsync(cancellationToken);
                        if (users == null) continue;

                        await UpdateUsersPathsAndChannels(users, cancellationToken);
                        await FetchUsersPackages(handler.HandleUpdateAsync, cancellationToken);
                        await Task.Delay(_fetchInterval, cancellationToken);
                    }
                }
                catch (Exception exception)
                {
                    await handler.HandleErrorAsync(exception, cancellationToken);
                }
            }, cancellationToken);
        }

        public async Task<bool> TestCredentialsAsync(string username, string password)
            => (await _flypack.LoginAsync(username, password)) != null;

        public async Task<IEnumerable<Package>> LoginAndFetchPackagesAsync(string username, string password)
        {
            try
            {
                var path = await _flypack.LoginAsync(username, password);
                var packages = await _flypack.GetPackagesAsync(path, username);

                if (_paths != null) _paths[username] = path;
                // TODO: if there are differences, might be a good idea to update the cache and notify the other users about the changes.
                //if (_currentPackages != null) _currentPackages[username] = packages;

                return packages;
            }
            catch (Exception ex)
            {
                LogFailedLogin(ex, new LoggedUser { Username = username });
            }

            return new Package[0];
        }

        public async Task<IEnumerable<Package>> GetCurrentPackagesAsync(long identifier, CancellationToken cancellationToken)
        {
            var user = await _userService.GetUserAsync(identifier, cancellationToken);
            if (user.User == null) return null;
            var username = user.User.Username;

            return _currentPackages.ContainsKey(username) ? _currentPackages[username] : new Package[0];
        }

        private async Task UpdateUsersPathsAndChannels(IEnumerable<UserAndChannels> users, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            foreach (var item in users)
            {
                _channels[item.User.Username] = item.Channels;

                if (_paths.ContainsKey(item.User.Username)) continue;

                var task = Task.Run(async () =>
                {
                    var username = item.User.Username;
                    var password = item.User.Password;
                    var salt = item.User.Salt;
                    try
                    {
                        var path = await _flypack.LoginAsync(username, _decrypterService.Decrypt(password, salt));
                        if (string.IsNullOrEmpty(path))
                            LogFailedLogin(null, item.User);
                        else
                            _paths[username] = path;
                    }
                    catch (Exception ex)
                    {
                        LogFailedLogin(ex, item.User);
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            var deleted = _paths.Keys.Except(users.Select(x => x.User.Username).ToList());
            foreach (var key in deleted)
            {
                _paths.Remove(key);
                _channels.Remove(key);
                _currentPackages.Remove(key);
            }

            await Task.WhenAll(tasks);
        }

        private Task FetchUsersPackages(
            Func<PackageUpdate, CancellationToken, Task> updateHandler,
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            foreach (var path in _paths)
            {
                tasks.Add(
                    Task.Run(async () =>
                    {
                        var packages = await FetchUserPackages(path.Value, path.Key, cancellationToken);
                        var t1 = StoreChanges(packages, cancellationToken);
                        var t2 = Task.CompletedTask;
                        if (packages.Updates.Any())
                            t2 = updateHandler(new PackageUpdate
                                {
                                    Updates = packages.Updates,
                                    Previous = packages.Previous,
                                    Channels = _channels[path.Key]
                                },
                                cancellationToken
                            );
                        await Task.WhenAll(t1, t2);
                    }, cancellationToken)
                );
            }

            return Task.WhenAll(tasks);
        }

        private async Task<PackageChanges> FetchUserPackages(string path, string username, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Executing fetch");
            IEnumerable<Package> packages;
            try
            {
                // TODO: should we try to login again after here?
                if (string.IsNullOrEmpty(path))
                {
                    _paths.Remove(username);
                    return PackageChanges.Empty;
                }

                packages = await _flypack.GetPackagesAsync(path, username);
            }
            catch(Exception ex)
            {
                _paths.Remove(username);
                LogFailedPackagesFetch(ex, path);
                return PackageChanges.Empty;
            }

            // TODO: implement some logic to avoid retrying a failed request/user indefinitely
            //if (packages == null && _retriesCount < MAX_RETRIES)
            //{
            //    LogFailedPackagesFetch(null, path);
            //    _path = await _flypack.LoginAsync("", "");// _settings.Username, _settings.Password);
            //    _retriesCount++;
            //    return await FetchPackages();
            //}
            //else if (_retriesCount >= MAX_RETRIES)
            //{
            //    LogMaxLoginAttemptsReached(_path);
            //    return PackageChanges.Empty;
            //}
            //else _retriesCount = 0;

            var current = _currentPackages.ContainsKey(username) ? _currentPackages[username] : new Package[0];
            var filteredPackages = FilterPackages(packages, current, username);
            _currentPackages[username] = packages;
            return filteredPackages;
        }

        private PackageChanges FilterPackages(IEnumerable<Package> packages, IEnumerable<Package> currentPackages, string username)
        {
            if (packages is null) return PackageChanges.Empty;

            var updatedPackages = packages.Except(currentPackages).ToList();
            var previousPackages = currentPackages.ToList();

            var ids = packages.Select(x => x.Identifier).ToList();
            var deletedPackages = previousPackages.Except(packages).ToList();
            deletedPackages.RemoveAll(x => ids.Contains(x.Identifier));

            if (updatedPackages.Any())
                _logger.LogInformation("Found {PackagesCount} new packages for user {Username}", updatedPackages.Count, username);

            return new PackageChanges
            {
                Updates = updatedPackages,
                Deletes = deletedPackages,
                Previous = previousPackages.ToDictionary(x => x.Identifier)
            };
        }

        private async Task StoreChanges(PackageChanges changes, CancellationToken cancellationToken)
        {
            var packages = new List<Package>(changes.Updates.Count() + changes.Deletes.Count());
            packages.AddRange(changes.Updates);

            foreach (var item in changes.Deletes)
            {
                var delete = item;
                delete.Status = PackageStatus.Delivered;
                packages.Add(delete);
            }

            var tasks = new List<Task>(packages.Count);
            if (packages.Any())
                tasks.Add(_repository.UpsertAsync(packages, cancellationToken));

            await Task.WhenAll(tasks);
        }

        //private async Task<PackageChanges> FetchPackages()
        //{
        //    _logger.LogDebug("Executing fetch");
        //    IEnumerable<Package> packages;
        //    try
        //    {
        //        if (string.IsNullOrEmpty(_path))
        //        {
        //            _logger.LogInformation("Login into Flypack with account: {account}", "");// _settings.Username);
        //            _path = await _flypack.LoginAsync("", "");// _settings.Username, _settings.Password);
        //        }

        //        if (string.IsNullOrEmpty(_path)) { LogFailedLogin(); return PackageChanges.Empty; }

        //        packages = await _flypack.GetPackagesAsync(_path);
        //    }
        //    catch { return PackageChanges.Empty; }

        //    if (packages == null && _retriesCount < MAX_RETRIES)
        //    {
        //        LogFailedListPackages(_path);
        //        _path = await _flypack.LoginAsync("", "");// _settings.Username, _settings.Password);
        //        _retriesCount++;
        //        return await FetchPackages();
        //    }
        //    else if (_retriesCount >= MAX_RETRIES)
        //    {
        //        LogMaxLoginAttemptsReached(_path);
        //        return PackageChanges.Empty;
        //    }
        //    else _retriesCount = 0;

        //    return FilterPackages(packages);
        //}

        private void LogFailedLogin(Exception exception, LoggedUser user)
            => _logger.LogWarning(exception, "Failed login for account: {Account}", user.Username);
        
        private void LogFailedPackagesFetch(Exception exception, string path)
            => _logger.LogWarning(exception, "Failed to retrieve packages with path: {Path}", path);

        private void LogMaxLoginAttemptsReached(string path)
            => _logger.LogWarning("Too many failed login attemps for path: {Path}", path);
    }
}
