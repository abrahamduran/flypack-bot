using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Application.Models;
using FlypackBot.Domain.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot.Application.Handlers
{
    public class FlypackUpdateHandler
    {
        private const int SIMPLE_PACKAGES_AMOUNT = 3;
        private const string SEPARATOR = "_break-line_";

        private readonly ILogger _logger;
        private readonly TelegramSettings _settings;
        private readonly ITelegramBotClient _telegram;
        private readonly Func<Exception, CancellationToken, Task> _errorHandler;

        public FlypackUpdateHandler(ITelegramBotClient telegram, TelegramSettings settings, Func<Exception, CancellationToken, Task> errorHandler, ILogger logger)
        {
            _telegram = telegram;
            _settings = settings;
            _errorHandler = errorHandler;
            _logger = logger;
        }

        public Task HandleUpdateAsync(PackageUpdate update, CancellationToken cancellationToken)
        {
            var message = ParseMessageFor(update.Updates, update.Previous, true);
            return SendMessageToChats(message, update.Channels, cancellationToken);
        }

        public Task HandleErrorAsync(Exception exception, CancellationToken cancellationToken)
            => _errorHandler(exception, cancellationToken);

        private string ParseMessageFor(IEnumerable<Package> packages, Dictionary<string, Package> previousPackages, bool isUpdate)
        {
            if (packages == null || !packages.Any())
                return "Lista de paquetes vacía 📭";

            var messages = new List<string>();
            messages.Add($"*Estado de paquetes*");
            if (packages.Count() > SIMPLE_PACKAGES_AMOUNT && !isUpdate)
                messages.Add($"_Tienes {packages.Count()} paquetes en proceso_");

            var entitiesCount = 2;
            foreach (var package in packages)
            {
                entitiesCount += isUpdate ? 7 : 8;
                if (entitiesCount > _settings.MaxMessageEntities)
                {
                    messages.Add(SEPARATOR);
                    entitiesCount = 2;
                }
                else
                    messages.Add("");

                messages.AddRange(ParseMessageFor(package, previousPackages, !isUpdate));
            }

            return string.Join('\n', messages);
        }

        private IEnumerable<string> ParseMessageFor(Package package, Dictionary<string, Package> previousPackages, bool includesDeliveryDate)
        {
            var message = new List<string>(includesDeliveryDate ? 6 : 5);

            var description = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(package.Description.ToLower());
            message.Add($"*Id*: {package.Identifier}");
            message.Add($"*Descripción*: {description}");
            message.Add($"*Tracking*: `{package.Tracking}`");

            if (includesDeliveryDate)
                message.Add($"*Recibido*: {package.DeliveredAt:MMM dd, yyyy}");

            var previous = previousPackages?.ContainsKey(package.Identifier) == true ? previousPackages[package.Identifier] : package;

            if (previous.Weight != package.Weight)
                message.Add($"*Peso*: {previous.Weight} → {package.Weight} libras");
            else
                message.Add($"*Peso*: {package.Weight} libras");

            if (previous.Status != package.Status)
                message.Add($"*Estado*: {previous.Status.Description} → {package.Status.Description}, _{package.Status.Percentage}_" + (package.Status.Percentage == "90%" ? " ✅" : ""));
            else
                message.Add($"*Estado*: {package.Status.Description}, _{package.Status.Percentage}_" + (package.Status.Percentage == "90%" ? " ✅" : ""));

            return message;
        }

        private async Task SendMessageToChats(string message, IEnumerable<long> channels, CancellationToken cancellationToken)
        {
            var messages = SplitMessage(message);
            var lastMessage = messages.Last();

            var tasks = new List<Task>(channels.Count());

            foreach (var channel in channels)
                tasks.Add(SendMessagesToChat(messages, lastMessage, channel, cancellationToken));

            await Task.WhenAll(tasks);
        }

        private IEnumerable<string> SplitMessage(string message)
        {
            if (message.Contains(SEPARATOR))
            {
                var separatorIndex = message.IndexOf(SEPARATOR);
                var trimmedMessage = message.Substring(0, separatorIndex);
                return new[] { trimmedMessage }.Concat(SplitMessage(message.Substring(separatorIndex + SEPARATOR.Length)));
            }
            if (message.Length > _settings.MaxMessageLength)
            {
                var breaklineIndex = message.Substring(0, _settings.MaxMessageLength).LastIndexOf("\n\n");
                var trimmedMessage = message.Substring(0, breaklineIndex);
                return new[] { trimmedMessage }.Concat(SplitMessage(message.Substring(breaklineIndex + 2)));
            }
            return new[] { message };
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
