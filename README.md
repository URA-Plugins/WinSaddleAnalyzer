# WinSaddleAnalyzer

殿堂马表格保留四列、对齐与表头排序，数据行不显示列间竖线。右键任一数据单元格可将该行设置为种马分析的 `TrainedCharaId`。

配置中的“要养的马的 CharaId”和“另一个种马的 TrainedCharaId”只接受 `0-9`；空输入保存为 `0`。

配置窗口从 `PluginData/WinSaddleAnalyzer/settings.json` 读取已有设置，只有保存才写入；启动前也可配置。

殿堂马、好友和单独相性分析按响应组替换 Workspace 中已有的插件面板；完整好友响应同组显示“好友”和“相性分析”。

育成开局通过 `/umamusume/single_mode*/start` 的共用 DTO 读取继承信息，包含普通育成与 Festa；每个入口只处理一次。

SkillEffectPlugin 未配置或收益表尚未生成时，相性分析省略技能期望收益，其余结果正常显示。

好友分析支持日服数组协议和繁中服单种马协议：完整好友读取 `partner_chara_info_array` 或 `practice_partner_info`，简略好友读取 `user_trained_chara_array` 或 `user_trained_chara`。日服数组取第一个代表；没有代表马时保留已有面板。

## 构建

```powershell
dotnet build .\WinSaddleAnalyzer.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
```

## 验证与发布

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。
