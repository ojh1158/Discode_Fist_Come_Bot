using System.Diagnostics;
using System.Security.Cryptography;
using DiscordBot.scripts.db;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Dapper;
using DiscordBot.scripts._src.Partys;
using DiscordBot.scripts.db.Models;
using DiscordBot.scripts.db.Repositories;
using DiscordBot.scripts.db.Services;

namespace DiscordBot.scripts._src;

public class Controller
{
    private readonly DiscordSocketClient _client;
    
    private const int MIN_COUNT = 1;
    private const int MAX_COUNT = 200;
    private const int MAX_HOUR = 168;
    private const int MAX_NAME_COUNT = 50;
    
    private const string VERSION = "1.1.1";

    private const string JOIN_KEY = "참가";
    private const string LEAVE_KEY = "나가기";
    private const string CLOSE_KEY = "일시정지";
    private const string OPTION_KEY = "기능";

    private const string EXPIRE_BUTTON_KEY = "expire";
    private const string OPTION_BUTTON_KEY = "button";
    private const string KICK_BUTTON_KEY = "kick";
    
    private const string SETTING_MODEL_KEY = "setting";
    
    private const string EXPIRE_KEY = "만료(영구)";
    private const string PING_KEY = "호출(파티원)";
    private const string PARTY_KEY = "파티설정";
    private const string KICK_KEY = "강퇴";
    
    private const string YES_BUTTON_KEY = "yes";
    private const string NO_BUTTON_KEY = "no";
    
    
    public Controller(DiscordSocketClient client)
    {
        _client = client;
    }
    public void Init()
    {
        _client.SlashCommandExecuted += HandleSlashCommandAsync;
        _client.ButtonExecuted += HandleButtonAsync;
        _client.ModalSubmitted += HandleModalAsync;
        _client.Ready += InitCommands;
        Cycle();
    }

