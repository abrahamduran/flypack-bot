using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Helpers;
using FlypackBot.Application.Services;
using FlypackBot.Domain.Models;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot.Application.Commands
{
    public class PackagesCommand
    {
        private readonly FlypackService _flypack;
        private readonly TelegramSettings _settings;
        private readonly UserCacheService _userCache;
        private readonly PackageNotificationParser _parser;
        private readonly PasswordDecrypterService _decrypter;

        public PackagesCommand(FlypackService flypack, UserCacheService userCache, PasswordDecrypterService decrypter, PackageNotificationParser parser, IOptions<TelegramSettings> settings)
        {
            _flypack = flypack;
            _parser = parser;
            _decrypter = decrypter;
            _userCache = userCache;
            _settings = settings.Value;
        }

        public async Task Handle(ITelegramBotClient client, Message message, CancellationToken cancellationToken)
        {
            var user = await _userCache.GetUserAsync(message.From.Id, cancellationToken);
            if (user.User is null)
            {
                await client.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "Pero... yo ni si quiera te conozco. ಠ_ಠ",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }
            var packages = await _flypack.LoginAndFetchPackagesAsync(user.User.Username, _decrypter.Decrypt(user.User.Password, user.User.Salt));
            await SendPackagesToChat(client, packages, message.Chat.Id, cancellationToken);
        }

        private async Task SendPackagesToChat(ITelegramBotClient client, IEnumerable<Package> packages, long channel, CancellationToken cancellationToken)
        {
            var messages = _parser.SplitMessage(_parser.ParseMessageFor(packages));
            var lastMessage = messages.Last();

            foreach (var msg in messages)
            {
                await client.SendTextMessageAsync(
                    chatId: channel,
                    text: msg,
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );

                if (msg != lastMessage)
                {
                    await client.SendChatActionAsync(channel, ChatAction.Typing, cancellationToken);
                    await Task.Delay(_settings.ConsecutiveMessagesInterval, cancellationToken);
                }
            }
        }
    }
}
