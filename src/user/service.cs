using DiscodeBot.src._core;
using DiscodeBot.src.user;

namespace DiscodeBot.src.user;

public class UserService
{
    private readonly UserRepository _userRepository;

    public UserService()
    {
        _userRepository = new UserRepository();
    }

    /// <summary>
    /// 유저 생성
    /// </summary>
    public async Task<bool> CreateAsync(ulong id, string name, string nickname)
    {
        // validation
        if (id == 0)
        {
            Console.WriteLine("[UserService] 유효하지 않은 ID입니다.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("[UserService] 이름은 비어있을 수 없습니다.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(nickname))
        {
            Console.WriteLine("[UserService] 닉네임은 비어있을 수 없습니다.");
            return false;
        }
        // new entity
        var userEntity = new UserEntity
        {
            ID = id,
            NAME = name,
            NICKNAME = nickname
        };
        var succes = await _userRepository.CreateAsync(userEntity);
        if (success) return userEntity
    }

    /// <summary>
    /// 유저 목록 조회 (페이징)
    /// ids가 null이면 전체 조회
    /// </summary>
    public async Task<PagedResult<UserEntity>> FindAsync(IEnumerable<ulong>? ids, int limit = 10, int offset = 0)
    {
        return await _userRepository.FindAsync(ids, limit, offset);
    }
}
