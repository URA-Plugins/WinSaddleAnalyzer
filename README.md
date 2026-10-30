# WinSaddleAnalyzer

殿堂马表格保留四列、对齐与表头排序，数据行不显示列间竖线。右键任一数据单元格可将该行设置为种马分析的 `TrainedCharaId`。

配置中的“要养的马的 CharaId”和“另一个种马的 TrainedCharaId”只接受 `0-9`；空输入保存为 `0`。

殿堂马、好友和单独相性分析按响应组替换 Workspace 中已有的插件面板；完整好友响应同组显示“好友”和“相性分析”。

SkillEffectPlugin 未配置或收益表尚未生成时，相性分析省略技能期望收益，其余结果正常显示。

好友分析支持日服数组协议和繁中服单种马协议：完整好友读取 `partner_chara_info_array` 或 `practice_partner_info`，简略好友读取 `user_trained_chara_array` 或 `user_trained_chara`。日服数组取第一个代表；没有代表马时保留已有面板。

## 构建

```powershell
dotnet build .\WinSaddleAnalyzer.csproj -c Release -m:1 -p:RuntimeIdentifier=win-x64 -p:SelfContained=false -p:PlatformTarget=AnyCPU -p:DeployUraPluginToLocalAppDataOnBuild=false
```

Host-dependent smoke 位于 `tests/WinSaddleAnalyzerSmoke`。

默认 smoke 执行本仓发布边界、闲置生命周期、好友与殿堂马 framebuffer、配置和持久化检查，并实际加载本轮 Release ZIP 验证好友协议、相性流程及程序集共享。测试工具源码由 `tests/Support/` 持有。

先完成上面的 Release 构建，再指定与编译 SDK 共享依赖版本一致的宿主源码运行测试。SDK `2026.10.31.1` 使用 Terminal.Gui `2.5.0`、MessagePack `3.1.12`。

```powershell
$testHostRoot = 'C:\path\to\matching-host'
$env:URA_TEST_PLUGIN_ROOT = (Get-Location).Path
$env:URA_PLUGIN_SMOKE_PACKAGE_ROOT = Join-Path (Get-Location).Path 'bin\Release'
dotnet run --project .\tests\WinSaddleAnalyzerSmoke -c Release -p:UraTestHostRoot="$testHostRoot" -p:GenerateUraPluginManifestOnBuild=false -p:PackageUraPluginOnBuild=false -p:DeployUraPluginToLocalAppDataOnBuild=false
```

协议样本来源和构造方式见 [Fixtures](tests/WinSaddleAnalyzerSmoke/Fixtures/README.md)。

## 验证与发布

在 Windows 仓库根执行 `act workflow_dispatch --artifact-server-path "$env:TEMP/ura-act-artifacts"`。本地与 GitHub 使用同一份 workflow；版本 tag 触发 GitHub Release 发布。环境要求、共用 workflow 本地映射和发布规则见 [URA plugin workflows](https://github.com/URA-Plugins/.github/blob/v1/README.md)。
