using Discord.WebSocket;
using DiscordBot.scripts._src.Discord;

namespace DiscordBot.scripts._src.Services;

public class BaseServices : ISingleton
{
    public DiscordServices Services;
    public BaseServices(DiscordServices services)
    {
        Services = services;
    }
}