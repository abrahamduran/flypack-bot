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
            _session.Add(message, message.From.Id, SessionScope.Stop);
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("Sí", "si"),
                    InlineKeyboardButton.WithCallbackData("No", "no"),
                }
            });

            var sent = await client.SendTextMessageAsync(
                chatId: message.Chat,
                text: "¿Estás seguro que quieres detener el bot?",
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken
            );

            _session.Add(sent, message.From.Id, SessionScope.Stop);
        }

        public async Task AnswerInlineKeyboard(ITelegramBotClient client, Message message, string answer, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>(2);
            tasks.Add(
                client.EditMessageTextAsync(message.Chat.Id, message.MessageId, $"{message.Text}\nListo, respuesta: *{answer}*", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken)
            );
            tasks.Add(_session.RemoveAsync(message.Chat.Id, cancellationToken));

            if (answer != "si") { await Task.WhenAll(tasks); return; }

            tasks.Add(
                client.SendTextMessageAsync(message.Chat.Id, "A partir de este momento ya no recibirás más notificaciones sobre tus paquetes. De igual forma, la información relacionada a tu usuario ha sido eliminada.", cancellationToken: cancellationToken)
            );

            var user = await _userRepository.GetByIdentifierAsync(message.From.Id, cancellationToken);
            if (user.Identifier != message.From.Id)
            {
                var authorizedUser = user.AuthorizedUsers.Single(x => x.Identifier == user.Identifier);
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
                        client.SendTextMessageAsync(message.Chat.Id, "Así mismo, los usuarios que has autorizado previamente han sido removidos y dejarán de recibir notificaciones.", cancellationToken: cancellationToken)
                    );

                foreach (var authorizedUser in user.AuthorizedUsers)
                {
                    tasks.Add(
                        client.SendTextMessageAsync(
                            authorizedUser.ChatIdentifier,
                            $"A partir de este momento ya no recibirás más notificaciones sobre los paquetes asociados a la cuenta FLY-{user.Username}. "
                            + "El usuario que se ha logueado previamente ha detenido las funciones del bot.\n"
                            + "Si deseas, puedes iniciar sesión usando el comando /start.",
                            cancellationToken: cancellationToken
                        )
                    );
                }
            }

            await Task.WhenAll(tasks);
        }
    }
}
