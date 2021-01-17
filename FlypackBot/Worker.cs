using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlypackBot.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot
{
    public class Worker : BackgroundService
    {
        private const int SIMPLE_PACKAGES_AMOUNT = 3;

        private readonly ILogger<Worker> _logger;
        private readonly FlypackService _service;
        private readonly TelegramSettings _settings;
        private readonly TelegramBotClient _client;

        public Worker(ILogger<Worker> logger, IOptions<TelegramSettings> settings, FlypackService service)
        {
            _logger = logger;
            _settings = settings.Value;
            _service = service;
            _client = new TelegramBotClient(_settings.AccessToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _client.OnUpdate += OnBotUpdate;
            _client.OnMessage += OnBotMessage;
            _client.OnInlineQuery += OnBotInlineQuery;

            _service.OnUpdate += OnFlypackUpdate;
            _service.OnFailedLogin += OnFlypackFailedLogin;
            _service.OnFailedFetch += OnFlypackFailedFetch;
            try
            {
                _client.StartReceiving(cancellationToken: stoppingToken);
                await _service.SubscribeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex);
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _service.StopAsync();
            _client.StopReceiving();
            return base.StopAsync(cancellationToken);
        }

        #region Telegram Event Handlers
        private async void OnBotMessage(object sender, MessageEventArgs e)
        {
            if (!_settings.AuthorizedUsers.Contains(e.Message.From.Id))
            {
                _logger.LogWarning("Received message from an unauthorized user. User ID: {UserId}, username: {Username}", e.Message.From.Id, e.Message.From.Username);
                return;
            }

            if (e.Message.Text?.StartsWith('/') == true)
            {
                try
                {
                    await AnswerBotCommand(e.Message);
                }
                catch (Exception ex)
                {
                    await HandleExceptionAsync(ex);
                }
            }
            else if (e.Message.Text != null)
            {
                _logger.LogDebug("Received a text message in chat {ChatId}", e.Message.Chat.Id);

                await _client.SendTextMessageAsync(
                  chatId: e.Message.Chat,
                  text: "You said:\n" + e.Message.Text
                );
            }
        }

        private async void OnBotUpdate(object sender, UpdateEventArgs e)
        {
            try
            {
                switch (e.Update.Type)
                {
                    case UpdateType.ChannelPost:
                        await AnswerChannelMessage(e.Update.ChannelPost);
                        break;
                }
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex);
            }
        }

        private async void OnBotInlineQuery(object sender, InlineQueryEventArgs e)
        {
            if (!_settings.AuthorizedUsers.Contains(e.InlineQuery.From.Id))
            {
                _logger.LogWarning("Received message from an unauthorized user. User ID: {UserId}, username: {Username}", e.InlineQuery.From.Id, e.InlineQuery.From.Username);
                return;
            }

            _logger.LogDebug("Received inline query from: {SenderId}", e.InlineQuery.From.Id);
            try
            {
                var query = e.InlineQuery.Query.ToLower();
                var results = _service.GetPackages().Where(x => x.ContainsQuery(query)).Select(x =>
                {
                    var content = new InputTextMessageContent(
                        $"*Id*: {x.Identifier}\n" +
                        $"*Descripción*: {x.Description}\n" +
                        $"*Tracking*: {x.Tracking}\n" +
                        $"*Recibido*: {x.DeliveredAt.ToString("MMM dd, yyyy")}\n" +
                        $"*Peso*: {x.Weight} libras\n" +
                        $"*Estado*: {x.Status.Description}, _{x.Status.Percentage}_"
                    )
                    { ParseMode = ParseMode.Markdown };

                    return new InlineQueryResultArticle(x.Identifier, x.Description, content)
                    {
                        Description = $"{x.Status}\n{x.Weight} libras"
                    };
                }).ToList();

                await _client.AnswerInlineQueryAsync(e.InlineQuery.Id, results, 60, true);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex);
            }
        }
        #endregion

        #region Flypack Event Handlers
        private async void OnFlypackUpdate(object sender, PackagesEventArgs e)
        {
            var message = ParseMessageFor(e.Packages, e.PreviousPackages, true);
            await SendMessageToChat(message, _settings.ChannelIdentifier);
        }

        private async void OnFlypackFailedLogin(object sender, EventArgs e)
            => await SendMessageToChat("⚠️ Hubo un error al iniciar sesión ⚠️", _settings.ChannelIdentifier);

        private async void OnFlypackFailedFetch(object sender, EventArgs e)
            => await SendMessageToChat("⚠️ Ocurrió un error al intentar recuperar la lista de paquetes ⚠️", _settings.ChannelIdentifier);
        #endregion

        private async Task AnswerChannelMessage(Message message)
        {
            _logger.LogDebug("Received a text message in channel {ChatId}", message.Chat.Id);
            if (message.Chat.Id != _settings.ChannelIdentifier) return;

            await AnswerBotCommand(message);
        }

        private async Task AnswerBotCommand(Message message)
        {
            await _client.SendChatActionAsync(message.Chat, ChatAction.Typing);

            var command = message.Text.Split(' ').First();

            string stringMessage = command switch
            {
                "/packages" => ParseMessageFor(await _service.LoginAndFetchPackagesAsync(), null, false),
                "/packages@flypackbot" => ParseMessageFor(await _service.LoginAndFetchPackagesAsync(), null, false),
                _ => "Hasta yo quiero saber"
            };

            if (string.IsNullOrEmpty(stringMessage))
            {
                stringMessage = $"⚠️ El comando {command} no produjo resultados ⚠️";
                _logger.LogWarning("The command {Command} produced no results", command);
            }

            await SendMessageToChat(stringMessage, message.Chat);
        }

        private async Task HandleExceptionAsync(Exception exception)
        {
            if (exception is TaskCanceledException)
                _logger.LogWarning("Task has been cancelled. Message: {Message}", exception.Message);
            else
            {
                _logger.LogError(exception, exception.Message);
                await SendMessageToChat("🧨 Ha ocurrido un error 🧨", _settings.ChannelIdentifier);
            }
        }

        private async Task SendMessageToChat(string message, ChatId chatId)
        {
            var messages = SplitMessage(message);

            foreach (var msg in messages)
            {
                await _client.SendTextMessageAsync(
                    chatId: chatId,
                    text: msg,
                    parseMode: ParseMode.Markdown
                );
            }
        }

        private IEnumerable<string> SplitMessage(string message)
        {
            if (message.Length > _settings.MaxMessageLength)
            {
                var breaklineIndex = message.Substring(0, _settings.MaxMessageLength).LastIndexOf("\n\n");
                var trimmedMessage = message.Substring(0, breaklineIndex);
                return new[] { trimmedMessage }.Concat(SplitMessage(message.Substring(breaklineIndex + 2)));
            }
            return new[] { message };
        }

        private string ParseMessageFor(IEnumerable<Package> packages, Dictionary<string, Package> previousPackages, bool isUpdate)
        {
            if (packages == null || !packages.Any())
                return "⚠️ Lista de paquetes vacía ⚠️";

            List<string> messages = new List<string>();
            messages.Add($"*Estado de paquetes*");
            if (packages.Count() > SIMPLE_PACKAGES_AMOUNT && !isUpdate)
                messages.Add($"_Tienes {packages.Count()} paquetes en proceso_");

            messages.Add("");

            foreach (var package in packages)
            {
                var description = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(package.Description.ToLower());
                messages.Add($"*Id*: {package.Identifier}");
                messages.Add($"*Descripción*: {description}");
                messages.Add($"*Tracking*: {package.Tracking}");

                if (!isUpdate)
                    messages.Add($"*Recibido*: {package.DeliveredAt:MMM dd, yyyy}");

                var previous = previousPackages != null && previousPackages.ContainsKey(package.Identifier)
                    ? previousPackages[package.Identifier] : package;

                if (previous.Weight != package.Weight)
                    messages.Add($"*Peso*: {previous.Weight} → {package.Weight} libras");
                else
                    messages.Add($"*Peso*: {package.Weight} libras");

                if (previous.Status != package.Status)
                    messages.Add($"*Estado*: {previous.Status.Description} → {package.Status.Description}, _{package.Status.Percentage}_" + (package.Status.Percentage == "90%" ? " 🎄" : ""));
                else
                    messages.Add($"*Estado*: {package.Status.Description}, _{package.Status.Percentage}_" + (package.Status.Percentage == "90%" ? " 🎄" : ""));

                messages.Add("");
            }

            messages.RemoveAt(messages.Count - 1);
            return string.Join('\n', messages);
        }
    }

    internal static class PackageExtension
    {
        internal static bool ContainsQuery(this Package package, string query)
            => (package.Identifier + package.Description + package.Status + package.Tracking)
                .ToLower().Contains(query) || string.IsNullOrEmpty(query);
    }
}
