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
        private readonly ILogger<Worker> _logger;
        private readonly ChatSessionService _session;
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
            FlypackService flypack, ChatSessionService session, StartCommand startCommand,
            StopCommand stopCommand, PackagesCommand packagesCommand, UpdatePasswordCommand updatePasswordCommand,
            PackageNotificationParser parser, IOptions<TelegramSettings> settings, ILogger<Worker> logger)
        {
            _logger = logger;
            _parser = parser;
            _settings = settings.Value;
            _flypack = flypack;
            _session = session;
            _startCommand = startCommand;
            _stopCommand = stopCommand;
            _packagesCommand = packagesCommand;
            _updatePasswordCommand = updatePasswordCommand;
            _telegram = new TelegramBotClient(_settings.AccessToken);
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
            _telegram.StartReceiving(new TelegramUpdateHandler(_flypack, _session, _settings, _startCommand, _stopCommand, _packagesCommand, _updatePasswordCommand, _parser, HandleExceptionAsync, _logger), receiverOptions, cancellationToken);

            // Flypack
            _flypack.StartReceiving(new FlypackUpdateHandler(_telegram, _settings, _parser, HandleExceptionAsync, _logger), cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Gracefully stopped this mf");
            await _session.StoreAsync(cancellationToken);
            await base.StopAsync(cancellationToken);
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
