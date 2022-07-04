using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Helpers;
using FlypackBot.Application.Models;
using FlypackBot.Application.Services;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot.Application.Handlers
{
    public class FlypackUpdateHandler
    {
        private readonly ILogger _logger;
        private readonly TelegramSettings _settings;
        private readonly ITelegramBotClient _telegram;
        private readonly PackageNotificationParser _parser;
        private readonly UserLanguageProvider _languageProvider;
        private readonly Func<Exception, CancellationToken, Task> _errorHandler;

        public FlypackUpdateHandler(ITelegramBotClient telegram, TelegramSettings settings, PackageNotificationParser parser, UserLanguageProvider languageProvider, Func<Exception, CancellationToken, Task> errorHandler, ILogger logger)
        {
            _parser = parser;
            _telegram = telegram;
            _settings = settings;
            _languageProvider = languageProvider;
            _errorHandler = errorHandler;
            _logger = logger;
        }

        public Task HandleUpdateAsync(PackageUpdate update, CancellationToken cancellationToken)
        {
            // TODO: Each call to ParseMessageFor needs to be localized for each user
            var tasks = new List<Task>(update.Channels.Count());

            foreach (var channel in update.Channels)
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(channel.LanguageCode);
                var message = _parser.ParseMessageFor(update.Updates, update.Previous, true);
                tasks.Add(SendMessageToChats(message, channel.Channels, cancellationToken));
            }

            return Task.WhenAll(tasks);
        }

        public Task HandleErrorAsync(Exception exception, CancellationToken cancellationToken)
            => _errorHandler(exception, cancellationToken);

        private async Task SendMessageToChats(string message, IEnumerable<long> channels, CancellationToken cancellationToken)
        {
            var messages = _parser.SplitMessage(message);
            var lastMessage = messages.Last();

            var tasks = new List<Task>(channels.Count());

            foreach (var channel in channels)
                tasks.Add(SendMessagesToChat(messages, lastMessage, channel, cancellationToken));

            await Task.WhenAll(tasks);
        }

        private async Task SendMessagesToChat(IEnumerable<string> messages, string lastMessage, long channel, CancellationToken cancellationToken)
        {
            foreach (var msg in messages)
            {
                await _telegram.SendTextMessageAsync(
                    chatId: channel,
                    text: msg,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );

                if (msg != lastMessage)
                {
                    await _telegram.SendChatActionAsync(channel, ChatAction.Typing, cancellationToken);
                    await Task.Delay(_settings.ConsecutiveMessagesInterval, cancellationToken);
                }
            }
        }
    }
}
