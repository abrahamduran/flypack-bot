using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Services;
using FlypackBot.Models;
using FlypackBot.Persistence;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot.Application.Commands
{
    public class StartCommand
    {
        private readonly TelegramSettings _settings;
        private readonly UserRepository _userRepository;
        private readonly FlypackService _flypack;
        private readonly ChatSessionService _session;
        private readonly PasswordEncrypterService _encrypter;

        public StartCommand(IOptions<TelegramSettings> settings, UserRepository userRepository, FlypackService flypackService, ChatSessionService session, PasswordEncrypterService encrypter)
        {
            _settings = settings.Value;
            _userRepository = userRepository;
            _flypack = flypackService;
            _session = session;
            _encrypter = encrypter;
        }

        public async Task Handle(TelegramBotClient client, Message message, CancellationToken cancellationToken)
        {
            await client.SendChatActionAsync(message.Chat, ChatAction.Typing);

            var userExists = await _userRepository.ExistsAsync(message.From.Id, cancellationToken);
            var session = _session.Get(message.Chat.Id);
            if (userExists || session != null)
            {
                await client.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "Me parece que ya hemos pasado por esto, quizás ya has enviado ese comando anteriormente. _Déjà vu_",
                    parseMode: ParseMode.Markdown,
                    replyToMessageId: message.MessageId,
                    cancellationToken: cancellationToken
                );
                return;
            }

            await client.SendTextMessageAsync(
                chatId: message.Chat,
                text: "¡Hola! Como tal vez ya sepas, este bot te ayudará con el seguimiento de tus paquetes de [Flypack](https://www.flypack.com.do).\n_Este bot no posee ninguna relación jurídica con la empresa Flypack o sus allegados._",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken
            );

            await Task.Delay(_settings.ConsecutiveMessagesInterval);

            var sent = await client.SendTextMessageAsync(
                chatId: message.Chat,
                text: "Por favor, mándame tu usuario y contraseña; así podré revisar tus paquetes y sus estados. No te preocupes, mantendre tus credenciales bien seguras.\n\nMándalos de esta forma: _usuario, contraseña_.",
                parseMode: ParseMode.Markdown,
                replyMarkup: new ForceReplyMarkup(),
                cancellationToken: cancellationToken
            );

            _session.Add(sent, message.From.Id, SessionScope.Login);
        }

        public async Task Login(TelegramBotClient client, Message message, CancellationToken cancellationToken)
        {
            _session.Add(message, message.From.Id, SessionScope.Login);
            var credentials = message.Text.Split(',', 2, StringSplitOptions.RemoveEmptyEntries);
            if (credentials.Length < 2)
            {
                var sent = await client.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "Por favor, mándame tu usuario y contraseña.\n\nMándalos de esta forma: *usuario, contraseña*, utilizando una coma (,) en medio de.",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: new ForceReplyMarkup(),
                    cancellationToken: cancellationToken
                );
                _session.Add(sent, message.From.Id, SessionScope.Login);
                return;
            }

            var validCredentials = await _flypack.TestCredentialsAsync(credentials[0], credentials[1]);
            if (validCredentials == false)
            {
                var sent = await client.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "*Usuario y contraseña incorrectos*\nPor favor, mándame tu usuario y contraseña una vez más.\n\nMándalos de esta forma: _usuario, contraseña_.",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: new ForceReplyMarkup(),
                    cancellationToken: cancellationToken
                );
                _session.Add(sent, message.From.Id, SessionScope.Login);
                return;
            }

            foreach (var msg in _session.Get(message.Chat.Id).Messages)
                await client.DeleteMessageAsync(message.Chat, msg.Identifier, cancellationToken);

            await _session.RemoveAsync(message.Chat.Id, cancellationToken);

            await client.SendChatActionAsync(message.Chat, ChatAction.Typing, cancellationToken);

            var loggedUser = await _userRepository.GetAsync(x => x.Username == credentials[0], cancellationToken);
            var authorizedUsers = loggedUser.AuthorizedUsers ?? new SecondaryUser[] { };
            if (loggedUser != null && authorizedUsers.Count(x => x.Identifier == message.From.Id) == 0)
            {
                await NotifyUserOfLoginAttempt(client, loggedUser, message.From, message.Chat.Id);

                var sent = await client.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "Hmm..., esto es extraño, pero, al parecer otra persona ya se ha logueado con esta cuenta. "
                    + "Te pido que me des unos minutos en lo que verifico esta situación.",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );

                _session.Add(sent, message.From.Id, SessionScope.Login);
                return;
            }

            var task1 = _userRepository.AddAsync(new LoggedUser(message, credentials[0], _encrypter.Encrypt(credentials[1])), cancellationToken);
            var task2 = client.SendTextMessageAsync(
                chatId: message.Chat,
                text: $"¡Hola {message.From.FirstName}! He podido iniciar sesión con tu usuario, ahora me mantendré monitoreando el estado de tus paquetes.",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
            var task3 = _session.RemoveAsync(message.Chat.Id);

            await Task.WhenAll(task1, task2, task3);
            // fetch initial packages "Aquí tienes una lista con tus paquetes pendientes de entrega"
        }

        public async Task AnswerLoginAttemptNotification(TelegramBotClient client, User user, Message message, string answer, SecondaryUser attemptingUser)
        {
            var tasks = new List<Task>(6);
            tasks.Add(
                client.EditMessageTextAsync(message.Chat.Id, message.MessageId, $"Listo, respuesta: *{answer}*", parseMode: ParseMode.Markdown)
            );
            if (answer == "denegar")
            {
                tasks.AddRange(new[]
                {
                    client.SendTextMessageAsync(message.Chat, "⚠️ Te recomiendo que cambies tu contraseña tan pronto te sea posible."),
                    client.SendTextMessageAsync(attemptingUser.ChatIdentifier, "Pues... tu intento de inicio de sesión no ha sido aprobado."),
                    _userRepository.UpdateUnauthorizedUsersAsync(user.Id, attemptingUser)
                });
            }
            else if (answer == "permitir")
            {
                tasks.AddRange(new[]
                {
                    client.SendTextMessageAsync(attemptingUser.ChatIdentifier, "Tu inicio de sesión ha sido aprobado."),
                    _userRepository.UpdateAuthorizedUsersAsync(user.Id, attemptingUser)
                });
            }

            tasks.AddRange(new[]
            {
                _session.RemoveAsync(attemptingUser.ChatIdentifier),
                _session.RemoveAsync(message.Chat.Id)
            });

            await Task.WhenAll(tasks);
            // fetch initial packages "Aquí tienes una lista con tus paquetes pendientes de entrega"
        }

        private async Task NotifyUserOfLoginAttempt(TelegramBotClient client, LoggedUser user, User attemptingUser, long attemptingCbatIdentifier)
        {
            _session.Add(new SecondaryUser { ChatIdentifier = attemptingCbatIdentifier, Identifier = attemptingUser.Id, FirstName = attemptingUser.FirstName }, user.ChatIdentifier, user.Identifier, SessionScope.LoginAttempt);
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("Permitir", "permitir"),
                    InlineKeyboardButton.WithCallbackData("Denegar", "denegar"),
                }
            });

            var sent = await client.SendTextMessageAsync(
                chatId: user.ChatIdentifier,
                text: $"Hey {user.FirstName}, el usuario @{attemptingUser.Username ?? $"[{attemptingUser.FirstName}](tg://user?id={attemptingUser.Id})"} está tratando de iniciar sesión con tu cuenta de Flypack, ¿estás de acuerdo con esto?",
                parseMode: ParseMode.Markdown,
                replyMarkup: inlineKeyboard
            );

            _session.Add(sent, user.Identifier, SessionScope.LoginAttempt);
        }
    }
}
