using System;
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
            _client.OnMessage += OnMessage;
            _client.OnUpdate += OnUpdate;
            _client.OnInlineQuery += OnInlineQuery;
            _client.StartReceiving(cancellationToken: stoppingToken);
            await _service.SubscribeAsync(_client, _settings.ChannelIdentifier, stoppingToken);

            //return Task.CompletedTask;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _client.StopReceiving();
            //_flypackService.StopAsync();
            return base.StopAsync(cancellationToken);
        }

        #region Bot Event Handlers
        private async void OnMessage(object sender, MessageEventArgs e)
        {
            if (!_settings.AuthorizedUsers.Contains(e.Message.From.Id))
            {
                _logger.LogWarning("Received message from an unauthorized user. User ID: {UserId}, username: {Username}", e.Message.From.Id, e.Message.From.Username);
                return;
            }

            if (e.Message.Text?.StartsWith('/') == true)
            {
                await AnswerBotCommand(e.Message);
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

        private async void OnUpdate(object sender, UpdateEventArgs e)
        {
            switch (e.Update.Type)
            {
                case UpdateType.ChannelPost:
                    await AnswerChannelMessage(e.Update.ChannelPost);
                    break;
            }
        }

        private async void OnInlineQuery(object sender, InlineQueryEventArgs e)
        {
            if (!_settings.AuthorizedUsers.Contains(e.InlineQuery.From.Id))
            {
                _logger.LogWarning("Received message from an unauthorized user. User ID: {UserId}, username: {Username}", e.InlineQuery.From.Id, e.InlineQuery.From.Username);
                return;
            }

            _logger.LogDebug("Received inline query from: {SenderId}", e.InlineQuery.From.Id);
            var query = e.InlineQuery.Query.ToLower();
            var results = _service.GetPackages().Where(x => x.ContainsQuery(query)).Select(x =>
            {
                var content = new InputTextMessageContent(
                    $"*Id*: {x.Identifier}\n" +
                    $"*Descripción*: {x.Description}\n" +
                    $"*Tracking*: {x.TrackingInformation}\n" +
                    $"*Recibido*: {x.Delivered.ToString("MMM dd, yyyy")}\n" +
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
        #endregion

        private async Task AnswerChannelMessage(Message message)
        {
            _logger.LogDebug("Received a text message in channel {ChatId}", message.Chat.Id);
            if (message.Chat.Id != _settings.ChannelIdentifier) return;

            await AnswerBotCommand(message);
        }

        private async Task AnswerBotCommand(Message message)
        {
            var command = message.Text.Split(' ').First();
            Task<string> action = command switch
            {
                "/packages" => _service.LoginAndRequestFreshPackagesListAsync(),
                "/packages@flypackbot" => _service.LoginAndRequestFreshPackagesListAsync(),
                "/packages2" => _service.RequestFreshPackagesListAsync(),
                "/current" => Task.FromResult(_service.GetCurrentPackagesList()),
                "/previous" => Task.FromResult(_service.GetPreviousPackagesList()),
                "/reset" => Task.FromResult(_service.Reset()),
                _ => Task.FromResult("Hasta yo quiero saber")
            };

            var stringMessage = await action;
            if (string.IsNullOrEmpty(stringMessage))
            {
                stringMessage = $"⚠️ El comando {command} no produjo resultados ⚠️";
                _logger.LogWarning("The command {Command} produced no results", command);
            }

            await _client.SendTextMessageAsync(
                chatId: message.Chat,
                text: stringMessage,
                parseMode: ParseMode.Markdown
            );
        }
    }

    internal static class PackageExtension
    {
        internal static bool ContainsQuery(this Package package, string query)
            => (package.Identifier + package.Description + package.Status)
                .ToLower().Contains(query) || string.IsNullOrEmpty(query);
    }
}
