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
        private int _retriesCount = 0;
        private readonly ILogger<FlypackService> _logger;
        private readonly FlypackScrapper _flypack;
        private readonly FlypackSettings _settings;
        private List<Package> _currentPackages = new List<Package>();
        private Dictionary<string, Package> _previousPackages = new Dictionary<string, Package>();

        private string _path;

        public FlypackService(ILogger<FlypackService> logger, FlypackScrapper flypack, IOptions<FlypackSettings> options)
        {
            _logger = logger;
            _flypack = flypack;
            _settings = options.Value;
        }

        public async Task SubscribeAsync(TelegramBotClient client, long channelIdentifier, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Login into Flypack with account: {account}", _settings.Username);
            _path = await _flypack.LoginAsync(_settings.Username, _settings.Password);

            if (string.IsNullOrEmpty(_path)) { LogFailedLogin(client, channelIdentifier); return; }

            while (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Fetch running at: {time}", DateTime.Now);
                var packages = await _flypack.GetPackagesAsync(_path);
                if (packages == null && _retriesCount < MAX_RETRIES)
                {
                    LogFailedListPackages(client, channelIdentifier, _path);
                    _path = await _flypack.LoginAsync(_settings.Username, _settings.Password);
                    _retriesCount++; continue;
                }
                else if (_retriesCount >= MAX_RETRIES)
                {
                    LogMaxLoginAttemptsReached(client, channelIdentifier, _path);
                    break;
                }
                else _retriesCount = 0;

                packages = FilterPackages(packages);

                if (packages.Any())
                {
                    var message = ParseMessageFor(packages);
                    await client.SendTextMessageAsync(
                      chatId: channelIdentifier,
                      text: message,
                      parseMode: ParseMode.Markdown
                    );
                }
                await Task.Delay(TimeSpan.FromMinutes(_settings.FetchInterval), cancellationToken);
            }

            _logger.LogInformation("Cancellation requested");
        }

        public void StopAsync() => _logger.LogInformation("Stopping FlypackService");

        public async Task<string> LoginAndRequestFreshPackagesListAsync()
        {
            var path = await _flypack.LoginAsync(_settings.Username, _settings.Password);
            var packages = await _flypack.GetPackagesAsync(path);
            return ParseMessageFor(packages);
        }

        // TODO: remove this command since it's only intended to be used for debugging purpose
        public async Task<string> RequestFreshPackagesListAsync()
        {
            var packages = await _flypack.GetPackagesAsync(_path);
            return ParseMessageFor(packages);
        }

        public string GetCurrentPackagesList() => ParseMessageFor(_currentPackages);
        public string GetPreviousPackagesList() => ParseMessageFor(_previousPackages.Values.ToList());

        // TODO: remove this command since it's only intended to be used for debugging purpose
        public string Reset()
        {
            _currentPackages = new List<Package>();
            _previousPackages = new Dictionary<string, Package>();
            _retriesCount = 0;
            _path = "";
            return "";
        }

        private async void LogFailedLogin(TelegramBotClient client, long channelIdentifier)
        {
            _logger.LogWarning("Packages path is empty for account: {Account}", _settings.Username);
            await client.SendTextMessageAsync(
              chatId: channelIdentifier,
              text: $"⚠️ Packages path is empty ⚠️",
              parseMode: ParseMode.Markdown
            );
        }

        private async void LogFailedListPackages(TelegramBotClient client, long channelIdentifier, string path)
        {
            _logger.LogWarning("Failed to retrieve packages with path: {Path}", path);
            await client.SendTextMessageAsync(
              chatId: channelIdentifier,
              text: $"⚠️ Failed to retrieve packages ⚠️",
              parseMode: ParseMode.Markdown
            );
        }

        private async void LogMaxLoginAttemptsReached(TelegramBotClient client, long channelIdentifier, string path)
        {
            _logger.LogWarning("Too many failed login attemps for path: {Path}", path);
            await client.SendTextMessageAsync(
              chatId: channelIdentifier,
              text: $"⚠️ Too many failed login attemps ⚠️\nCheck logs for more details.",
              parseMode: ParseMode.Markdown
            );
        }

        private List<Package> FilterPackages(IEnumerable<Package> packages)
        {
            var updatedPackages = packages.Except(_currentPackages).ToList();
            _previousPackages = _currentPackages.ToDictionary(x => x.Identifier);
            _currentPackages = packages.ToList();

            if (updatedPackages.Any())
            {
                _logger.LogInformation("Found {PackagesCount} new packages at: {Time}", updatedPackages.Count, DateTime.Now.AddHours(-4));
                _logger.LogInformation("New package's ID: {PackageIds}", string.Join(", ", updatedPackages.Select(x => x.Identifier).ToList()));
            }
            else
                _logger.LogInformation("No new packages were found");

            return updatedPackages;
        }

        private string ParseMessageFor(IEnumerable<Package> packages)
        {
            if (packages == null || !packages.Any())
                return "⚠️ Lista de paquetes vacía ⚠️";

            List<string> messages = new List<string>();
            messages.Add("*Estado de paquetes*\n");

            foreach (var package in packages)
            {
                var description = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(package.Description.ToLower());
                messages.Add($"*Id*: {package.Identifier}");
                messages.Add($"*Descripción*: {description}");
                messages.Add($"*Tracking*: {package.TrackingInformation}");
                messages.Add($"*Peso*: {package.Weight} libras");

                var previousStatus = _previousPackages.ContainsKey(package.Identifier)
                    ? _previousPackages[package.Identifier].Status
                    : package.Status;
                if (previousStatus != package.Status)
                    messages.Add($"*Estado*: {previousStatus.Description} → {package.Status.Description}, _{package.Status.Percentage}_");
                else
                    messages.Add($"*Estado*: {package.Status.Description}, _{package.Status.Percentage}_");
                messages.Add("");
            }

            messages.RemoveAt(messages.Count - 1);
            return string.Join('\n', messages);
        }
    }
}
