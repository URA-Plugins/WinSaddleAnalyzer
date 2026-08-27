# WinSaddleAnalyzer

殿堂马表格保留四列、对齐与表头排序，数据行不显示列间竖线。右键任一数据单元格可将该行设置为种马分析的 `TrainedCharaId`。

配置中的“要养的马的 CharaId”和“另一个种马的 TrainedCharaId”只接受 `0-9`；空输入保存为 `0`。

殿堂马、好友和单独相性分析按响应组替换 Workspace 中已有的插件面板；完整好友响应同组显示“好友”和“相性分析”。

SkillEffectPlugin 未配置或收益表尚未生成时，相性分析省略技能期望收益，其余结果正常显示。
