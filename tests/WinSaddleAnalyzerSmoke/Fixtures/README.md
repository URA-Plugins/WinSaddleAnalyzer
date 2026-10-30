# 好友协议样本

两个 JSON 是按 2026-10-30 Komoe 繁中客户端 Hachimi `protos.json` 构造的最小响应，姓名、账号和种马 ID 均为测试数据，不是真实响应捕获。

`FriendSearchResponse.CommonResponse.practice_partner_info` 的类型为 `Gallop.TrainedChara`；`UserInfoAtFriend.user_trained_chara` 的类型为 `Gallop.UserTrainedCharaAtFriend`。日服测试将这两个单值字段替换为对应的数组字段，并加入不同的后续代表，验证首项选择。

测试直接把 JSON 编码为 MessagePack，经宿主实际加载的插件注册分派，不依赖本机游戏路径。
