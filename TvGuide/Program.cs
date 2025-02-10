using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;
using TvGuide;
using TvGuide.Events;
using TvGuide.Modules;
using TvGuide.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<Configuration>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations();

builder.Services
    .AddHttpClient();

builder.Services
    .AddApplicationCommands<SlashCommandInteraction, ApplicationCommandContext>();

builder.Services
    .AddSingleton<IAuthenticationModule, AuthenticationModule>()
    .AddSingleton<INowStreamingService, NowLiveService>()
    .AddSingleton<DataModule>()
    .AddSingleton<ActiveStreamsModule>()
    .AddSingleton<NowLiveStates>();

builder.Services
    .AddScoped<IStreamsModule, StreamsModule>()
    .AddScoped<IClipsModule, ClipsModule>()
    .AddScoped<IUsersModule, UsersModule>();

builder.Services
    .AddHostedService<TokenRefreshService>()
    .AddHostedService<NowLiveService>();

builder.Services
    .AddDiscordGateway(options =>
    {
        options.Intents =
            GatewayIntents.GuildMessages |
            GatewayIntents.GuildMessageReactions |
            GatewayIntents.MessageContent;
    });

builder.Services
    .AddLogging(configure => configure
        .AddConsole());

var host = builder.Build()
    .AddModules(typeof(Program).Assembly)
    .UseGatewayEventHandlers();

await host.RunAsync();