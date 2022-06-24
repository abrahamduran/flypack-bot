using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FlypackBot.Domain.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ScrapySharp.Extensions;
using ScrapySharp.Network;

namespace FlypackBot.Infraestructure
{
    public class FlypackScrapper
    {
        private const string BASE_URL = "https://www.flypack.one";
        private const string SESSION_EXPIRED_MESSAGE = "Session expirada, ingrese nuevamente al sistema";
        private const string INVALID_LOGIN_MESSAGE = "index.php?ID=323&OPTIONS=LogiN&MSG=USUARIO O CLAVE INVALIDO";
        private const string PACKAGES_PAGE_TITLE = "<h2 class=\"mb-4\">Mis Paquetes</h2>";
        private readonly ILogger<FlypackScrapper> _logger;
        private readonly ScrapingBrowser _browser;

        public FlypackScrapper(ILogger<FlypackScrapper> logger)
        {
            _logger = logger;
            _browser = new ScrapingBrowser();
        }

        public async Task<string> LoginAsync(string username, string password)
        {
            _logger.LogInformation("Login into Flypack with account: {Account}", username);
            var data = new[] {
                new Field() { Name = "EJECUTE", Value = "1" },
                new Field() { Name = "contactForm", Value = "" },
                new Field() { Name = "text1", Value = username },
                new Field() { Name = "text2", Value = password }
            };
            var html = await GetHtmlAsync($"{BASE_URL}/run.php", HttpVerb.Post, data, "application/x-www-form-urlencoded");
            var script = html?.SelectSingleNode("//script")?.InnerText;
            var nextLocation = script?.Replace("window.location='", "")?.Replace("';\r\n      ", "");
            return nextLocation != INVALID_LOGIN_MESSAGE ? nextLocation : null;
        }

        public async Task<IEnumerable<Package>> GetPackagesAsync(string path, string username)
        {
            _logger.LogInformation("Get packages list from Flypack");
            var html = await GetHtmlAsync($"{BASE_URL}/{path}", HttpVerb.Get, null, null);
            var webUsername = ParseUsernameHtml(html);
            var packages = ParsePackagesHtml(html, webUsername ?? username);

            if (webUsername != username)
                _logger.LogWarning("username found in web response differs from the provided username. Web's Username: {WebUsername}, Provided Username: {ProvidedUsername}", webUsername, username);

            if (packages == null && html.InnerText.Contains(SESSION_EXPIRED_MESSAGE))
                _logger.LogDebug("Logged session has expired");
            else if (packages == null && !html.InnerHtml.Contains(PACKAGES_PAGE_TITLE))
                _logger.LogCritical("Response structure seems to differ from the expected. Unable to find packages for path: {Path}", path);

            return packages;
        }

        private async Task<HtmlNode> GetHtmlAsync(string url, HttpVerb verb, Field[] data, string contentType)
        {
            WebPage webpage = await _browser.NavigateToPageAsync(new Uri(url), verb, Serialize(data), contentType);
            return webpage.Html;
        }

        private string Serialize(Field[] fields)
        {
            if (fields is null)
                return string.Empty;

            var builder = new StringBuilder();

            for (int i = 0; i < fields.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(fields[i].Name))
                    continue;

                if (i > 0)
                    builder.Append('&');
                builder.AppendFormat("{0}={1}", Uri.EscapeDataString(fields[i].Name), Uri.EscapeDataString(fields[i].Value));
            }

            return builder.ToString();
        }

        private string ParseUsernameHtml(HtmlNode html)
        {
            var node = html.CssSelect("body > div > div.d-none.d-sm-block > h5 > strong");
            var text = node.SingleOrDefault()?.InnerText ?? "";
            return Regex.Match(text, @"\d+").Value;
        }

        private IEnumerable<Package> ParsePackagesHtml(HtmlNode html, string username)
        {
            var rows = html.CssSelect("tbody > tr");
            if (!rows.Any()) return null;

            var packages = new List<Package>();
            foreach (var row in rows)
            {
                var columns = row.CssSelect("td").ToArray();
                var info = columns[1].InnerText.Replace("\r\n      ", ",").Split(',');
                var content = columns[2].InnerHtml.Split("<br>");
                var description = content[0].Replace("\r\n\r\n", "");
                var deliveredDate = DateTime.ParseExact(columns[2].InnerHtml.Split("<br>")[1].TrimEnd(), "dd/MM/yyyy", CultureInfo.InvariantCulture);

                // Flypack made a change to the UI which misplaced weight values
                float weight;
                var parsed = float.TryParse(columns[3].InnerText, out weight);
                if (parsed == false)
                    float.TryParse(columns[4].InnerText, out weight);

                // Flypack made a change to the UI which pushed the status to the next column
                var statusCell = columns[4].CssSelect("label");
                statusCell = statusCell.Any() ? statusCell : columns[5].CssSelect("label");
                var status = statusCell.ElementAt(0).InnerText;
                var percentageCell = columns[4].CssSelect("div.progress-bar");
                percentageCell = percentageCell.Any() ? percentageCell : columns[5].CssSelect("div.progress-bar");
                var percentage = percentageCell.ElementAt(0).InnerText.Replace("\r\n", "").Trim();

                packages.Add(new Package
                {
                    Identifier = info[0],
                    Username = username,
                    Tracking = info[1],
                    Description = description,
                    DeliveredAt = deliveredDate,
                    Weight = weight,
                    Status = new PackageStatus { Description = status, Percentage = percentage }
                });
            }

            return packages;
        }

        private struct Field
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
    }
}
