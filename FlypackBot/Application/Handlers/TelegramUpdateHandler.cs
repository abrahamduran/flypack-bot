using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Commands;
using FlypackBot.Application.Helpers;
using FlypackBot.Application.Services;
using FlypackBot.Domain.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot.Application.Handlers
{
    public class TelegramUpdateHandler : IUpdateHandler
    {
        private readonly ILogger _logger;
        private readonly FlypackService _flypack;
        private readonly TelegramSettings _settings;
        private readonly ChatSessionService _session;
        private readonly PackageNotificationParser _parser;
        private readonly Func<Exception, CancellationToken, Task> _errorHandler;

        // Commands
        private readonly StartCommand _startCommand;
        private readonly StopCommand _stopCommand;
        private readonly PackagesCommand _packagesCommand;
        private readonly UpdatePasswordCommand _updatePasswordCommand;

        public TelegramUpdateHandler(FlypackService flypack, ChatSessionService session, TelegramSettings settings, StartCommand startCommand, StopCommand stopCommand, PackagesCommand packagesCommand, UpdatePasswordCommand updatePasswordCommand, PackageNotificationParser parser, Func<Exception, CancellationToken, Task> errorHandler, ILogger logger)
        {
            _parser = parser;
            _session = session;
            _flypack = flypack;
            _settings = settings;
            _errorHandler = errorHandler;
            _logger = logger;

            _stopCommand = stopCommand;
            _startCommand = startCommand;
            _packagesCommand = packagesCommand;
            _updatePasswordCommand = updatePasswordCommand;
        }

        public Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
        {
            var userId = update.Message?.From.Id ?? update.InlineQuery?.From.Id ?? update.CallbackQuery.From?.Id;
            var channelId = update.ChannelPost?.Chat.Id;
            var isUnauthorizedChannel = channelId != _settings.ChannelIdentifier;
            if (update.ChannelPost != null && isUnauthorizedChannel)
            {
                var username = update.Message?.From.Username ?? update.InlineQuery?.From.Username;
                var channelName = update.ChannelPost?.Chat.Title;
                _logger.LogWarning("Received message from an unauthorized user. User ID: {UserId}, username: {Username}, channel: {ChannelId}: {ChannelName}", userId, username, channelId, channelName);
                return Task.CompletedTask;
            }

            var handler = update.Type switch
            {
                UpdateType.Message => OnBotMessage(client, update.Message, cancellationToken),
                //UpdateType.ChannelPost => OnBotMessage(client, update.ChannelPost),
                UpdateType.InlineQuery => OnBotInlineQuery(client, update.InlineQuery, cancellationToken),
                UpdateType.CallbackQuery => OnCallbackQueryReceived(client, update.CallbackQuery, cancellationToken),
                _ => Task.CompletedTask
            };

            Task.Run(async () =>
            {
                try
                {
                    await handler;
                }
                catch (Exception exception)
                {
                    await HandleErrorAsync(client, exception, cancellationToken);
                }
            }, cancellationToken);

            return Task.CompletedTask;
        }

        public Task HandleErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
            => _errorHandler(exception, cancellationToken);

        private async Task OnBotMessage(ITelegramBotClient client, Message message, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Received a text message in {ChatId}, from: {UserId}. Message: {Message}", message.Chat.Id, message.From.Id, message.Text);
            if (message.Text?.StartsWith('/') == true)
            {
                await client.SendChatActionAsync(message.Chat, ChatAction.Typing, cancellationToken);
                await AnswerBotCommandAsync(client, message, cancellationToken);
            }
            else if (_session.Get(message.Chat.Id) is ChatSession session)
            {
                await client.SendChatActionAsync(message.Chat, ChatAction.Typing, cancellationToken);
                switch (session.Scope)
                {
                    case SessionScope.Login:
                        await _startCommand.Login(client, message, cancellationToken);
                        break;
                    default: break;
                }
            }

            return;
        }

        private Task AnswerBotCommandAsync(ITelegramBotClient client, Message message, CancellationToken cancellationToken)
        {
            var command = message.Text
                .Replace('@', ' ')
                .Split(' ')
                .First()
                .Replace("/", "");

            return L10nCommands.Normalize(command) switch
            {
                "/start" => _startCommand.Handle(client, message, cancellationToken),
                "/packages" => _packagesCommand.Handle(client, message, cancellationToken),
                "/change_password" => _updatePasswordCommand.Handle(client, message, cancellationToken),
                "/stop" => _stopCommand.Handle(client, message, cancellationToken),
                "/psa" => Task.CompletedTask,
                _ => Task.Run(() =>
                {
                    _logger.LogWarning("Unrecognized command send by user {User}. Command: {Command}", message.From, command);
                    return Task.CompletedTask;
                })
            };
        }

        private Task OnCallbackQueryReceived(ITelegramBotClient client, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var session = _session.Get(callbackQuery.Message.Chat.Id);
            if (session == null) return Task.CompletedTask;

            if (session.Scope == SessionScope.LoginAttempt && session.AttemptingUser != null)
                return _startCommand.AnswerLoginAttemptNotification(client, callbackQuery.From, callbackQuery.Message, callbackQuery.Data, session.AttemptingUser, cancellationToken);

            if (session.Scope == SessionScope.Stop)
                return _stopCommand.AnswerInlineKeyboard(client, callbackQuery.From, callbackQuery.Message, callbackQuery.Data, cancellationToken);

            return Task.CompletedTask;
        }

        private async Task OnBotInlineQuery(ITelegramBotClient client, InlineQuery message, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Received inline query from: {SenderId}", message.From.Id);
            var text = message.Query.ToLower();
            var packages = await _flypack.GetCurrentPackagesAsync(message.From.Id, cancellationToken);

            if (packages == null)
            {
                await client.AnswerInlineQueryAsync(message.Id, new InlineQueryResultArticle[0], cancellationToken: cancellationToken);
                return;
            }

            var results = packages.Where(x => x.ContainsQuery(text)).Select(x =>
            {
                var message = _parser.ParseMessageFor(x);
                var content = new InputTextMessageContent(string.Join('\n', message))
                { ParseMode = ParseMode.Markdown };

                return new InlineQueryResultArticle(x.Identifier, x.Description, content)
                {
                    Description = $"{x.Status}\n{x.Weight} libras"
                };
            })
            .ToList();

            await client.AnswerInlineQueryAsync(message.Id, results, 60, true, cancellationToken: cancellationToken);
        }
    }

    internal static class PackageExtension
    {
        internal static bool ContainsQuery(this Package package, string query)
            => (package.Identifier + package.Description + package.Status + package.Tracking)
                .ToLower().Contains(query) || string.IsNullOrEmpty(query);
    }
}