    // ReSharper disable once FunctionRecursiveOnAllPaths
    private async void Cycle()
    {
        try
        {
            // 다음 정각(00초)까지 대기
            var now = DateTime.Now;
            var secondsUntilNextMinute = 60 - now.Second;
            var millisecondsToSubtract = now.Millisecond;
            var delay = TimeSpan.FromSeconds(secondsUntilNextMinute).Subtract(TimeSpan.FromMilliseconds(millisecondsToSubtract));
            
            Console.WriteLine($"[Cycle] 다음 정각까지 대기 중... (현재: {now:HH:mm:ss.fff}, 대기: {delay.TotalSeconds:F1}초)");
            await Task.Delay(delay);
            
            // 작업 실행
            Console.WriteLine($"[Cycle] 만료 파티 체크 시작 (시간: {DateTime.Now:HH:mm:ss})");
            var partyList = await PartyService.CycleExpiredPartyListAsync();
            
            if (partyList.Count > 0)
            {
                Console.WriteLine($"[Cycle] {partyList.Count}개의 만료 파티 발견");
                foreach (var partyEntity in partyList)
                {
                    await ExpirePartyAsync(partyEntity);
                }
            }
            
            // 다음 사이클 시작
            Cycle();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Cycle] 오류 발생: {e.Message}");
            Console.WriteLine(e);
            Cycle();
        }
    }

    private async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        var commandName = command.Data.Name;
        
        if (command.Channel is SocketGuildChannel guildChannel)
        {
            // 봇의 현재 권한 가져오기
            var permissions = guildChannel.Guild.CurrentUser.GetPermissions(guildChannel);

            // 필요한 권한 체크 (채널 보기 & 메시지 보내기)
            if (!permissions.ViewChannel)
            {
                await command.RespondAsync("🚫 이 채널에 대한 접근 권한이 없습니다. 권한을 확인해주세요.", ephemeral: true);
                return;
            }

            if (!permissions.SendMessages)
            {
                await command.RespondAsync("🚫 이 채널에 대한 메시지 전송 권한이 없습니다. 권한을 확인해주세요.", ephemeral: true);
                return;
            }

            // 메시지 기록 보기 권한 체크
            if (!permissions.ReadMessageHistory)
            {
                await command.RespondAsync("🚫 이 채널의 '메시지 기록 보기' 권한이 없습니다.", ephemeral: true);
                return;
            }
            
            if (!await GuildService.GuildCheckAsync(guildChannel.Id, guildChannel.Guild.Name))
            {
                await command.RespondAsync("🚫 이 채널을 검증할 수 없거나 제한되었습니다.", ephemeral: true);
                return;
            }
        }
        else
        {
            await command.RespondAsync("서버에서만 사용 가능합니다.", ephemeral: true);
            return;
        }
        
        if (commandName != "파티")
        {
            await command.RespondAsync("알 수 없는 명령입니다.", ephemeral: true);
            return;
        }
        
        
        
        var commandOptions = command.Data.Options;
        var nameOption = commandOptions.FirstOrDefault(x => x.Name == "이름");
        var countOption = commandOptions.FirstOrDefault(x => x.Name == "인원");
        var timeOption = commandOptions.FirstOrDefault(x => x.Name == "만료시간");
        // var collOption = commandOptions.FirstOrDefault(x => x.Name == "호출");
        
        if (nameOption?.Value == null || countOption?.Value == null || !int.TryParse(countOption.Value.ToString(), out var count))
        {
            await command.RespondAsync("명령어에 오류가 있습니다.", ephemeral: true);
            return;
        }

        if (count is < MIN_COUNT or > MAX_COUNT)
        {
            await command.RespondAsync($"파티 인원은 최소 {MIN_COUNT} 최대 {MAX_COUNT}까지만 지정할 수 있습니다.", ephemeral: true);
            return;
        }
                
        var partyName = nameOption.Value.ToString()!;
        
        if (await PartyService.IsPartyExistsAsync(partyName, (ulong)command.GuildId!))
        {
            await command.RespondAsync("해당 파티 이름이 이미 있습니다.", ephemeral: true);
            return;
        }

        var time = TimeSpan.FromHours(MAX_HOUR);

        if (timeOption?.Value != null)
        {
            var timeString = timeOption.Value.ToString()!.ToLower();
            if (!int.TryParse(timeString[..1], out var number))
            {
                await command.RespondAsync("시간 형식이 알맞지 않습니다!", ephemeral: true);
                return;
            }
            
            switch (timeString[^1])
            {
                case 'm' or '분' :
                    time = TimeSpan.FromMinutes(number);
                    break;
                case 'h' or '시':
                    time = TimeSpan.FromHours(number);
                    break;
                default:
                    await command.RespondAsync("시간 형식이 알맞지 않습니다!", ephemeral: true);
                    return;
            }
        }
        

        if (time >= TimeSpan.FromHours(MAX_HOUR))
        {
            time = TimeSpan.FromHours(MAX_HOUR);
        }
        
        await command.RespondAsync("초기화 중입니다...");
        var message = await command.GetOriginalResponseAsync();
        
        var now = DateTime.Now;
        var party = new PartyEntity
        {
            DISPLAY_NAME = partyName,
            MAX_COUNT_MEMBER = count,
            MESSAGE_KEY = message.Id,
            GUILD_KEY = (ulong)command.GuildId!,
            CHANNEL_KEY = (ulong)command.ChannelId!,
            OWNER_KEY = command.User.Id,
            OWNER_NICKNAME = command.User is SocketGuildUser user
                ? user.DisplayName
                : command.User.Username,
            EXPIRE_DATE = now.AddSeconds(-now.Second).AddMilliseconds(-now.Millisecond).Add(time),
        };
        
        if (!await PartyService.CreatePartyAsync(party))
        {
            await message.DeleteAsync();
            await command.FollowupAsync("파티 생성에 실패하였습니다.", ephemeral: true);
            await RespondMessageWithExpire(command);
            return;
        }

        var updatedEmbed = UpdatedEmbed(party);
        var component = UpdatedComponent(party);
        
        await message.ModifyAsync(m =>
        {
            m.Embed = updatedEmbed;
            m.Components = component;
            m.Content = "";
        });
        
        
        
        // if (collOption != null)
        // {
        //     // 파티 메시지에 답장 형태로 Role 멘션
        //     await message.ReplyAsync($"{collOption.Value}");
        // }

        var me = await command.FollowupAsync("파티를 생성하였습니다!", ephemeral: true);
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
        
        switch (action)
        {
            case JOIN_KEY:
                // 파티 가득 찼는지 확인
                var isFull = party.Members.Count >= party.MAX_COUNT_MEMBER;
                
                // Service에서 중복 체크 포함하여 처리
                if (await PartyService.JoinPartyAsync(messageId, partyClass.guildUser.Id, partyClass.userNickname, isFull))
                {
                    if (isFull)
                    {
                        message = "파티 인원이 가득 찼습니다. 대기 인원으로 등록되었습니다.";
                    }
                    else
                    {
                        message = $"✅ {party.DISPLAY_NAME} 파티에 참가했습니다!";
                    }
                    
                    // 파티 정보 갱신
                    party.Members = await PartyService.GetPartyMemberListAsync(messageId);
                    party.WaitMembers = await PartyService.GetPartyWaitMemberListAsync(messageId);
                }
                else
                {
                    // 실패 (이미 참가했거나 오류)
                    await component.RespondAsync("파티에 참가할 수 없습니다. (이미 참가했거나 오류 발생)", ephemeral: true);
                    return;
                }
                break;
                
            case LEAVE_KEY:
                if (await PartyService.LeavePartyAsync(messageId, partyClass.userId))
                {
                    message = $"❌ {party.DISPLAY_NAME} 파티에서 나갔습니다.";
                    
                    // 파티 정보 갱신
                    party.Members = await PartyService.GetPartyMemberListAsync(messageId);
                    party.WaitMembers = await PartyService.GetPartyWaitMemberListAsync(messageId);
                }
                else
                {
                    await component.RespondAsync("파티에 참가하지 않았거나 나가기에 실패했습니다.", ephemeral: true);
                    return;
                }
                break;
            case OPTION_KEY:
                if (partyClass.isNone)
                {
                    await component.RespondAsync("권한이 없어 표시할 기능이 없습니다.", ephemeral: true);
                    await RespondMessageWithExpire(component, time: 5);
                    return;
                }
                
                await component.RespondAsync("불러오는 중...", ephemeral: true); 
                
                // 옵션 버튼들 만들기
                var componentBuilder = new ComponentBuilder();

                if (party.Members.Count >= 1)
                {
                    componentBuilder.WithButton(PING_KEY, $"party_{OPTION_BUTTON_KEY}_{messageId}_{PING_KEY}", ButtonStyle.Success);
                    if (partyClass.isAdmin || partyClass.isOwner)
                    {
                        componentBuilder.WithButton(KICK_KEY,$"party_{OPTION_BUTTON_KEY}_{messageId}_{KICK_KEY}", ButtonStyle.Success);
                    }
                }

                if (partyClass.isAdmin || partyClass.isOwner)
                {
                    componentBuilder.WithButton(PARTY_KEY,$"party_{OPTION_BUTTON_KEY}_{messageId}_{PARTY_KEY}", ButtonStyle.Primary);
                }
                
                componentBuilder.WithButton(party.IS_CLOSED ? "재개" : CLOSE_KEY, $"party_{OPTION_BUTTON_KEY}_{messageId}_{CLOSE_KEY}", party.IS_CLOSED ? ButtonStyle.Success : ButtonStyle.Danger);
                componentBuilder.WithButton(EXPIRE_KEY, $"party_{OPTION_BUTTON_KEY}_{messageId}_{EXPIRE_KEY}", ButtonStyle.Secondary);
                
                
                await component.ModifyOriginalResponseAsync( m =>
                {
                    m.Content = "버튼을 선택해주세요.";
                    m.Components = componentBuilder.Build();
                });

                await RespondMessageWithExpire(component, time: 30);
                return;
            case OPTION_BUTTON_KEY:

                if (parts[3] != PARTY_KEY)
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
                    case CLOSE_KEY:
                        var closed = party.IS_CLOSED;
                        var e = party.IS_CLOSED ? "오픈" : "마감";
                        
                        if (partyClass is { isOwner: false, isAdmin: false })
                        {
                            await component.ModifyOriginalResponseAsync(msg =>
                            {
                                msg.Content = $"❌ 파티를 생성한 사람만 {e}할 수 있습니다.";
                            });

                            await RespondMessageWithExpire(component);
                            return;
                        }
                        
                        if (!await PartyService.SetPartyCloseAsync(messageId, !closed))
                        {
                            await component.ModifyOriginalResponseAsync(msg =>
                            {
                                msg.Content = "❌ 파티 조작에 실패하였습니다.";
                            });
                            
                            await RespondMessageWithExpire(component);
                            return;   
                        }

                        // 성공 메시지로 업데이트
                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = $"✅ 파티를 {e}했습니다.";
                        });
                        
                        await RespondMessageWithExpire(component);
                        

                        party.IS_CLOSED = !closed;
                        message = $"{partyClass.userRoleString}님이 {party.DISPLAY_NAME} 파티를 {e}하였습니다.";
                        isAllMessage = true;
                        break;
                    case PING_KEY:
                        if (!partyClass.isOwner && !partyClass.isAdmin && !partyClass.isPartyMember)
                        {
                            await component.ModifyOriginalResponseAsync(msg =>
                            {
                                msg.Content = "❌ 관리자, 파티원, 파티장만 호출할 수 있습니다!";
                            });
                            
                            await RespondMessageWithExpire(component);
                            return;
                        }
                        
                        // 성공 메시지로 업데이트
                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = "✅ 파티원을 호출했습니다.";
                        });
                        
                        await RespondMessageWithExpire(component);
                        
                        // 파티원 전체 멘션
                        var mentions = string.Join(" ", party.Members.Select(m => $"<@{m.USER_ID}>"));
                        isAllMessage = true;
                        message = $"🔔 {partyClass.userRoleString}님이 파티원을 호출하였습니다!\n{mentions}";
                        break;
                    case EXPIRE_KEY:
                        // 권한 확인: 파티장 또는 관리자만
                        if (!partyClass.isOwner && !partyClass.isAdmin)
                        {
                            await component.ModifyOriginalResponseAsync(msg =>
                            {
                                msg.Content = "❌ 파티장 또는 관리자만 만료시킬 수 있습니다.";
                            });
                            
                            await RespondMessageWithExpire(component);
                            return;
                        }
                        
                        // 확인 버튼 생성
                        var confirmComponent = new ComponentBuilder()
                            .WithButton("예", $"party_{EXPIRE_BUTTON_KEY}_{messageId}_{YES_BUTTON_KEY}", ButtonStyle.Danger)
                            .WithButton("아니오", $"party_{EXPIRE_BUTTON_KEY}_{messageId}_{NO_BUTTON_KEY}", ButtonStyle.Secondary)
                            .Build();
                        
                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = $"⚠️ **{party.DISPLAY_NAME}** 파티를 영구적으로 만료시키시겠습니까?\n만료된 파티는 복구할 수 없습니다.";
                            msg.Components = confirmComponent;
                        });
                        _ = RespondMessageWithExpire(component, time: 30);
                        return;
                    case PARTY_KEY:
                        // Modal로 인원 수 입력받기
                        var renameModal = new ModalBuilder()
                            .WithTitle("파티 설정 변경")
                            .WithCustomId($"party_{SETTING_MODEL_KEY}_{messageId}")
                            .AddTextInput("이름", "name", TextInputStyle.Short, 
                                placeholder: $"여기에 이름 입력", 
                                required: true,
                                value: party.DISPLAY_NAME,
                                minLength: 1,
                                maxLength: MAX_NAME_COUNT)
                            .AddTextInput("새로운 인원 수", "count", TextInputStyle.Short, 
                                placeholder: $"{1}-{MAX_COUNT}", 
                                required: true,
                                value: party.MAX_COUNT_MEMBER.ToString(),
                                minLength: 1,
                                maxLength: 3)
                            .Build();

                        // await component.DeleteOriginalResponseAsync();
                        await component.RespondWithModalAsync(renameModal);
                        return;
                    case KICK_KEY:
                        var builder = new ComponentBuilder();
                        
                        foreach (var entity in party.Members)
                        {
                            builder.WithButton($"{entity.USER_NICKNAME}",
                                $"party_{KICK_BUTTON_KEY}_{messageId}_{entity.USER_ID}");
                        }
                        
                        foreach (var entity in party.WaitMembers)
                        {
                            builder.WithButton($"{entity.USER_NICKNAME}",
                                $"party_{KICK_BUTTON_KEY}_{messageId}_{entity.USER_ID}");
                        }
                        
                        await component.ModifyOriginalResponseAsync(msg =>
                        {
                            msg.Content = $"추방할 맴버를 선택하세요";
                            msg.Components = builder.Build();
                        });
                        _ = RespondMessageWithExpire(component, time: 30);
                        
                        return;
                }
                break;
            case EXPIRE_BUTTON_KEY:
                
                if (parts[3] == YES_BUTTON_KEY)
                {
                    if (await ExpirePartyAsync(party, component.Channel))
                    {
                        await component.UpdateAsync(msg =>
                        {
                            msg.Content = $"✅ **{party.DISPLAY_NAME}** 파티를 만료시켰습니다.";
                            msg.Components = null;
                        });
                        
                        _ = RespondMessageWithExpire(component);
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
                        
                        _ = RespondMessageWithExpire(component);
                    }
                }
                else
                {
                    await component.UpdateAsync(msg =>
                    {
                        msg.Content = "❌ 만료가 취소되었습니다.";
                        msg.Components = null;
                    });
                    
                    _ = RespondMessageWithExpire(component);
                    return;
                }
                break;
            case KICK_BUTTON_KEY:
                await component.DeferAsync();
                
                var id = parts[3];
                var targetUserId = ulong.Parse(id);
                var result = "";
                
                if (await PartyService.KickMemberAsync(messageId, targetUserId))
                {
                    var user = _client.GetGuild(party.GUILD_KEY).GetUser(targetUserId);

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
                    party.Members = await PartyService.GetPartyMemberListAsync(messageId);
                    party.WaitMembers = await PartyService.GetPartyWaitMemberListAsync(messageId);
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
                _ = RespondMessageWithExpire(component, time: 30);
                await UpdateMessage(component, party, isAllMessage, message);
                return;
        }
        
        await UpdateMessage(component, party, isAllMessage, message);
        await RespondMessageWithExpire(component, message: message);
    }

    private async Task UpdateMessage(SocketInteraction component, PartyEntity party, bool isAllMessage, string message)
    {
        // 임베드 메시지 업데이트
        var updatedEmbed = UpdatedEmbed(party);
        var updatedComponent = UpdatedComponent(party);
        
        var originalMessage = await component.Channel.GetMessageAsync(party.MESSAGE_KEY) as IUserMessage;
        if (originalMessage == null)
        {
            if (await _client.GetChannelAsync(party.CHANNEL_KEY) is IMessageChannel cl)
            {
                originalMessage = await cl.GetMessageAsync(party.MESSAGE_KEY) as IUserMessage;
            }
        }

        // 원본 메시지 수정
        if (originalMessage != null)
        {
            
            await originalMessage.ModifyAsync(msg =>
            {
                msg.Embed = updatedEmbed;
                msg.Components = updatedComponent;
            });

            if (isAllMessage)
            {
                if (!component.HasResponded)
                {
                    await component.DeferAsync();
                }
                await originalMessage.ReplyAsync(message);
            }
        }
        else
        {
            await component.Channel.SendMessageAsync($"{party.DISPLAY_NAME} 파티에 대한 원본 메세지를 찾을 수 없습니다. 파티를 해산합니다.");
            await PartyService.ExpirePartyAsync(party.MESSAGE_KEY);
        }
    }

    private async Task HandleModalAsync(SocketModal modal)
    {
        var customId = modal.Data.CustomId;
        
        var parts = customId.Split('_');
        if (parts[0] != "party")
            return;
        
        if (!ulong.TryParse(parts[2], out var messageId))
            return;

        var partyEntity = await PartyService.GetPartyEntityAsync(messageId);

        var partyClass = new PartyClass();
        partyClass.Init(partyEntity, modal);
        var party = partyClass.Entity;

        var message = "";

        switch (parts[1])
        {
            case SETTING_MODEL_KEY:
                await modal.RespondAsync("작업 중....", ephemeral: true);
                
                // 입력값 가져오기
                var countInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "count");
                int newCount = party.MAX_COUNT_MEMBER;
                if (countInput == null || !int.TryParse(countInput.Value, out newCount))
                {
                    message += $"인원 오류: 유호한 숫자를 입력해주세요.\n";
                }

                if (party.MAX_COUNT_MEMBER != newCount)
                {
                    // 범위 체크
                    if (newCount < 1 || newCount > MAX_COUNT)
                    {
                        message += $"인원 오류: 파티 인원은 {1}~{MAX_COUNT} 사이여야 합니다.\n";
                    }

                    if (partyClass is { isOwner: false, isAdmin: false })
                    {
                        message += $"인원 오류: 파티장 또는 관리자만 인원을 변경할 수 있습니다.\n";
                    }

                    var (members, waitMember) = await PartyService.ResizePartyAsync(messageId, newCount);

                    party.Members = members;
                    party.WaitMembers = waitMember;
                    party.MAX_COUNT_MEMBER = newCount;
                    message += $"인원: 인원을 변경하였습니다.\n";
                }
                
                var nameInput = modal.Data.Components.FirstOrDefault(c => c.CustomId == "name");
                var name = nameInput?.Value ?? "";
                if (string.IsNullOrEmpty(name))
                {
                    break;
                }

                if (name != party.DISPLAY_NAME)
                {
                    if (await PartyService.PartyRename(messageId, name))
                    {
                        message += "제목: 제목을 변경하였습니다.\n";
                        party.DISPLAY_NAME = name;
                    }
                    else
                    {
                        message += "제목 오류: 제목을 변경할 수 없었습니다.\n";
                    }
                }

                break;
        }

        if (message == "")
        {
            message = "설정이 취소되었습니다.";
        }
        await modal.ModifyOriginalResponseAsync(m => m.Content = message);
        _ = RespondMessageWithExpire(modal);
        
        await UpdateMessage(modal, party, false, "");
    }

    private static async Task RespondMessageWithExpire(SocketInteraction component, int time = 10, string? message = null)
    {
        var separator = "\u200B"; // Zero-Width Space
        var exMessage = $"{separator} (해당 메세지는 {time}초 후 삭제됩니다.)";
        
        if (message != null)
        {
            // HasResponded 체크 - 이미 응답했는지 확인
            if (!component.HasResponded)
            {
                await component.RespondAsync(message + exMessage, ephemeral: true);
            }
            else
            {
                await component.ModifyOriginalResponseAsync(m =>
                {
                    m.Content = message + exMessage;
                });
            }
        }
        else
        {
            message = (await component.GetOriginalResponseAsync()).Content;
            await component.ModifyOriginalResponseAsync(m =>
            {
                m.Content = message + exMessage;
            });
        }
        
        // 백그라운드에서 삭제
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(time));

            var old = (await component.GetOriginalResponseAsync()).Content;
            var s = old.Split(separator)[0];
            if (s != message)
            {
                return;
            }
            try
            {
                await component.DeleteOriginalResponseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RespondMessageWithExpire] 삭제 실패: {ex.Message}");
            }
        });
    }

    private async Task InitCommands()
    {
        var commands = await _client.GetGlobalApplicationCommandsAsync();

        var array = new[]
        {
            new SlashCommandBuilder()
                .WithName("파티")
                .WithDescription($"파티를 생성합니다. 허용 인원은 {MIN_COUNT}-{MAX_COUNT} 입니다.")
                .AddOption("이름", ApplicationCommandOptionType.String, "파티 이름", isRequired: true, minLength: 1, maxLength: MAX_NAME_COUNT)
                .AddOption("인원", ApplicationCommandOptionType.Integer, "파티 인원", isRequired: true)
                // .AddOption("호출", ApplicationCommandOptionType.Role, "해당 역할 소유자에게 알람을 보냅니다", isRequired: false)
                .AddOption("만료시간", ApplicationCommandOptionType.String, $"파티 만료 시간 ex(15m, 15h, 15분, 15시) 빈 필드: {MAX_HOUR}시간", isRequired: false)
        };
        
        // 내용이 다르거나 없는 명령어 생성/업데이트
        foreach (var commandBuilder in array)
        {
            var built = commandBuilder.Build();
            var existing = commands.FirstOrDefault(c => c.Name == built.Name.Value);
            
            if (existing == null || !CommandEquals(existing, built))
            {
                if (existing != null)
                {
                    await existing.DeleteAsync();
                }
                await _client.CreateGlobalApplicationCommandAsync(built);
            }
        }
        
        // array에 없는 명령어 삭제
        foreach (var socketApplicationCommand in commands.Where(c => !array.Any(f => f.Name == c.Name)))
        {
            await socketApplicationCommand.DeleteAsync();
        }
    }
    
    private bool CommandEquals(SocketApplicationCommand existing, SlashCommandProperties built)
    {
        // Description 비교
        if (existing.Description != built.Description.Value) return false;
        
        // Options 개수 비교
        var builtOptionsCount = built.Options.IsSpecified ? built.Options.Value.Count : 0;
        if (existing.Options.Count != builtOptionsCount) return false;
        
        // Options가 없으면 true
        if (!built.Options.IsSpecified) return existing.Options.Count == 0;
        
        var existingOptions = existing.Options.ToList();
        var builtOptions = built.Options.Value.ToList();
        
        for (int i = 0; i < existingOptions.Count; i++)
        {
            var e = existingOptions[i];
            var b = builtOptions[i];
            
            if (e.Name != b.Name || e.Type != b.Type || 
                e.Description != b.Description)
                return false;
        }
        
        return true;
    }
    
    private Embed UpdatedEmbed(PartyEntity party)
    {
        var memberList = party.Members.Count > 0 
            ? string.Join("\n", party.Members.Select(info => $"**<@{info.USER_ID}>**"))
            : "없음";

        string state;
        if (party.IS_EXPIRED)
            state = " (만료)";
        else if (party.IS_CLOSED)
            state = " (일시정지)";
        else
            state = "";
        
        var title = $"**{party.DISPLAY_NAME}** [생성자: {party.OWNER_NICKNAME}]{state}";
        var description = $"**참가자: {party.Members.Count}/{party.MAX_COUNT_MEMBER}**\n\n{memberList}";
        if (party.WaitMembers.Count > 0)
        {
            description += $"\n====================\n**대기열: {party.WaitMembers.Count}\n**";

            var array = party.WaitMembers;
            for (var i = 0; i < array.Count; i++)
            {
                var member = array[i];
                description += $"\n순번: {i + 1} | 닉네임: <@{member.USER_ID}>";
            }
        }
        
        // 만료시간 추가 (강조 표시)
        description += $"\n\n\n**만료시간: {party.EXPIRE_DATE:yyyy/MM/dd tt h:mm}**";
        
        var color = Color.Blue;
        if (party.MAX_COUNT_MEMBER == party.Members.Count) color = Color.Green;
        if (party.IS_CLOSED) color = Color.Orange;
        if (party.IS_EXPIRED) color = Color.Red;
        
        var updatedEmbed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color)
            .WithFooter($"버그제보(Discord): ojh1158 Version: {VERSION}")
            .WithCurrentTimestamp()
            .Build();
        
        return updatedEmbed;
    }

    private MessageComponent UpdatedComponent(PartyEntity party)
    {
        var partyKey = party.MESSAGE_KEY;

        var component = new ComponentBuilder();
        var maxFlag = party.MAX_COUNT_MEMBER <= party.Members.Count;

        if (party.IS_EXPIRED) return component.Build();

        if (!party.IS_CLOSED)
        {
            // 인원이 가득 찬 경우
            if (maxFlag)
            {
                component.WithButton("대기하기", $"party_{JOIN_KEY}_{partyKey}");
            }
            else
            {
                component.WithButton(JOIN_KEY, $"party_{JOIN_KEY}_{partyKey}", ButtonStyle.Success);
            }
        }

        component.WithButton(LEAVE_KEY, $"party_{LEAVE_KEY}_{partyKey}", ButtonStyle.Danger);

        component.WithButton(OPTION_KEY, $"party_{OPTION_KEY}_{partyKey}", ButtonStyle.Secondary);
        
        return component.Build();
    }
    
    private async Task<bool> ExpirePartyAsync(PartyEntity party, ISocketMessageChannel? channel = null)
    {
        channel ??= await _client.GetChannelAsync(party.CHANNEL_KEY) as ISocketMessageChannel;

        if (channel == null) return false;
        
        var result = await PartyService.ExpirePartyAsync(party.MESSAGE_KEY);

        if (!result) return false;
        
        party.IS_EXPIRED = true;
        
        var embed = UpdatedEmbed(party);
            
        await channel!.ModifyMessageAsync(party.MESSAGE_KEY, msg =>
        {
            msg.Embed = embed;
            msg.Components = null;
        });
        
        return true;
    }

}