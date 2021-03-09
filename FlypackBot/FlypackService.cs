using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Models;
using FlypackBot.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FlypackSettings = FlypackBot.Settings.Flypack;

namespace FlypackBot
{
    public class FlypackService
    {
        private const int MAX_RETRIES = 3;
        private int _retriesCount = 0;
        private string _path;
        private IEnumerable<Package> _currentPackages = new List<Package>();

        private readonly ILogger<FlypackService> _logger;
        private readonly FlypackScrapper _flypack;
        private readonly FlypackSettings _settings;
        private readonly PackagesRepository _repository;
        private readonly TimeSpan _fetchInterval;

        public event EventHandler<PackagesEventArgs> OnUpdate;
        public event EventHandler OnFailedLogin;
        public event EventHandler OnFailedFetch;

        public FlypackService(ILogger<FlypackService> logger, FlypackScrapper flypack, PackagesRepository repository, IOptions<FlypackSettings> options)
        {
            _logger = logger;
            _flypack = flypack;
            _repository = repository;
            _settings = options.Value;
            _fetchInterval = TimeSpan.FromMinutes(_settings.FetchInterval);
        }

        public async Task SubscribeAsync(CancellationToken cancellationToken)
        {
            _currentPackages = await _repository.GetPendingAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var packages = await FetchPackages();

                if (packages.Updates.Any())
                    OnUpdate?.Invoke(this, new PackagesEventArgs(packages.Updates, packages.Previous));

                await StoreChanges(packages, cancellationToken);

                await Task.Delay(_fetchInterval, cancellationToken);
            }

            _logger.LogInformation("Cancellation requested");
        }

        public void StopAsync()
        {
            _logger.LogInformation("Stopping FlypackService");
            OnFailedFetch = null;
            OnFailedLogin = null;
            OnUpdate = null;
        }

        public async Task<IEnumerable<Package>> LoginAndFetchPackagesAsync()
        {
            var path = await _flypack.LoginAsync(_settings.Username, _settings.Password);
            return await _flypack.GetPackagesAsync(path);
        }

        public IEnumerable<Package> GetPackages() => _currentPackages;

        private async Task<PackageChanges> FetchPackages()
        {
            _logger.LogDebug("Executing fetch");
            IEnumerable<Package> packages;
            try
            {
                if (string.IsNullOrEmpty(_path))
                {
                    _logger.LogInformation("Login into Flypack with account: {account}", _settings.Username);
                    _path = await _flypack.LoginAsync(_settings.Username, _settings.Password);
                }

                if (string.IsNullOrEmpty(_path)) { LogFailedLogin(); return PackageChanges.Empty; }

                packages = await _flypack.GetPackagesAsync(_path);
            }
            catch { return PackageChanges.Empty; }

            if (packages == null && _retriesCount < MAX_RETRIES)
            {
                LogFailedListPackages(_path);
                _path = await _flypack.LoginAsync(_settings.Username, _settings.Password);
                _retriesCount++;
                return await FetchPackages();
            }
            else if (_retriesCount >= MAX_RETRIES)
            {
                LogMaxLoginAttemptsReached(_path);
                return PackageChanges.Empty;
            }
            else _retriesCount = 0;

            return FilterPackages(packages);
        }

        private PackageChanges FilterPackages(IEnumerable<Package> packages)
        {
            var updatedPackages = packages.Except(_currentPackages).ToList();
            var previousPackages = _currentPackages.ToList();
            _currentPackages = packages.ToList();

            var ids = packages.Select(x => x.Identifier).ToList();
            var deletedPackages = previousPackages.Except(packages).ToList();
            deletedPackages.RemoveAll(x => ids.Contains(x.Identifier));

            if (updatedPackages.Any())
            {
                _logger.LogInformation("Found {PackagesCount} new packages", updatedPackages.Count);
                _logger.LogInformation("New package's ID: {PackageIds}", string.Join(", ", updatedPackages.Select(x => x.Identifier).ToList()));
            }
            else
                _logger.LogInformation("No new packages were found");

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

            if (packages.Any())
                await _repository.UpsertAsync(packages, cancellationToken);
        }

        private void LogFailedLogin()
        {
            _logger.LogWarning("Failed login for account: {Account}", _settings.Username);
            OnFailedLogin?.Invoke(this, EventArgs.Empty);
        }
        
        private void LogFailedListPackages(string path)
        {
            _logger.LogWarning("Failed to retrieve packages with path: {Path}", path);
            OnFailedFetch?.Invoke(this, EventArgs.Empty);
        }

        private void LogMaxLoginAttemptsReached(string path)
        {
            _logger.LogWarning("Too many failed login attemps for path: {Path}", path);
            OnFailedLogin?.Invoke(this, EventArgs.Empty);
        }

        private struct PackageChanges
        {
            public IEnumerable<Package> Updates { get; set; }
            public IEnumerable<Package> Deletes { get; set; }
            public Dictionary<string, Package> Previous { get; set; }

            public static PackageChanges Empty => new PackageChanges
            {
                Updates = new Package[0],
                Deletes = new Package[0],
                Previous = new Dictionary<string, Package>()
            };
        }
    }
}
