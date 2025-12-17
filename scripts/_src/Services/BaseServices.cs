using Discord.WebSocket;

namespace DiscordBot.scripts._src.Services;

public class BaseServices : ISingleton
{
    public DiscordServices Services;
    public BaseServices(DiscordServices services)
    {
        Services = services;
    }
}