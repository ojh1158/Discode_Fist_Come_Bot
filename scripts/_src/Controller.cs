using System.Security.Cryptography;
using DiscodeBot.scripts.db;
using DiscodeBot.scripts.db.Models;
using DiscodeBot.scripts.db.Repositories;
using Discord;
using Discord.WebSocket;

namespace DiscodeBot.scripts._src;

public class Controller
{
    private readonly DiscordSocketClient _client;
    
    // private Dictionary<string, Party> _partyTable = new();
    
    private const int MIN_COUNT = 1;
    private const int MAX_COUNT = 200;
    private const int MAX_HOUR = 24;
    private const string VERSION = "1.0.6";

    private const string JOIN_KEY = "참가";
    private const string LEAVE_KEY = "나가기";
    private const string CLOSE_KEY = "일시정지";
    private const string EXPIRE_KEY = "만료(영구)";
    private const string PING_KEY = "호출(파티원)";

    private const string EXPIRE_BUTTEN_KEY = "expire";
    
    private const string YES_BUTTEN_KEY = "yes";
    private const string NO_BUTTEN_KEY = "no";
    
    
    public Controller(DiscordSocketClient client)
    {
        _client = client;
    }
    public void Init()
    {
        _client.SlashCommandExecuted += HandleSlashCommandAsync;
        _client.ButtonExecuted += HandleButtonAsync;
        _client.Ready += InitCommands;
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
            
            if (await GuildRepository.GuildCheck(guildChannel.Id, guildChannel.Name))
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
        
        if (await PartyRepository.IsPartyExistsAsync(partyName, (ulong)command.GuildId!))
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
        
        var party = new PartyEntity
        {
            DISPLAY_NAME = partyName,
            MAX_COUNT_MEMBER = count,
            // MESSAGE_KEY = ,
            GUILD_KEY = (ulong)command.GuildId!,
            CHANNEL_KEY = (ulong)command.ChannelId!,
            OWNER_KEY = command.User.Id,
            OWNER_NICKNAME = command.User is SocketGuildUser user
                ? string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname
                : command.User.Username,
            EXPIRE_DATE = DateTime.Now + time
        };
        
        var updatedEmbed = UpdatedEmbed(party);
        var component = UpdatedComponent(party);

        await command.RespondAsync(embed: updatedEmbed, components: component);
        var message = await command.GetOriginalResponseAsync();

        party.MESSAGE_KEY = message.Id;
        
        if (!await PartyRepository.CreatePartyAsync(party))
        {
            await command.FollowupAsync("파티 생성에 실패하였습니다.", ephemeral: true);
            return;
        }
        
        await command.FollowupAsync("파티를 생성하였습니다!", ephemeral: true);
    }
    
