using Discord;
using Discord.WebSocket;
using DiscordBot.scripts._src.party;

namespace DiscordBot.scripts._src.Services;

public class BaseServices(DiscordServices services) : ISingleton
{
    protected DiscordServices Services = services;
    
    private string warkText = "작업 중...";

    protected async Task InitCommands(SocketInteraction component, string action)
    {
        switch (action)
        {
            case PartyConstant.PARTY_KEY or PartyConstant.TEAM_KEY:
                break;
            default:
                if (!component.HasResponded && action is
                        not PartyConstant.JOIN_KEY
                        and not PartyConstant.LEAVE_KEY
                        and not PartyConstant.OPTION_KEY
                   )
                {
                    try
                    {
                        switch (component)
                        {
                            case SocketMessageComponent messageComponent:
                                await messageComponent.UpdateAsync(msg =>
                                {
                                    msg.Content = warkText;
                                    msg.Components = null;
                                });
                                break;
                            case SocketModal submitModal:
                                await submitModal.UpdateAsync(msg =>
                                {
                                    msg.Content = warkText;
                                    msg.Components = null;
                                });
                                break;
                        }
                    }
                    catch
                    {
                        await component.RespondAsync(warkText, ephemeral: true);
                    }
                }
                else if (component.HasResponded)
                {
                    await component.ModifyOriginalResponseAsync(m => m.Content = warkText);
                }
                else
                {
                    await component.RespondAsync(warkText, ephemeral: true);
                }
                break;
        }
    }
}