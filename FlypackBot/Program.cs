using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FlypackSettings = FlypackBot.Settings.Flypack;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((ctx, services) =>
                {
                    services.Configure<TelegramSettings>(ctx.Configuration.GetSection("Telegram"));
                    services.Configure<FlypackSettings>(ctx.Configuration.GetSection("Flypack"));
                    services.AddScoped<FlypackService>();
                    services.AddScoped<FlypackScrapper>();
                    services.AddHostedService<Worker>();
                });
    }
}