    private async Task HandleButtonAsync(SocketMessageComponent component)
    {
        var customId = component.Data.CustomId;
        
        // CustomId 파싱: "party_join_{partyId}" 또는 "party_leave_{partyId}"
        var parts = customId.Split('_');
        if (parts.Length < 4 || parts[0] != "party")
            return;
        
        var action = parts[1]; // "join", "leave", "expire" 등

        var messageId = ulong.Parse(parts[2]);

        var isAllMessage = false;
        var message = "알 수 없는 오류가 나타났습니다.";

        var party = await PartyRepository.GetPartyEntity(messageId);
        
        // // 파티 정보 가져오기
        if (party == null)  
        {
            await component.RespondAsync("파티를 찾을 수 없습니다.", ephemeral: true);
            return;
        }

        var userId = component.User.Id;
        var isOwner = party.OWNER_KEY == userId;
        if (component.User is not SocketGuildUser guildUser)
        {
            return;
        }
        
        var isAdmin = guildUser.GuildPermissions is { Administrator: true };
        var isWater = party.WaitMembers.Any(x => x.USER_ID == userId);
        var isPartyMember = party.Members.Any(x => x.USER_ID == userId);
        var userNickname = string.IsNullOrEmpty(guildUser.Nickname) ? guildUser.Username : guildUser.Nickname;
        
        var userRoleString = "일반";

        if (isWater)
            userRoleString = "대기자";
        if (isPartyMember)
            userRoleString = "파티원";
        if (isAdmin)
            userRoleString = "관리자";
        if (isOwner)
            userRoleString = "파티장";

        userRoleString += $"({userNickname})";
        
        switch (action)
        {
            // 이미 참가했는지 확인
            case JOIN_KEY when await PartyRepository.ExistsUser(party.MESSAGE_KEY, guildUser.Id):
                await component.RespondAsync("이미 파티에 참가하셨습니다.", ephemeral: true);
                return;
            // 인원 초과 확인
            case JOIN_KEY when party.Members.Count >= party.MAX_COUNT_MEMBER:
                if (await PartyRepository.AddUser(party.MESSAGE_KEY, guildUser.Id, userNickname, true))
                {
                    message = "파티 인원이 가득 찼습니다. 대기 인원으로 등록되었습니다.";
                }
                else
                {
                    message = "파티에 들어갈 수 없었습니다! 다시 시도해주세요.";
                }
                break;
            // 참가자 추가
            case JOIN_KEY:
                if (await PartyRepository.AddUser(party.MESSAGE_KEY, guildUser.Id, userNickname, false))
                {
                    message = $"✅ {party.DISPLAY_NAME} 파티에 참가했습니다!";
                }
                else
                {
                    message = "파티에 들어갈 수 없었습니다! 다시 시도해주세요.";
                }
                break;
            // 참가 여부 확인
            case LEAVE_KEY when !await PartyRepository.ExistsUser(party.MESSAGE_KEY, guildUser.Id):
                await component.RespondAsync("파티에 참가하지 않았습니다.", ephemeral: true);
                return;
            // 참가자 제거
            case LEAVE_KEY:
                if (await PartyRepository.RemoveUser(messageId, userId) && await PartyRepository.UpdateParty(messageId))
                {
                    message = $"❌ {party.DISPLAY_NAME} 파티에서 나갔습니다.";
                }
                else
                {
                    message = $"파티에서 나가기에 실패하엿습니다. 다시 시도해주세요.";
                }
                break;
            case CLOSE_KEY:
                var closed = party.IS_CLOSED;
                var e = party.IS_CLOSED ? "오픈" : "마감";
                
                if (!isOwner && !isAdmin)
                {
                    await component.RespondAsync($"파티를 생성한 사람만 {e}할 수 있습니다.", ephemeral: true);
                    return;
                }
                
                if (!await PartyRepository.SetPartyClose(messageId, !closed))
                {
                    await component.RespondAsync($"파티 조작에 실패하였습니다.", ephemeral: true);
                    return;   
                }

                message = $"{userRoleString}님이 {party.DISPLAY_NAME} 파티를 {e}하였습니다.";
                isAllMessage = true;
                break;
            case PING_KEY:
                if (!isOwner && !isAdmin && !isPartyMember)
                {
                    await component.RespondAsync("관리자, 파티원, 파티장만 호출할 수 있습니다!", ephemeral: true);
                    return;
                }
                
                // 파티원 전체 멘션
                var mentions = string.Join(" ", party.Members.Select(m => $"<@{m.USER_ID}>"));
                isAllMessage = true;
                message = $"🔔 {userRoleString}님이 파티원을 호출하였습니다!\n{mentions}";
                break;
            case EXPIRE_KEY:
                // 권한 확인: 파티장 또는 관리자만
                if (!isOwner && !isAdmin)
                {
                    await component.RespondAsync("파티장 또는 관리자만 만료시킬 수 있습니다.", ephemeral: true);
                    return;
                }
                
                // 확인 버튼 생성
                var confirmComponent = new ComponentBuilder()
                    .WithButton("예", $"party_{EXPIRE_BUTTEN_KEY}_{messageId}_{YES_BUTTEN_KEY}", ButtonStyle.Danger)
                    .WithButton("아니오", $"party_{EXPIRE_BUTTEN_KEY}_{messageId}_{NO_BUTTEN_KEY}", ButtonStyle.Secondary)
                    .Build();
                
                await component.RespondAsync(
                    $"⚠️ **{party.DISPLAY_NAME}** 파티를 영구적으로 만료시키시겠습니까?\n" +
                    "만료된 파티는 복구할 수 없습니다.", 
                    components: confirmComponent, 
                    ephemeral: true);
                return;
            
            case EXPIRE_BUTTEN_KEY:
                
                if (parts[3] == YES_BUTTEN_KEY)
                {
                    await ExpirePartyAsync(party);
                    await component.UpdateAsync(msg =>
                    {
                        msg.Content = $"✅ **{party.DISPLAY_NAME}** 파티를 만료시켰습니다.";
                        msg.Components = null;
                    });
                    message = $"❌ {userRoleString}님이 파티를 만료시켰습니다.";
                    isAllMessage = true;
                }
                else
                {
                    await component.UpdateAsync(msg =>
                    {
                        msg.Content = "❌ 만료가 취소되었습니다.";
                        msg.Components = null;
                    });
                    return;
                }
                break;
        }
        
        // 임베드 메시지 업데이트
        var updatedEmbed = UpdatedEmbed(party);
        var updatedComponent = UpdatedComponent(party);
        
        var originalMessage = await component.Channel.GetMessageAsync(party.MESSAGE_KEY) as IUserMessage;
        
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
            else
            {
                await component.RespondAsync(message, ephemeral: true);
            }
        }
        else
        {
            await component.Channel.SendMessageAsync($"{party.DISPLAY_NAME} 파티에 대한 원본 메세지를 찾을 수 없습니다. 파티를 해산합니다.");
            await PartyRepository.ExpiredParty(party.MESSAGE_KEY);
        }
    }

    private async Task InitCommands()
    {
        var commands = await _client.GetGlobalApplicationCommandsAsync();

        var array = new[]
        {
            new SlashCommandBuilder()
                .WithName("파티")
                .WithDescription($"파티를 생성합니다. 허용 인원은 {MIN_COUNT}-{MAX_COUNT} 입니다.")
                .AddOption("이름", ApplicationCommandOptionType.String, "파티 이름", isRequired: true)
                .AddOption("인원", ApplicationCommandOptionType.Integer, "파티 인원", isRequired: true)
                .AddOption("만료시간", ApplicationCommandOptionType.String, $"파티 만료 시간 ex(15m, 15h, 15분, 15시) 빈 필드 :{MAX_COUNT}최대시간", isRequired: false),
        };
        
        foreach (var commandBuilder in array.Where(x => !commands.Any(f => f.Name == x.Name)))
        {
            await _client.CreateGlobalApplicationCommandAsync(commandBuilder.Build());
        }
        
        foreach (var socketApplicationCommand in commands.Where(c => !array.Any(f => f.Name == c.Name)))
        {
            await socketApplicationCommand.DeleteAsync();
        }
    }
    
    private Embed UpdatedEmbed(PartyEntity party)
    {
        var memberList = party.Members.Count > 0 
            ? string.Join("\n", party.Members.Select(info => $"**{info.USER_NICKNAME}**"))
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
                description += $"\n순번: {i + 1} | 닉네임: {member.USER_NICKNAME}";
            }
        }
        
        // 만료시간 추가 (강조 표시)
        description += $"\n\n\n**만료시간: {party.EXPIRE_DATE:yyyy/MM/dd tt hh:mm:ss}**";
        
        var color = Color.Blue;
        if (party.MAX_COUNT_MEMBER == party.Members.Count) color = Color.Green;
        if (party.IS_CLOSED) color = Color.Orange;
        if (party.IS_EXPIRED) color = Color.Red;
        
        var updatedEmbed = new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithColor(color)
            .WithFooter($"make-by-ojh1158 {VERSION}")
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
        
        if (party.Members.Count >= 1)
        {
            component.WithButton(PING_KEY, $"party_{PING_KEY}_{partyKey}", ButtonStyle.Success);
        }

        component.WithButton(party.IS_CLOSED ? "재개" : CLOSE_KEY , $"party_{CLOSE_KEY}_{partyKey}", party.IS_CLOSED ? ButtonStyle.Success : ButtonStyle.Danger);

        component.WithButton(EXPIRE_KEY,$"party_{EXPIRE_KEY}_{partyKey}", ButtonStyle.Secondary);
        
        return component.Build();
    }
    
    private async Task ExpirePartyAsync(PartyEntity party, ISocketMessageChannel? channel = null)
    {
        if (channel != null)
        {
            var embed = UpdatedEmbed(party);
            
            await channel.ModifyMessageAsync(party.MESSAGE_KEY, msg =>
            {
                msg.Embed = embed;
                msg.Components = null;
            });
        }
    }

}