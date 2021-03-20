using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlypackBot.Models;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using ScrapySharp.Extensions;
using ScrapySharp.Network;

namespace FlypackBot
{
    public class FlypackScrapper
    {
        private const string BASE_URL = "https://www.flypack.com.do";
        private const string SESSION_EXPIRED_MESSAGE = "Session expirada, ingrese nuevamente al sistema";
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
            var html = await GetHtmlAsync($"{BASE_URL}/run.php", HttpVerb.Post, data, "application /x-www-form-urlencoded");
            var script = html?.SelectSingleNode("//script")?.InnerText;
            var nextLocation = script?.Replace("window.location='", "")?.Replace("';\r\n      ", "");
            return nextLocation;
        }

        public async Task<IEnumerable<Package>> GetPackagesAsync(string path)
        {
            _logger.LogInformation("Get packages list from Flypack");
            var html = await GetHtmlAsync($"{BASE_URL}/{path}", HttpVerb.Get, null, null);
            var rows = html.CssSelect("tbody > tr");

            if (!rows.Any() && !html.InnerHtml.Contains(PACKAGES_PAGE_TITLE))
            {
                _logger.LogWarning("Response structure seems to differ from the expected. Unable to find packages for path: {Path}", path);
                if (html.InnerText.Contains(SESSION_EXPIRED_MESSAGE))
                    _logger.LogWarning("Logged session has expired");

                return null;
            }

            var packages = new List<Package>();
            foreach (var row in rows)
            {
                var columns = row.CssSelect("td").ToArray();
                var info = columns[1].InnerText.Replace("\r\n      ", ",").Split(',');
                var content = columns[2].InnerHtml.Split("<br>");
                var description = content[0].Replace("\r\n\r\n", "");
                var deliveredDate = DateTime.ParseExact(columns[2].InnerHtml.Split("<br>")[1].TrimEnd(), "dd/MM/yyyy", CultureInfo.InvariantCulture);
                var weight = float.Parse(columns[3].InnerText);
                var status = columns[4].CssSelect("label").ElementAt(0).InnerText;
                var percentage = columns[4].CssSelect("div.progress-bar").ElementAt(0).InnerText.Replace("\r\n", "").Trim();

                packages.Add(new Package
                {
                    Identifier = info[0],
                    Tracking = info[1],
                    Description = description,
                    DeliveredAt = deliveredDate,
                    Weight = weight,
                    Status = new PackageStatus { Description = status, Percentage = percentage }
                });
            }

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

        private struct Field
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
    }
}
