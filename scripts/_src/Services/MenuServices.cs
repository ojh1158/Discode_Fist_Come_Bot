using Discord;
using Discord.WebSocket;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Repositories;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src.Services;

public class MenuServices : BaseServices
{
    public MenuServices(DiscordServices services) : base(services)
    {
        Services.client.SelectMenuExecuted += HandleSelectMenuAsync;
    }
    private async Task HandleSelectMenuAsync(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId;
        
        // await component.DeleteOriginalResponseAsync();
        
        // CustomId 파싱: "party_{JOIN_AUTO_KEY}_{messageId}"
        var parts = customId.Split('_');
        if (parts.Length < 3 || parts[0] != "party")
            return;
        
        var action = parts[1]; // "인원추가"
        var messageId = ulong.Parse(parts[2]);
        var allMessageFlag = false;
        var allmessage = "";
        
        await component.RespondAsync($"처리 중...", ephemeral: true);
        
        // 파티 정보 가져오기
        var partyEntity = await PartyService.GetPartyEntityAsync(messageId);

        if (partyEntity == null)
        {
            await component.RespondAsync($"파티를 찾을 수 없습니다.", ephemeral: true);
            _ = Services.RespondMessageWithExpire(component, 5);
            return;
        }

        var partyClass = new PartyClass();
        await partyClass.Init(partyEntity, component, Services.client);

        // 선택된 값들 가져오기 (SelectMenu는 여러 값 선택 가능)
        var selectedValues = component.Data.Values; // string[] 배열
        switch (action)
        {
            case PartyConstant.JOIN_AUTO_KEY:
        
                if (action == PartyConstant.JOIN_AUTO_KEY)
                {
                    
                    if (partyEntity == null)
                    {
                        await component.ModifyOriginalResponseAsync(m=> m.Content = "파티를 찾을 수 없습니다.");
                        return;
                    }

                    string? message = null;
                    
                    foreach (var selectedValue in selectedValues)
                    {
                        // 선택된 유저 ID를 파싱하여 파티에 추가
                        // 예: selectedValue가 "123456789" (ulong)라면
                        
                        
                        if (ulong.TryParse(selectedValue, out var userId))
                        {
                            IUser? user = Services.client.GetGuild(partyEntity.GUILD_KEY).GetUser(userId);
                            user ??= await Services.client.GetUserAsync(userId); // RestUser 반환
                            
                            if (user is { IsBot: false })
                            {
                                string name;
                                
                                if (user is IGuildUser guildUser)
                                {
                                    name = guildUser.DisplayName;
                                }
                                else
                                {
                                    // 길드에서 최신 유저 정보를 가져와서 닉네임 확인 (Rest API 사용)
                                    try
                                    {
                                        var restGuild = await Services.client.Rest.GetGuildAsync(partyEntity.GUILD_KEY);
                                        if (restGuild != null)
                                        {
                                            var guildUserInfo = await restGuild.GetUserAsync(userId);
                                            if (guildUserInfo != null && !string.IsNullOrEmpty(guildUserInfo.Nickname))
                                            {
                                                // Rest API에서 가져온 닉네임 사용
                                                name = guildUserInfo.Nickname;
                                            }
                                            else
                                            {
                                                // 닉네임이 없으면 Username 사용
                                                name = guildUserInfo?.GlobalName ?? user.Username;
                                            }
                                        }
                                        else
                                        {
                                            name = user.GlobalName ?? user.Username;
                                        }
                                    }
                                    catch
                                    {
                                        name = user.GlobalName ?? user.Username;
                                    }
                                }
                                
                                var type = await PartyService.JoinPartyAsync(partyEntity, userId, name);
                                
                                // 파티에 유저 추가 로직
                                if (message == null)
                                    message = "";
                                message += $"{name} : {type.GetComment()}\n";
                            }
                        }
                    }
                    
                    string finalMessage;
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        finalMessage = "아무도 추가되지 못하였습니다.";
                        await component.ModifyOriginalResponseAsync(m => m.Content = finalMessage);
                        _ = Services.RespondMessageWithExpire(component, 5);
                    }
                    else
                    {
                        allMessageFlag = true;
                        allmessage = $"{partyClass.userRoleString}님이 다음 파티원을 초대하였습니다!\n" + message;
                        await component.DeleteOriginalResponseAsync();
                    }
                }
                break;
            case PartyConstant.KICK_KEY:

                var ms = "";

                var dic = new Dictionary<ulong, PartyMemberEntity>();
                
                foreach (var entity in partyEntity.Members)
                {
                    dic.TryAdd(entity.USER_ID, entity);
                }
                foreach (var entity in partyEntity.WaitMembers)
                {
                    dic.TryAdd(entity.USER_ID, entity);
                }
                
                foreach (var selectedValue in selectedValues)
                {
                    if (ulong.TryParse(selectedValue, out var userId))
                    {
                        if (await PartyService.KickMemberAsync(partyEntity, userId))
                        {
                            ms += $"{dic[userId].USER_NICKNAME} 님을 추방하였습니다.\n";
                        }
                    }
                }

                
                await component.DeleteOriginalResponseAsync();
                await component.Channel.SendMessageAsync($"{partyClass.userNickname} 님이 아래의 파티원을 추방하였습니다.\n"+ ms);
                break;
            case PartyConstant.TEAM_KEY:
                
                
                break;
        }
        
        var party = await PartyService.GetPartyEntityAsync(messageId);

        if (party != null)
        {
            await Services.UpdateMessage(component, party, allMessageFlag, allmessage);
        }
    }
}