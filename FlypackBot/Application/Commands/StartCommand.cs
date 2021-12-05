﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Helpers;
using FlypackBot.Application.Services;
using FlypackBot.Domain.Models;
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
        private readonly FlypackService _flypack;
        private readonly TelegramSettings _settings;
        private readonly ChatSessionService _session;
        private readonly UserCacheService _userCache;
        private readonly UserRepository _userRepository;
        private readonly PackageNotificationParser _parser;
        private readonly PasswordEncrypterService _encrypter;

        public StartCommand(
            IOptions<TelegramSettings> settings, UserRepository userRepository,
            UserCacheService userCache, FlypackService flypackService, ChatSessionService session,
            PasswordEncrypterService encrypter, PackageNotificationParser parser)
        {
            _parser = parser;
            _settings = settings.Value;
            _userCache = userCache;
            _userRepository = userRepository;
            _flypack = flypackService;
            _session = session;
            _encrypter = encrypter;
        }

        public async Task Handle(ITelegramBotClient client, Message message, CancellationToken cancellationToken)
        {
            await client.SendChatActionAsync(message.Chat, ChatAction.Typing, cancellationToken);

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

            await Task.Delay(_settings.ConsecutiveMessagesInterval, cancellationToken);

            var sent = await client.SendTextMessageAsync(
                chatId: message.Chat,
                text: "Por favor, mándame tu usuario y contraseña; así podré revisar tus paquetes y sus estados. No te preocupes, mantendre tus credenciales bien seguras.\n\nMándalos de esta forma: _usuario, contraseña_.",
                parseMode: ParseMode.Markdown,
                replyMarkup: new ForceReplyMarkup() { InputFieldPlaceholder = "usuario, contraseña" },
                cancellationToken: cancellationToken
            );

            _session.Add(sent, message.From.Id, SessionScope.Login);
        }

        public async Task Login(ITelegramBotClient client, Message message, CancellationToken cancellationToken)
        {
            _session.Add(message, message.From.Id, SessionScope.Login);
            var credentials = message.Text.Split(',', 2, StringSplitOptions.RemoveEmptyEntries);
            if (credentials.Length < 2)
            {
                var sent = await client.SendTextMessageAsync(
                    chatId: message.Chat,
                    text: "Por favor, mándame tu usuario y contraseña.\n\nMándalos de esta forma: *usuario, contraseña*, utilizando una coma (,) en medio de.",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: new ForceReplyMarkup() { InputFieldPlaceholder = "usuario, contraseña" },
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
                    replyMarkup: new ForceReplyMarkup() { InputFieldPlaceholder = "usuario, contraseña" },
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
            var authorizedUsers = loggedUser?.AuthorizedUsers ?? new SecondaryUser[] { };
            if (loggedUser != null && authorizedUsers.Count(x => x.Identifier == message.From.Id) == 0)
            {
                await NotifyUserOfLoginAttempt(client, loggedUser, message.From, message.Chat.Id, cancellationToken);

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

            var encrypted = _encrypter.Encrypt(credentials[1]);
            var user = new LoggedUser(message, credentials[0], encrypted.Password, encrypted.Salt);
            _userCache.AddOrUpdate(user);

            var task1 = _userRepository.AddAsync(user, cancellationToken);
            var task2 = client.SendTextMessageAsync(
                chatId: message.Chat,
                text: $"¡Hola {message.From.FirstName}! He podido iniciar sesión con tu usuario, ahora me mantendré monitoreando el estado de tus paquetes.",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
            var task3 = _session.RemoveAsync(message.Chat.Id);
            
            await Task.WhenAll(task1, task2, task3);

            var packages = await _flypack.LoginAndFetchPackagesAsync(credentials[0], credentials[1]);
            await SendPackagesToChat(client, packages, message.From.Id, cancellationToken);
        }

        public async Task AnswerLoginAttemptNotification(ITelegramBotClient client, User user, Message message, string answer, SecondaryUser attemptingUser, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>(6);
            tasks.Add(
                client.EditMessageTextAsync(message.Chat.Id, message.MessageId, $"{message.Text}\nListo, respuesta: *{answer}*", parseMode: ParseMode.Markdown, cancellationToken: cancellationToken)
            );
            if (answer == "denegar")
            {
                tasks.AddRange(new[]
                {
                    client.SendTextMessageAsync(message.Chat, "⚠️ Te recomiendo que cambies tu contraseña tan pronto te sea posible.", cancellationToken: cancellationToken),
                    client.SendTextMessageAsync(attemptingUser.ChatIdentifier, "Pues... tu intento de inicio de sesión no ha sido aprobado.", cancellationToken: cancellationToken),
                    _userRepository.UpdateUnauthorizedUsersAsync(user.Id, attemptingUser, cancellationToken)
                });
            }
            else if (answer == "permitir")
            {
                tasks.AddRange(new[]
                {
                    client.SendTextMessageAsync(attemptingUser.ChatIdentifier, "Tu inicio de sesión ha sido aprobado.", cancellationToken: cancellationToken),
                    _userRepository.UpdateAuthorizedUsersAsync(user.Id, attemptingUser, cancellationToken)
                });
            }

            tasks.AddRange(new[]
            {
                _session.RemoveAsync(attemptingUser.ChatIdentifier, cancellationToken),
                _session.RemoveAsync(message.Chat.Id, cancellationToken)
            });

            await Task.WhenAll(tasks);

            if (answer != "permitir") return;

            var cachedUser = (await _userCache.GetUserAsync(user.Id, cancellationToken)).User;
            cachedUser.AuthorizedUsers = cachedUser.AuthorizedUsers ?? new List<SecondaryUser>(1);
            cachedUser.AuthorizedUsers.Add(attemptingUser);
            _userCache.AddOrUpdate(cachedUser);

            var packages = await _flypack.GetCurrentPackagesAsync(user.Id, cancellationToken);
            await SendPackagesToChat(client, packages, attemptingUser.ChatIdentifier, cancellationToken);
        }

        private async Task NotifyUserOfLoginAttempt(ITelegramBotClient client, LoggedUser user, User attemptingUser, long attemptingCbatIdentifier, CancellationToken cancellationToken)
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
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken
            );

            _session.Add(sent, user.Identifier, SessionScope.LoginAttempt);
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
