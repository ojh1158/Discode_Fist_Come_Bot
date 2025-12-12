using Discord;
using Discord.WebSocket;
using DiscordBot.scripts._src.Discord;
using DiscordBot.scripts._src.party;
using DiscordBot.scripts._src.Partys;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src.Services;

public class ButtonServices : BaseServices
{
    public ButtonServices(DiscordServices services) : base(services)
    {
        Services.client.ButtonExecuted += HandleButtonAsync;
    }

    private async Task HandleButtonAsync(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId;
        
        // CustomId 파싱: "party_join_{partyId}" 또는 "party_leave_{partyId}"
        var parts = customId.Split('_');
        if (parts.Length < 3 || parts[0] != "party")
            return;
        
        var action = parts[1]; // "join", "leave", "expire" 등

        var messageId = ulong.Parse(parts[2]);

        var isAllMessage = false;
        var message = "알 수 없는 오류가 나타났습니다.";

        var partyEntity = await PartyService.GetPartyEntityAsync(messageId);
        var partyClass = new PartyClass();
        var error = partyClass.Init(partyEntity, component);
        var party = partyClass.Entity;
        
        if (error is not "")
        {
            await component.RespondAsync(error, ephemeral: true);
            return;
        }

        var type = JoinType.Error;
        
        switch (action)
        {
            case PartyConstant.JOIN_KEY:
                type = await PartyService.JoinPartyAsync(party, partyClass.guildUser.Id, partyClass.userNickname);
                    
                // Service에서 중복 체크 포함하여 처리
                if (type is JoinType.Join or JoinType.Wait)
                {
                    if (type is JoinType.Wait)
                    {
                        message = "파티 인원이 가득 찼습니다. 대기 인원으로 등록되었습니다.";
                    }
                    else
                    {
                        message = $"✅ {party.DISPLAY_NAME} 파티에 참가했습니다!";
                    }

                    var members = await PartyService.GetPartyMemberListAsync(party.PARTY_KEY);
                    var waitMembers = await PartyService.GetPartyWaitMemberListAsync(party.PARTY_KEY);

                    if (members != null && waitMembers != null)
                    {
                        party.Members = members;
                        party.WaitMembers = waitMembers;
                    }
                    else
                    {
                        message = "파티 UI 업데이트에 실패하였습니다. 인원 등록은 완료되었습니다";
                    }
                }
                else if(type is JoinType.Exists or JoinType.Error)
                {
                    await component.RespondAsync((type is JoinType.Exists ? "파티에 이미 참가하였습니다." : "알 수 없는 오류가 나타났습니다."), ephemeral: true);
                    _ = Services.RespondMessageWithExpire(component);
                    return;
                }
                break;
                
            case PartyConstant.LEAVE_KEY:
                if (await PartyService.LeavePartyAsync(party, partyClass.userId))
                {
                    message = $"❌ {party.DISPLAY_NAME} 파티에서 나갔습니다.";
                    
                    var members = await PartyService.GetPartyMemberListAsync(party.PARTY_KEY);
                    var waitMembers = await PartyService.GetPartyWaitMemberListAsync(party.PARTY_KEY);

                    if (members != null && waitMembers != null)
                    {
                        party.Members = members;
                        party.WaitMembers = waitMembers;
                    }
                    else
                    {
                        message = "파티 UI 업데이트에 실패하였습니다. 나가기는 완료 되었습니다.";
                    }
                }
                else
                {
                    await component.RespondAsync("파티에 참가하지 않았거나 나가기에 실패했습니다.", ephemeral: true);
                    return;
                }
                break;
            case PartyConstant.OPTION_KEY:
                if (partyClass.isNone)
                {
                    await component.RespondAsync("권한이 없어 표시할 기능이 없습니다.", ephemeral: true);
                    await Services.RespondMessageWithExpire(component, time: 5);
                    return;
                }
                
                await component.RespondAsync("불러오는 중...", ephemeral: true); 
                
                // 옵션 버튼들 만들기
                var componentBuilder = new ComponentBuilder();

                if (partyClass is {isAdmin: true} or {isPartyMember: true} or {isOwner: true})
                {
                    componentBuilder.WithButton(PartyConstant.PULLING_UP_KEY,$"party_{PartyConstant.OPTION_BUTTON_KEY}_{messageId}_{PartyConstant.PULLING_UP_KEY}", ButtonStyle.Success);
                    
                }

                if (party.Members.Count >= 1)
                {
                    componentBuilder.WithButton(PartyConstant.PING_KEY, $"party_{PartyConstant.OPTION_BUTTON_KEY}_{messageId}_{PartyConstant.PING_KEY}", ButtonStyle.Success);
                    if (partyClass.isAdmin || partyClass.isOwner)
                    {
                        componentBuilder.WithButton(PartyConstant.KICK_KEY,$"party_{PartyConstant.OPTION_BUTTON_KEY}_{messageId}_{PartyConstant.KICK_KEY}", ButtonStyle.Success);
                    }
                }

                if (partyClass.isAdmin || partyClass.isOwner)
                {
                    componentBuilder.WithButton(PartyConstant.JOIN_AUTO_KEY, $"party_{PartyConstant.OPTION_BUTTON_KEY}_{messageId}_{PartyConstant.JOIN_AUTO_KEY}", ButtonStyle.Secondary);
                    componentBuilder.WithButton(PartyConstant.PARTY_KEY,$"party_{PartyConstant.OPTION_BUTTON_KEY}_{messageId}_{PartyConstant.PARTY_KEY}", ButtonStyle.Primary);
                }
                
                componentBuilder.WithButton(party.IS_CLOSED ? "재개" : PartyConstant.CLOSE_KEY, $"party_{PartyConstant.OPTION_BUTTON_KEY}_{messageId}_{PartyConstant.CLOSE_KEY}", party.IS_CLOSED ? ButtonStyle.Success : ButtonStyle.Danger);
                componentBuilder.WithButton(PartyConstant.EXPIRE_KEY, $"party_{PartyConstant.OPTION_BUTTON_KEY}_{messageId}_{PartyConstant.EXPIRE_KEY}", ButtonStyle.Secondary);
                
                
                
                await component.ModifyOriginalResponseAsync( m =>
                {
                    m.Content = "버튼을 선택해주세요.";
                    m.Components = componentBuilder.Build();
                });

                await Services.RespondMessageWithExpire(component, time: 30);
                return;
            case PartyConstant.OPTION_BUTTON_KEY:

                if (parts[3] != PartyConstant.PARTY_KEY)
                {
                    // 옵션 메시지를 업데이트로 제거
                    await component.UpdateAsync(msg =>
                    {
                        msg.Content = "처리 중...";
                        msg.Components = null;
                    });
                }
                
                switch (parts[3])
                {
                    case PartyConstant.CLOSE_KEY:
                        var closed = party.IS_CLOSED;
                        var e = party.IS_CLOSED ? "오픈" : "마감";
                        
                        if (partyClass is { isOwner: false, isAdmin: false })
                        {
                            await component.ModifyOriginalResponseAsync(msg =>
                            {
                                msg.Content = $"❌ 파티를 생성한 사람만 {e}할 수 있습니다.";
                            });

                            await Services.RespondMessageWithExpire(component);
                            return;
                        }
                        
                        if (!await PartyService.SetPartyCloseAsync(messageId, !closed))
                        {
                            await component.ModifyOriginalResponseAsync(msg =>
                            {
                                msg.Content = "❌ 파티 조작에 실패하였습니다.";
                            });
                            
                            await Services.RespondMessageWithExpire(component);
                            return;   
                        }

                        // 성공 메시지로 업데이트
                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = $"✅ 파티를 {e}했습니다.";
                        });
                        
                        await Services.RespondMessageWithExpire(component);
                        

                        party.IS_CLOSED = !closed;
                        message = $"{partyClass.userRoleString}님이 {party.DISPLAY_NAME} 파티를 {e}하였습니다.";
                        isAllMessage = true;
                        break;
                    case PartyConstant.PING_KEY:
                        if (!partyClass.isOwner && !partyClass.isAdmin && !partyClass.isPartyMember)
                        {
                            await component.ModifyOriginalResponseAsync(msg =>
                            {
                                msg.Content = "❌ 관리자, 파티원, 파티장만 호출할 수 있습니다!";
                            });
                            
                            await Services.RespondMessageWithExpire(component);
                            return;
                        }
                        
                        // 성공 메시지로 업데이트
                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = "✅ 파티원을 호출했습니다.";
                        });
                        
                        await Services.RespondMessageWithExpire(component);
                        
                        // 파티원 전체 멘션
                        var mentions = string.Join(" ", party.Members.Select(m => $"<@{m.USER_ID}>"));
                        isAllMessage = true;
                        message = $"🔔 {partyClass.userRoleString}님이 파티원을 호출하였습니다!\n{mentions}";
                        break;
                    case PartyConstant.EXPIRE_KEY:
                        // 권한 확인: 파티장 또는 관리자만
                        if (!partyClass.isOwner && !partyClass.isAdmin)
                        {
                            await component.ModifyOriginalResponseAsync(msg =>
                            {
                                msg.Content = "❌ 파티장 또는 관리자만 만료시킬 수 있습니다.";
                            });
                            
                            await Services.RespondMessageWithExpire(component);
                            return;
                        }
                        
                        // 확인 버튼 생성
                        var confirmComponent = new ComponentBuilder()
                            .WithButton("예", $"party_{PartyConstant.EXPIRE_BUTTON_KEY}_{messageId}_{PartyConstant.YES_BUTTON_KEY}", ButtonStyle.Danger)
                            .WithButton("아니오", $"party_{PartyConstant.EXPIRE_BUTTON_KEY}_{messageId}_{PartyConstant.NO_BUTTON_KEY}", ButtonStyle.Secondary)
                            .Build();
                        
                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = $"⚠️ **{party.DISPLAY_NAME}** 파티를 영구적으로 만료시키시겠습니까?\n만료된 파티는 복구할 수 없습니다.";
                            msg.Components = confirmComponent;
                        });
                        _ = Services.RespondMessageWithExpire(component, time: 30);
                        return;
                    case PartyConstant.PARTY_KEY:
                        // Modal로 인원 수 입력받기
                        var renameModal = new ModalBuilder()
                            .WithTitle("파티 설정 변경")
                            .WithCustomId($"party_{PartyConstant.SETTING_MODEL_KEY}_{messageId}")
                            .AddTextInput("이름", "name", TextInputStyle.Short, 
                                placeholder: $"여기에 이름 입력", 
                                required: true,
                                value: party.DISPLAY_NAME,
                                minLength: 1,
                                maxLength: PartyConstant.MAX_NAME_COUNT)
                            .AddTextInput("새로운 인원 수", "count", TextInputStyle.Short, 
                                placeholder: $"{1}-{PartyConstant.MAX_COUNT}", 
                                required: true,
                                value: party.MAX_COUNT_MEMBER.ToString(),
                                minLength: 1,
                                maxLength: 3)
                            .Build();

                        // await component.DeleteOriginalResponseAsync();
                        await component.RespondWithModalAsync(renameModal);
                        return;
                    case PartyConstant.JOIN_AUTO_KEY:
                        
                        var selectMenuBuilder = new SelectMenuBuilder()
                            .WithCustomId($"party_{PartyConstant.JOIN_AUTO_KEY}_{messageId}")
                            .WithPlaceholder("추가할 유저를 선택하세요");
                        
                        // 채널이 서버 채널인 경우에만 유저 목록 가져오기
                        if (component.Channel is IGuildChannel guildChannel)
                        {
                            var allUsers = new List<IGuildUser>();
                            await foreach (var userBatch in guildChannel.GetUsersAsync())
                            {
                                allUsers.AddRange(userBatch);
                            }
                            
                            // SelectMenu 옵션은 최대 25개까지 가능
                            var usersToAdd = allUsers
                                .Where(u => u is { IsBot: false, Status: not UserStatus.Offline }) // 봇 제외, 오프라인 유저 제외
                                .Take(25) // 최대 25개
                                .ToList();
                            
                            if (usersToAdd.Count == 0)
                            {
                                // 추가할 수 있는 유저가 없는 경우
                                await component.ModifyOriginalResponseAsync(msg =>
                                {
                                    msg.Content = "❌ 추가할 수 있는 온라인 유저가 없습니다.";
                                });
                                _ = Services.RespondMessageWithExpire(component, time: 5);
                                return;
                            }
                            
                            foreach (var user in usersToAdd)
                            {
                                selectMenuBuilder.AddOption(
                                    user.DisplayName ?? user.Username, 
                                    user.Id.ToString(),
                                    description: user.Username
                                );
                            }
                        }
                        else
                        {
                            // 서버 채널이 아닌 경우 테스트 옵션만 추가
                            selectMenuBuilder.AddOption("테스트", "ㅎㅎㅎ");
                        }
                        
                        // 확인 버튼 생성
                        var ag = new ComponentBuilder()
                            .WithSelectMenu(selectMenuBuilder)
                            .Build();

                        // await component.DeleteOriginalResponseAsync();
                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = $"⚠️ 추가할 유저를 선택하세요";
                            msg.Components = ag;
                        });
                        _ = Services.RespondMessageWithExpire(component, time: 30);
                        return;
                    case PartyConstant.KICK_KEY:
                        var builder = new ComponentBuilder();
                        
                        foreach (var entity in party.Members)
                        {
                            builder.WithButton($"{entity.USER_NICKNAME}",
                                $"party_{PartyConstant.KICK_BUTTON_KEY}_{messageId}_{entity.USER_ID}");
                        }
                        
                        foreach (var entity in party.WaitMembers)
                        {
                            builder.WithButton($"{entity.USER_NICKNAME}",
                                $"party_{PartyConstant.KICK_BUTTON_KEY}_{messageId}_{entity.USER_ID}");
                        }
                        
                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = $"추방할 맴버를 선택하세요";
                            msg.Components = builder.Build();
                        });
                        _ = Services.RespondMessageWithExpire(component, time: 30);
                        
                        return;
                    
                    case PartyConstant.PULLING_UP_KEY:

                        var channel = component.Channel;
                        
                        var sendMessageAsync = await channel.SendMessageAsync("초기화 중입니다...");
                        
                        // await component.ModifyOriginalResponseAsync(m => m.Content = "초기화 중입니다...");
                        
                        if (!await PartyService.ChangeMessageId(party.MESSAGE_KEY, sendMessageAsync.Id))
                        {
                            await sendMessageAsync.DeleteAsync();
                            // await component.ModifyOriginalResponseAsync( m => m.Content = "파티 생성에 실패하였습니다.");
                            _ = Services.RespondMessageWithExpire(component);
                            return;
                        }

                        var lastMessage = await channel.GetMessageAsync(messageId);
                        await lastMessage.DeleteAsync();

                        party.MESSAGE_KEY = sendMessageAsync.Id;

                        var updatedEmbed = Services.UpdatedEmbed(party);
                        var updatedComponent = Services.UpdatedComponent(party);
                        
                        await sendMessageAsync.ModifyAsync(m =>
                        {
                            m.Embed = updatedEmbed;
                            m.Components = updatedComponent;
                            m.Content = "";
                        });

                        await component.DeleteOriginalResponseAsync();
                        
                        return;
                }
                break;
            case PartyConstant.EXPIRE_BUTTON_KEY:
                
                if (parts[3] == PartyConstant.YES_BUTTON_KEY)
                {
                    if (await Services.ExpirePartyAsync(party, component.Channel))
                    {
                        await component.UpdateAsync(msg =>
                        {
                            msg.Content = $"✅ **{party.DISPLAY_NAME}** 파티를 만료시켰습니다.";
                            msg.Components = null;
                        });
                        
                        _ = Services.RespondMessageWithExpire(component);
                        message = $"❌ {partyClass.userRoleString}님이 파티를 만료시켰습니다.";
                        isAllMessage = true;
                    }
                    else
                    {
                        await component.UpdateAsync(msg =>
                        {
                            msg.Content = $"오류로 인하여 파티를 만료시키지 못하였습니다.";
                            msg.Components = null;
                        });
                        
                        _ = Services.RespondMessageWithExpire(component);
                    }
                }
                else
                {
                    await component.UpdateAsync(msg =>
                    {
                        msg.Content = "❌ 만료가 취소되었습니다.";
                        msg.Components = null;
                    });
                    
                    _ = Services.RespondMessageWithExpire(component);
                    return;
                }
                break;
            case PartyConstant.KICK_BUTTON_KEY:
                await component.DeferAsync();
                
                var id = parts[3];
                var targetUserId = ulong.Parse(id);
                var result = "";
                
                if (await PartyService.KickMemberAsync(party, targetUserId))
                {
                    var user = Services.client.GetGuild(party.GUILD_KEY).GetUser(targetUserId);

                    if (user is IGuildUser guildUser)
                    {
                        result = $"{guildUser.DisplayName} 님을 추방하였습니다.";
                    }
                    else if (user != null)
                    {
                        result = $"{user.GlobalName ?? user.Username} 님을 추방하였습니다.";
                    }
                    else
                    {
                        result = "해당 유저를 추방하였습니다.";
                    }
                    
                    // 파티 정보 갱신
                    var members = await PartyService.GetPartyMemberListAsync(party.PARTY_KEY);
                    var waitMembers = await PartyService.GetPartyWaitMemberListAsync(party.PARTY_KEY);

                    if (members != null && waitMembers != null)
                    {
                        party.Members = members;
                        party.WaitMembers = waitMembers;
                    }
                    else
                    {
                        message = "파티 UI 업데이트에 실패하였습니다. 추방은 완료되었습니다";
                    }
                }
                else
                {
                    result = $"오류";
                }
                
                await component.ModifyOriginalResponseAsync(msg =>
                {
                    msg.Content = result;
                    msg.Components = null;
                });
                _ = Services.RespondMessageWithExpire(component, time: 30);
                await Services.UpdateMessage(component, party, isAllMessage, message);
                return;
        }
        
        await Services.UpdateMessage(component, party, isAllMessage, message);
        await Services.RespondMessageWithExpire(component, message: message);
    }
    
}