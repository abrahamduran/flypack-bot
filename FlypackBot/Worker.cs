using System;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Commands;
using FlypackBot.Application.Handlers;
using FlypackBot.Application.Helpers;
using FlypackBot.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types.Enums;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot
{
    public class Worker : BackgroundService
    {
        private readonly Timer _timer;
        private readonly ILogger<Worker> _logger;
        private readonly ChatSessionService _session;
        private readonly UserCacheService _userCache;
        private readonly FlypackService _flypack;
        private readonly TelegramSettings _settings;
        private readonly TelegramBotClient _telegram;
        private readonly PackageNotificationParser _parser;

        // Commands
        private readonly StartCommand _startCommand;
        private readonly StopCommand _stopCommand;
        private readonly PackagesCommand _packagesCommand;
        private readonly UpdatePasswordCommand _updatePasswordCommand;

        // TODO: migrate to scope services (eg: commands)
        private readonly IServiceProvider _serviceProvider;

        public Worker(
            FlypackService flypack, ChatSessionService session, UserCacheService userCache, StartCommand startCommand,
            StopCommand stopCommand, PackagesCommand packagesCommand, UpdatePasswordCommand updatePasswordCommand,
            PackageNotificationParser parser, IOptions<TelegramSettings> settings, ILogger<Worker> logger)
        {
            _logger = logger;
            _parser = parser;
            _settings = settings.Value;
            _flypack = flypack;
            _session = session;
            _userCache = userCache;
            _startCommand = startCommand;
            _stopCommand = stopCommand;
            _packagesCommand = packagesCommand;
            _updatePasswordCommand = updatePasswordCommand;
            _telegram = new TelegramBotClient(_settings.AccessToken);
            _timer = new Timer(async (state) => { await StoreChanges(); });
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await _session.LoadAsync(cancellationToken);

            await _telegram.SetMyCommandsAsync(L10nCommands.Spanish, null, "es", cancellationToken);
            await _telegram.SetMyCommandsAsync(L10nCommands.English, null, "en", cancellationToken);
            await _telegram.SetMyCommandsAsync(L10nCommands.French, null, "fr", cancellationToken);

            //Telegram
            var allowed = new[] { UpdateType.Message,/* UpdateType.ChannelPost,*/ UpdateType.InlineQuery, UpdateType.CallbackQuery };
            var receiverOptions = new ReceiverOptions() { AllowedUpdates = allowed };
            _telegram.StartReceiving(new TelegramUpdateHandler(_flypack, _session, _userCache, _settings, _startCommand, _stopCommand, _packagesCommand, _updatePasswordCommand, _parser, HandleExceptionAsync, _logger), receiverOptions, cancellationToken);

            // Flypack
            _flypack.StartReceiving(new FlypackUpdateHandler(_telegram, _settings, _parser, _userCache, HandleExceptionAsync, _logger), cancellationToken);

            // Store Changes Periodically
            _timer?.Change(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Gracefully stopped this mf");
            _timer?.Change(Timeout.Infinite, 0);
            await StoreChanges(cancellationToken);
            await base.StopAsync(cancellationToken);
        }

        private async Task StoreChanges(CancellationToken token = default)
        {
            try
            {
                await Task.WhenAll(_userCache.StoreAsync(token), _session.StoreAsync(token));
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex, token);
            }
        }

        private async Task HandleExceptionAsync(Exception exception, CancellationToken cancellationToken)
        {
            if (exception is OperationCanceledException || exception is TaskCanceledException)
                _logger.LogWarning("Task has been cancelled. Message: {Message}", exception.Message);
            else
            {
                _logger.LogError(exception, exception.Message);
                await _telegram.SendTextMessageAsync(
                    chatId: _settings.ChannelIdentifier,
                    text: $"🧨 Ha ocurrido un error 🧨\n{exception.Message}",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
            }
        }
    }
}
