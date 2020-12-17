using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using FlypackSettings = FlypackBot.Settings.Flypack;

namespace FlypackBot
{
    public class FlypackService
    {
        private const int MAX_RETRIES = 3;
        private string _path;
        private int _retriesCount = 0;
        private readonly ILogger<FlypackService> _logger;
        private readonly FlypackScrapper _flypack;
        private readonly FlypackSettings _settings;
        private List<Package> _currentPackages = new List<Package>();
        private Dictionary<string, Package> _previousPackages = new Dictionary<string, Package>();
        
        public event EventHandler<PackagesEventArgs> OnUpdate;
        public event EventHandler OnFailedLogin;
        public event EventHandler OnFailedFetch;

        public FlypackService(ILogger<FlypackService> logger, FlypackScrapper flypack, IOptions<FlypackSettings> options)
        {
            _logger = logger;
            _flypack = flypack;
            _settings = options.Value;
        }

        public async Task SubscribeAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Login into Flypack with account: {account}", _settings.Username);
            _path = await _flypack.LoginAsync(_settings.Username, _settings.Password);

            if (string.IsNullOrEmpty(_path)) { LogFailedLogin(); return; }

            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Executing fetch");
                var packages = await _flypack.GetPackagesAsync(_path);
                if (packages == null && _retriesCount < MAX_RETRIES)
                {
                    LogFailedListPackages(_path);
                    _path = await _flypack.LoginAsync(_settings.Username, _settings.Password);
                    _retriesCount++; continue;
                }
                else if (_retriesCount >= MAX_RETRIES)
                {
                    LogMaxLoginAttemptsReached(_path);
                    break;
                }
                else _retriesCount = 0;

                packages = FilterPackages(packages);

                if (packages.Any())
                    OnUpdate?.Invoke(this, new PackagesEventArgs(packages, _previousPackages));

                await Task.Delay(TimeSpan.FromMinutes(_settings.FetchInterval), cancellationToken);
            }

            _logger.LogInformation("Cancellation requested");
        }

        public void StopAsync() => _logger.LogInformation("Stopping FlypackService");

        public async Task<IEnumerable<Package>> LoginAndFetchPackagesAsync()
        {
            var path = await _flypack.LoginAsync(_settings.Username, _settings.Password);
            var packages = await _flypack.GetPackagesAsync(path);
            return packages;
        }

        // TODO: remove this command since it's only intended to be used for debugging purpose
        public async Task<IEnumerable<Package>> FetchPackagesAsync()
        {
            var packages = await _flypack.GetPackagesAsync(_path);
            if (packages == null || !packages.Any())
                _logger.LogWarning("Failed to retrieve packages with path: {Path}", _path);

            return packages;
        }

        public IEnumerable<Package> GetPackages() => _currentPackages;
        public IEnumerable<Package> GetCurrentPackages() => _currentPackages;
        public IEnumerable<Package> GetPreviousPackages() => _previousPackages.Values.ToList();

        // TODO: remove this command since it's only intended to be used for debugging purpose
        public string Reset()
        {
            _currentPackages = new List<Package>();
            _previousPackages = new Dictionary<string, Package>();
            _retriesCount = 0;
            _path = "";
            return "Los datos han sido reiniciados ✅";
        }

        private List<Package> FilterPackages(IEnumerable<Package> packages)
        {
            var updatedPackages = packages.Except(_currentPackages).ToList();
            _previousPackages = _currentPackages.ToDictionary(x => x.Identifier);
            _currentPackages = packages.ToList();

            if (updatedPackages.Any())
            {
                _logger.LogInformation("Found {PackagesCount} new packages", updatedPackages.Count);
                _logger.LogInformation("New package's ID: {PackageIds}", string.Join(", ", updatedPackages.Select(x => x.Identifier).ToList()));
            }
            else
                _logger.LogInformation("No new packages were found");

            return updatedPackages;
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
    }
}
