using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Commands;
using FlypackBot.Application.Services;
using FlypackBot.Domain.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FlypackBot.Application.Handlers
{
    public class TelegramUpdateHandler : IUpdateHandler
    {
        private readonly ILogger _logger;
        private readonly ChatSessionService _session;
        private readonly StartCommand _startCommand;
        private readonly Func<Exception, CancellationToken, Task> _errorHandler;

        public TelegramUpdateHandler(ChatSessionService session, StartCommand startCommand, Func<Exception, CancellationToken, Task> errorHandler, ILogger logger)
        {
            _session = session;
            _startCommand = startCommand;
            _errorHandler = errorHandler;
            _logger = logger;
        }

        public Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
        {
            //var userId = update.Message?.From.Id ?? update.InlineQuery?.From.Id ?? update.CallbackQuery.From?.Id;
            //var isUnauthorizedUser = userId is null || !_settings.AuthorizedUsers.Contains(userId ?? -1);
            ////var channelId = update.ChannelPost?.Chat.Id;
            ////var isUnauthorizedChannel = channelId != _settings.ChannelIdentifier;
            ////if (isUnauthorizedUser && isUnauthorizedChannel)
            //if (isUnauthorizedUser)
            //{
            //    var username = update.Message?.From.Username ?? update.InlineQuery?.From.Username;
            //    //var channelName = update.ChannelPost?.Chat.Title;
            //    //_logger.LogWarning("Received message from an unauthorized user. User ID: {UserId}, username: {Username}, channel: {ChannelId}: {ChannelName}", userId, username, channelId, channelName);
            //    _logger.LogWarning("Received message from an unauthorized user. User ID: {UserId}, username: {Username}", userId, username);
            //    return Task.CompletedTask;
            //}

            var handler = update.Type switch
            {
                UpdateType.Message => OnBotMessage(client, update.Message, cancellationToken),
                //UpdateType.ChannelPost => OnBotMessage(client, update.ChannelPost),
                //UpdateType.InlineQuery => OnBotInlineQuery(client, update.InlineQuery),
                UpdateType.CallbackQuery => OnCallbackQueryReceived(client, update.CallbackQuery),
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
            _logger.LogDebug("Received a text message in {ChatId}, from: {UserId}", message.Chat.Id, message.From.Id);

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
                .First();

            return command switch
            {
                "/start" => _startCommand.Handle(client, message, cancellationToken),
                "/paquetes" => Task.CompletedTask,
                "/iniciarSesion" => Task.CompletedTask,
                "/cerrarSession" => Task.CompletedTask,
                _ => Task.CompletedTask
            };
        }

        private Task OnCallbackQueryReceived(ITelegramBotClient client, CallbackQuery callbackQuery)
        {
            var session = _session.Get(callbackQuery.Message.Chat.Id);
            if (session != null && session.Scope != SessionScope.LoginAttempt && session.AttemptingUser == null) return Task.CompletedTask;
            return _startCommand.AnswerLoginAttemptNotification(client, callbackQuery.From, callbackQuery.Message, callbackQuery.Data, session.AttemptingUser);
        }

        //private async Task OnBotInlineQuery(ITelegramBotClient client, InlineQuery message)
        //{
        //    _logger.LogDebug("Received inline query from: {SenderId}", message.From.Id);
        //    var text = message.Query.ToLower();
        //    var results = _service.GetPackages().Where(x => x.ContainsQuery(text)).Select(x =>
        //    {
        //        var message = ParseMessageFor(x, null, true);
        //        var content = new InputTextMessageContent(string.Join('\n', message))
        //        { ParseMode = ParseMode.Markdown };

        //        return new InlineQueryResultArticle(x.Identifier, x.Description, content)
        //        {
        //            Description = $"{x.Status}\n{x.Weight} libras"
        //        };
        //    })
        //    .DefaultIfEmpty(
        //        new InlineQueryResultArticle(
        //            "no-content-id",
        //            "Hello Darkness, My Old Friend",
        //            new InputTextMessageContent("I already told you there are no packages, why did you click me anyway?"))
        //        { Description = "You currently have no pending packages", ThumbUrl = "http://cdn.onlinewebfonts.com/svg/img_460888.png" }
        //    )
        //    .ToList();

        //    await _client.AnswerInlineQueryAsync(message.Id, results, 60, true);
        //}
    }
}
