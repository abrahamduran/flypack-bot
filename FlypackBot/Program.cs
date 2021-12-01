using FlypackBot.Application.Commands;
using FlypackBot.Application.Services;
using FlypackBot.Infraestructure;
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
                    services.AddHostedService<Worker>();
                    services.AddSingleton<ChatSessionService>();
                    services.AddSingleton<ChatSessionRepository>();
                    services.Configure<TelegramSettings>(ctx.Configuration.GetSection("Telegram"));
                    services.Configure<FlypackSettings>(ctx.Configuration.GetSection("Flypack"));
                    services.Configure<MongoDbSettings>(ctx.Configuration.GetSection("MongoDb"));
                    // TODO: migrate to scope services (eg: commands, AddScoped)
                    services.AddSingleton<StartCommand>();
                    services.AddSingleton<PasswordEncrypterService>();
                    services.AddSingleton<PasswordDecrypterService>();
                    services.AddSingleton<UserService>();
                    services.AddSingleton<FlypackService>();
                    services.AddSingleton<FlypackScrapper>();
                    services.AddSingleton<MongoDbContext>();
                    services.AddSingleton<PackagesRepository>();
                    services.AddSingleton<UserRepository>();
                });
    }
}
