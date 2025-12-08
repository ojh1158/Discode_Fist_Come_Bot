using Discord.WebSocket;
using DiscordBot.scripts.db.Models;

namespace DiscordBot.scripts._src.Partys;

public class PartyClass
{
    public PartyEntity Entity { get; private set; } = null!;

    public SocketGuildUser guildUser = null!;
    
    public ulong userId;
    public bool isOwner;
    public bool isAdmin;
    public bool isWater;
    public bool isPartyMember;
    public bool isNone;
    public string userNickname;
    public string userRoleString;

    public string Init(PartyEntity? partyEntity, SocketInteraction  interaction)
    {
        if (partyEntity == null)
        {
            return "파티를 찾을 수 없습니다.";
        }
        
        Entity = partyEntity;
        

        userId = interaction.User.Id;
        isOwner = partyEntity.OWNER_KEY == userId;
        if (interaction.User is not SocketGuildUser user)
        {
            return "길드 채팅에서만 사용할 수 있습니다.";
        }
        
        guildUser = user;
        isAdmin = user.GuildPermissions is { Administrator: true };
        isWater = Entity.WaitMembers.Any(x => x.USER_ID == userId);
        isPartyMember = Entity.Members.Any(x => x.USER_ID == userId);
        isNone = !isAdmin && !isWater && !isPartyMember && !isOwner;
        
        userNickname = string.IsNullOrEmpty(user.Nickname) ? user.Username : user.Nickname;
        
        
        userRoleString = "일반";

        if (isWater)
            userRoleString = "대기자";
        if (isPartyMember)
            userRoleString = "파티원";
        if (isAdmin)
            userRoleString = "관리자";
        if (isOwner)
            userRoleString = "파티장";

        userRoleString += $"({userNickname})";
        
        return "";
    }
}