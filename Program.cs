using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;
using TvGuide;
using TvGuide.Events;
using TvGuide.Modules;
using TvGuide.Services;
using TwitchSharp.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddSingleton<IValidateOptions<Configuration>, ConfigurationValidator>();

builder.Services
    .AddOptions<Configuration>()
    .Bind(builder.Configuration)
    .ValidateOnStart();

builder.AddTwitchApi();

builder.Services
    .AddApplicationCommands<SlashCommandInteraction, ApplicationCommandContext>();

builder.Services
    .AddSingleton<NowLiveService>()
    .AddSingleton<INowLiveService>(static serviceProvider => serviceProvider.GetRequiredService<NowLiveService>())
    .AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<NowLiveService>())
    .AddSingleton<DataModule>()
    .AddSingleton<ActiveBroadcastsModule>()
    .AddSingleton<BroadcastStates>();

builder.Services
    .AddDiscordGateway()
    .AddGatewayHandlers(typeof(Program).Assembly);

var host = builder.Build()
    .AddModules(typeof(Program).Assembly);

await host.RunAsync();