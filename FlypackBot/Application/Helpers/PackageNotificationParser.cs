﻿using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlypackBot.Domain.Models;
using Microsoft.Extensions.Options;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot.Application.Helpers
{
    public class PackageNotificationParser
    {
        private const int SIMPLE_PACKAGES_AMOUNT = 3;
        private const string SEPARATOR = "_break-line_";

        private readonly TelegramSettings _settings;

        public PackageNotificationParser(IOptions<TelegramSettings> settings)
            => _settings = settings.Value;

        public IEnumerable<string> ParseMessageFor(Package package) => ParseMessageFor(package, null, true);
        public string ParseMessageFor(IEnumerable<Package> packages) => ParseMessageFor(packages, null, false);
        public string ParseMessageFor(IEnumerable<Package> packages, Dictionary<string, Package> previousPackages, bool isUpdate)
        {
            if (packages == null || !packages.Any())
                return L10n.strings.EmptyPackageListMessage;

            var messages = new List<string>
            {
                $"*{L10n.strings.PackageStatus}*"
            };
            if (packages.Count() > SIMPLE_PACKAGES_AMOUNT && !isUpdate)
                messages.Add($"_{string.Format(L10n.strings.PackagesInProcessMessage, packages.Count())}_");

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

        public IEnumerable<string> SplitMessage(string message)
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

        private IEnumerable<string> ParseMessageFor(Package package, Dictionary<string, Package> previousPackages, bool includesDeliveryDate)
        {
            var message = new List<string>(includesDeliveryDate ? 6 : 5);

            var description = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(package.Description.ToLower());
            message.Add($"*ID*: {package.Identifier}");
            message.Add($"*{L10n.strings.DescriptionField}*: {description}");
            message.Add($"*{L10n.strings.TrackingField}*: `{package.Tracking}`");

            if (includesDeliveryDate)
                message.Add($"*{L10n.strings.ReceivedByField}*: {package.DeliveredAt:MMM dd, yyyy}");

            var previous = previousPackages?.ContainsKey(package.Identifier) == true ? previousPackages[package.Identifier] : package;

            if (previous.Weight != package.Weight)
                message.Add($"*{L10n.strings.WeightField}*: {previous.Weight} → {package.Weight} {L10n.strings.PoundsText}");
            else
                message.Add($"*{L10n.strings.WeightField}*: {package.Weight} {L10n.strings.PoundsText}");

            if (previous.Status != package.Status)
                message.Add($"*{L10n.strings.StatusField}*: {previous.Status.Description} → {package.Status.Description}, _{package.Status.Percentage}_" + (package.Status.Percentage == "90%" ? " 🎉" : ""));
            else
                message.Add($"*{L10n.strings.StatusField}*: {package.Status.Description}, _{package.Status.Percentage}_" + (package.Status.Percentage == "90%" ? " 🎉" : ""));

            return message;
        }
    }
}
