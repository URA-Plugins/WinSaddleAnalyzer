using System.Text.Json;

namespace WinSaddleAnalyzer;

internal static class SkillEffectFileStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };

    public static Dictionary<string, double>? LoadIfAvailable()
    {
        var dataDirectory = Path.Combine("PluginData", "SkillEffectPlugin");
        var settingsPath = Path.Combine(dataDirectory, "settings.json");
        if (!File.Exists(settingsPath))
            return null;

        var settings = JsonSerializer.Deserialize<SkillEffectSettings>(File.ReadAllText(settingsPath), JsonOptions)
            ?? throw new InvalidDataException($"SkillEffectPlugin 配置文件反序列化结果为空: {settingsPath}");

        if (string.IsNullOrWhiteSpace(settings.Race) || string.IsNullOrWhiteSpace(settings.RunningStyle))
            return null;

        var effectPath = Path.Combine(dataDirectory, settings.Race, $"{settings.RunningStyle}.json");
        if (!File.Exists(effectPath))
            return null;

        var items = JsonSerializer.Deserialize<SkillEffectFileItem[]>(File.ReadAllText(effectPath), JsonOptions)
            ?? throw new InvalidDataException($"SkillEffectPlugin 技能收益表反序列化结果为空: {effectPath}");

        var effects = new Dictionary<string, double>();
        foreach (var (item, index) in items.Select((item, index) => (item, index)))
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new InvalidDataException($"SkillEffectPlugin 技能收益表第 {index} 条缺少 Name: {effectPath}");

            if (string.IsNullOrWhiteSpace(item.Effect) || item.Effect.Length < 4)
                throw new InvalidDataException($"SkillEffectPlugin 技能收益表第 {index} 条 Effect 格式无效: {effectPath}");

            if (!double.TryParse(item.Effect[..4], out var effect))
                throw new InvalidDataException($"SkillEffectPlugin 技能收益表第 {index} 条 Effect 无法解析为数字: {effectPath}");

            effects.TryAdd(item.Name, effect);
        }

        if (effects.Count == 0)
            throw new InvalidDataException($"SkillEffectPlugin 技能收益表没有可用条目: {effectPath}");

        return effects;
    }

    sealed record SkillEffectSettings(
        int DisplayOrder,
        double MinimumExpectedEffect,
        string Race,
        string RunningStyle,
        string URACloudBaseUrl,
        bool AutoUpdateSkillEffects);

    sealed record SkillEffectFileItem(string Name, string Effect);
}
