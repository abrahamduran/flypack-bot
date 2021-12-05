using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Services;
using FlypackBot.Persistence;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FlypackBot.Application.Commands
{
    public class UpdatePasswordCommand
    {
        private readonly FlypackService _flypack;
        private readonly UserCacheService _userCache;
        private readonly UserRepository _userRepository;
        private readonly PasswordEncrypterService _encrypter;

        public UpdatePasswordCommand(UserCacheService userCache, UserRepository userRepository, PasswordEncrypterService encrypter, FlypackService flypack)
        {
            _flypack = flypack;
            _userCache = userCache;
            _encrypter = encrypter;
            _userRepository = userRepository;
        }

        public async Task Handle(ITelegramBotClient client, Message message, CancellationToken cancellationToken)
        {
            var password = string.Join(' ', message.Text.Split(' ').Skip(1));
            if (string.IsNullOrEmpty(password))
            {
                await client.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "Tu nueva contraseña no puede ser un mensaje en blanco.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            var user = await _userRepository.GetByIdentifierAsync(message.From.Id, cancellationToken);
            var result = await _flypack.TestCredentialsAsync(user.Username, password);

            if (!result)
            {
                await client.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "La nueva contraseña parece ser incorrecta.",
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            var encrypted = _encrypter.Encrypt(password);
            user.Password = encrypted.Password;
            user.Salt = encrypted.Salt;
            if (user.Identifier == message.From.Id)
            {
                user.FirstName = message.From.FirstName;
                user.ChatIdentifier = message.Chat.Id;
            }
            else
            {
                var authorizedUser = user.AuthorizedUsers.Single(x => x.Identifier == message.From.Id);
                authorizedUser.FirstName = message.From.FirstName;
                authorizedUser.ChatIdentifier = message.Chat.Id;
            }

            _userCache.AddOrUpdate(user);
            await _userRepository.UpdateAsync(user, cancellationToken);
        }
    }
}
