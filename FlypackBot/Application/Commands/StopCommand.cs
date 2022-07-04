using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Services;
using FlypackBot.Domain.Models;
using FlypackBot.Persistence;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace FlypackBot.Application.Commands
{
    public class StopCommand
    {
        private readonly ChatSessionService _session;
        private readonly UserCacheService _userCache;
        private readonly UserRepository _userRepository;
        private readonly PackagesRepository _packagesRepository;

        public StopCommand(UserCacheService userCache, UserRepository userRepository, PackagesRepository packagesRepository, ChatSessionService session)
        {
            _userCache = userCache;
            _userRepository = userRepository;
            _packagesRepository = packagesRepository;
            _session = session;
        }

        public async Task Handle(ITelegramBotClient client, Message message, CancellationToken cancellationToken)
        {
            var exists = await _userRepository.ExistsAsync(message.From.Id, cancellationToken);
            if (!exists)
            {
                await client.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: L10n.strings.DontKnowYouMessage,
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            _session.Add(message, message.From.Id, SessionScope.Stop);
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData(L10n.strings.YesText, L10n.strings.YesKeyword),
                    InlineKeyboardButton.WithCallbackData(L10n.strings.NoText, L10n.strings.NoKeyword),
                }
            });

            var sent = await client.SendTextMessageAsync(
                chatId: message.Chat,
                text: L10n.strings.StopBotConfirmationMessage,
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken
            );

            _session.Add(sent, message.From.Id, SessionScope.Stop);
        }

        public async Task AnswerInlineKeyboard(ITelegramBotClient client, User from, Message message, string answer, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>(2);
            tasks.Add(
                client.EditMessageTextAsync(message.Chat.Id, message.MessageId, string.Format(L10n.strings.InlineQueryAnswerMessage, message.Text, answer), parseMode: ParseMode.Markdown, cancellationToken: cancellationToken)
            );
            tasks.Add(_session.RemoveAsync(message.Chat.Id, cancellationToken));

            if (answer != L10n.strings.YesKeyword) { await Task.WhenAll(tasks); return; }

            tasks.Add(
                client.SendTextMessageAsync(message.Chat.Id, L10n.strings.StoppedBotMessage, cancellationToken: cancellationToken)
            );

            var user = await _userRepository.GetByIdentifierAsync(from.Id, cancellationToken);
            if (user.Identifier != from.Id)
            {
                var authorizedUser = user.AuthorizedUsers.Single(x => x.Identifier == from.Id);
                user.AuthorizedUsers.Remove(authorizedUser);
                _userCache.AddOrUpdate(user);
                tasks.Add(_userRepository.RemoveAuthorizedUserAsync(user.Identifier, authorizedUser, cancellationToken));
            }
            else
            {
                _userCache.Remove(user.Identifier);
                tasks.Add(_userRepository.DeleteAsync(user.Identifier, cancellationToken));
                tasks.Add(_packagesRepository.DeleteByUsernameAsync(user.Username, cancellationToken));
                if (user.AuthorizedUsers?.Any() == true)
                    tasks.Add(
                        client.SendTextMessageAsync(message.Chat.Id, L10n.strings.StoppedBotFollowUpMessage, cancellationToken: cancellationToken)
                    );

                foreach (var authorizedUser in user.AuthorizedUsers)
                {
                    tasks.Add(
                        client.SendTextMessageAsync(
                            authorizedUser.ChatIdentifier,
                            string.Format(L10n.strings.StoppedBotAuthorizedUsersMessage, user.Username),
                            cancellationToken: cancellationToken
                        )
                    );
                }
            }

            await Task.WhenAll(tasks);
        }
    }
}
