using Gallop;
using MessagePack;

namespace WinSaddleAnalyzer;

[MessagePackObject(AllowPrivate = true)]
internal sealed partial class FriendResponse
{
    [Key("data")] public FriendResponseData Data { get; set; } = null!;

    public static FriendResponseData Read(ReadOnlyMemory<byte> payload, string endpoint)
    {
        try
        {
            var data = MessagePackSerializer.Deserialize<FriendResponse>(payload)?.Data
                ?? throw new InvalidDataException($"WinSaddleAnalyzer 的 {endpoint} 好友响应缺少 data。");
            if (data.UserInfo is null)
                throw new InvalidDataException($"WinSaddleAnalyzer 的 {endpoint} 好友响应缺少 data.user_info_summary。");
            return data;
        }
        catch (MessagePackSerializationException ex)
        {
            throw new InvalidDataException($"WinSaddleAnalyzer 无法解析 {endpoint} 好友响应：{ex.Message}", ex);
        }
    }
}

[MessagePackObject(AllowPrivate = true)]
internal sealed partial class FriendResponseData
{
    [Key("user_info_summary")] public FriendUserInfo UserInfo { get; set; } = null!;
    [Key("follower_num")] public int FollowerCount { get; set; }
    [Key("partner_chara_info_array")] public TrainedChara[]? PartnerCharacters { get; set; }
    [Key("practice_partner_info")] public TrainedChara? PracticePartner { get; set; }
}

[MessagePackObject(AllowPrivate = true)]
internal sealed partial class FriendUserInfo
{
    [Key("viewer_id")] public long ViewerId { get; set; }
    [Key("name")] public string Name { get; set; } = null!;
    [Key("user_trained_chara_array")] public UserTrainedCharaAtFriend[]? TrainedCharacters { get; set; }
    [Key("user_trained_chara")] public UserTrainedCharaAtFriend? TrainedCharacter { get; set; }
}
