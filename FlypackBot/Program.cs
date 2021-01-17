using FlypackBot.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using FlypackSettings = FlypackBot.Settings.Flypack;
using MongoDbSettings = FlypackBot.Settings.MongoDb;
using TelegramSettings = FlypackBot.Settings.Telegram;

namespace FlypackBot
{
    public class Program
    {
        public static void Main(string[] args) =>
            CreateHostBuilder(args).Build().Run();

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(builder =>
                    builder.AddSimpleConsole(options =>
                    {
                        options.ColorBehavior = LoggerColorBehavior.Enabled;
                        options.TimestampFormat = "[yyyy-MM-dd HH:mm] ";
                    })
                )
                .ConfigureServices((ctx, services) =>
                {
                    services.Configure<TelegramSettings>(ctx.Configuration.GetSection("Telegram"));
                    services.Configure<FlypackSettings>(ctx.Configuration.GetSection("Flypack"));
                    services.Configure<MongoDbSettings>(ctx.Configuration.GetSection("MongoDb"));
                    services.AddScoped<FlypackService>();
                    services.AddScoped<FlypackScrapper>();
                    services.AddScoped<MongoDbContext>();
                    services.AddScoped<PackagesRepository>();
                    services.AddHostedService<Worker>();
                });
    }
}
