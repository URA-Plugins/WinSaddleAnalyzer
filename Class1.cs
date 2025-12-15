using Gallop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using static WinSaddleAnalyzer.i18n.ParseTrainedCharaLoadResponse;

namespace WinSaddleAnalyzer
{
    public partial class WinSaddleAnalyzer : IPlugin
    {
        [PluginDescription("显示殿堂马&好友种马信息")]
        public string Name => "WinSaddleAnalyzer";
        public string Author => "离披";
        public Version Version => Assembly.GetExecutingAssembly().GetName().Version ?? new();
        public string[] Targets => [];

        [PluginSetting, PluginDescription("显示顺序: 0为从老到新，1是从高胜鞍到低胜鞍，2是从高分到低分")]
        public int DisplayOrder { get; set; } = 0;
        [PluginSetting, PluginDescription("是否只显示收藏了的马")]
        public bool OnlyFavourites { get; set; } = true;

        [PluginSetting, PluginDescription("要养的马的CharaId")]
        public int TargetHorseId { get; set; } = 0;
        [PluginSetting, PluginDescription("另一个种马的TrainedCharaId")]
        public int ParentHorseId { get; set; } = 0;

        internal string PLUGIN_DATA_DIRECTORY = string.Empty;
        internal string TRAINED_CHARA_FILEPATH = string.Empty;
        internal string FACTOR_EFFECT_FILEPATH = string.Empty;
        internal TrainedChara[] TrainedChara = [];
        internal Dictionary<int, string> FactorEffects = [];
        internal Dictionary<string, double> SkillEffects = [];
        /// <summary>
        /// 指定的前提种马
        /// </summary>
        internal static TrainedChara Parent { get; set; } = default!;

        public void Initialize()
        {
            PLUGIN_DATA_DIRECTORY = Path.Combine("PluginData", Name);
            Directory.CreateDirectory(PLUGIN_DATA_DIRECTORY);
            TRAINED_CHARA_FILEPATH = Path.Combine(PLUGIN_DATA_DIRECTORY, "trained_chara.json");
            TrainedChara = File.Exists(TRAINED_CHARA_FILEPATH)
                ? JsonConvert.DeserializeObject<TrainedChara[]>(File.ReadAllText(TRAINED_CHARA_FILEPATH)) ?? []
                : [];
            FACTOR_EFFECT_FILEPATH = Path.Combine(PLUGIN_DATA_DIRECTORY, "factor_effects.br");
            var factorEffects = JArray.Parse(Encoding.UTF8.GetString(Brotli.Decompress(File.ReadAllBytes(FACTOR_EFFECT_FILEPATH))));
            foreach (var effect in factorEffects)
            {
                var id = effect["index"].ToInt();
                var text = effect["text"].ToString();
                var skill = FactorEffectRegex().Match(text).Groups[1].Value;
                if (!string.IsNullOrEmpty(skill))
                {
                    skill = skill.Replace("○", "◎");
                    FactorEffects.Add(id, skill);
                }
            }
            Parent = TrainedChara.FirstOrDefault(x => x.trained_chara_id == ParentHorseId)!;

            var sep = PluginManager.LoadedPlugins.FirstOrDefault(x => x.Name == "SkillEffectPlugin");
            if (sep != default)
            {
                var plugin = (dynamic)sep;
                var effects = (Dictionary<UmamusumeResponseAnalyzer.Entities.SkillData, double>)plugin.Effects;
                SkillEffects = effects.ToDictionary(x => x.Key.Name, x => x.Value);
            }
        }

        [Analyzer]
        public void Analyze(JObject jo)
        {
            if (!jo.ContainsKey("data")) return;
            var data = jo["data"] as JObject;
            if (data.ContainsKey("common_define") && data.ContainsKey("trained_chara"))
            {
                var obj = data["trained_chara"].ToObject<TrainedChara[]>();
                TrainedChara = obj;
                File.WriteAllText(TRAINED_CHARA_FILEPATH, JsonConvert.SerializeObject(obj));
            }
            if (data.ContainsKey("single_mode_start_common") && data["single_mode_start_common"].ContainsKey("add_trained_chara_array"))
            {
                var singleModeStartCommon = data["single_mode_start_common"];
                var charaInfo = singleModeStartCommon["chara_info"];
                TargetHorseId = int.Parse(charaInfo["card_id"].ToString()[..4]);
                var successionTrainedCharaIdDad = charaInfo["succession_trained_chara_id_1"].ToObject<int>();
                var successionTrainedCharaIdMom = charaInfo["succession_trained_chara_id_2"].ToObject<int>();
                var addTrainedCharaArray = singleModeStartCommon["add_trained_chara_array"];
                var rentalHorse = (addTrainedCharaArray.FirstOrDefault(x => x["trained_chara_id"].ToObject<int>() == successionTrainedCharaIdDad) ?? addTrainedCharaArray.FirstOrDefault(x => x["trained_chara_id"].ToObject<int>() == successionTrainedCharaIdMom))?.ToObject<TrainedChara>();
                rentalHorse ??= TrainedChara.FirstOrDefault(x => x.trained_chara_id == successionTrainedCharaIdDad) ?? TrainedChara.FirstOrDefault(x => x.trained_chara_id == successionTrainedCharaIdMom);
                var mineHorse = TrainedChara.FirstOrDefault(x => x.trained_chara_id != rentalHorse?.trained_chara_id && x.trained_chara_id == successionTrainedCharaIdDad) ?? TrainedChara.FirstOrDefault(x => x.trained_chara_id != rentalHorse?.trained_chara_id && x.trained_chara_id == successionTrainedCharaIdMom);
                ParentHorseId = mineHorse.trained_chara_id;
                CalculateRelation(rentalHorse, mineHorse);
            }
            if (data.ContainsKey("trained_chara_array") && data.ContainsKey("trained_chara_favorite_array") && data.ContainsKey("room_match_entry_chara_id_array"))
            {
                var obj = jo.ToObject<TrainedCharaLoadResponse>()?.data;
                if (obj is not null)
                {
                    TrainedChara = obj.trained_chara_array;
                    Parent = TrainedChara.FirstOrDefault(x => x.trained_chara_id == ParentHorseId)!;
                    File.WriteAllText(TRAINED_CHARA_FILEPATH, JsonConvert.SerializeObject(obj.trained_chara_array));
                    AnalyzeTrainedCharaLoadResponse(obj);
                }
                return;
            }
            if (data.ContainsKey("user_info_summary"))
            {
                if (data.ContainsKey("practice_partner_info") && data.ContainsKey("support_card_data") && data.ContainsKey("follower_num") && data.ContainsKey("own_follow_num"))
                    ParseFriendSearchResponse(jo.ToObject<FriendSearchResponse>());
                else if (data.ContainsKey("user_info_summary") && data["user_info_summary"].ContainsKey("user_trained_chara"))
                    ParseFriendSearchResponseSimple(jo.ToObject<FriendSearchResponse>());
            }
        }

        void AnalyzeTrainedCharaLoadResponse(TrainedCharaLoadResponse.CommonResponse data)
        {
            var fav_ids = data.trained_chara_favorite_array.Select(x => x.trained_chara_id).ToList();
            var chara = OnlyFavourites
                ? data.trained_chara_array.Where(x => x.is_locked == 1 && fav_ids.Contains(x.trained_chara_id))
                : data.trained_chara_array;
            var win_saddle_result = new List<(string Name, int TrainedCharaId, int WinSaddleBonus, int Score, string CreateTime)>();
            foreach (var i in chara)
            {
                var charaWinSaddle = i.win_saddle_id_array.Intersect(Database.SaddleIds);
                var parentWinSaddle_a = i.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
                var parentWinSaddle_b = i.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
                var win_saddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3
                    + charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;
                win_saddle_result.Add((Database.Names.GetUmamusume(i.card_id).FullName, i.trained_chara_id, win_saddle, i.rank_score, i.create_time));
            }
            switch (DisplayOrder)
            {
                default:
                    {
                        win_saddle_result.Reverse();
                        break;
                    }
                case 1:
                    {
                        win_saddle_result.Sort((a, b) => b.WinSaddleBonus.CompareTo(a.WinSaddleBonus));
                        break;
                    }
                case 2:
                    {
                        win_saddle_result.Sort((a, b) => b.Score.CompareTo(a.Score));
                        break;
                    }
            }
            var table = new Table
            {
                Border = TableBorder.Ascii
            };
            table.AddColumns(I18N_UmaName, "TrainedCharaId", I18N_WinSaddleBonus, I18N_Score);
            foreach (var (Name, TrainedCharaId, WinSaddleBonus, Score, _) in win_saddle_result)
                table.AddRow(Name.EscapeMarkup(), TrainedCharaId.ToString(), WinSaddleBonus.ToString(), Score.ToString());
            AnsiConsole.Write(table);
        }
        public async Task UpdatePlugin(ProgressContext ctx)
        {
            var progress = ctx.AddTask($"[[{Name}]] 更新");

            using var client = new HttpClient();

            var assetsHost = string.IsNullOrEmpty(Config.Updater.CustomDatabaseRepository) ? "https://github.com/UmamusumeResponseAnalyzer/Assets/raw/refs/heads/main/".AllowMirror() : Config.Updater.CustomDatabaseRepository;
            var brUrl = $"{assetsHost}/GameData/ja-JP/factor_effects.br";
            var br = await client.GetByteArrayAsync(brUrl);
            File.WriteAllBytes(FACTOR_EFFECT_FILEPATH, br);

            using var resp = await client.GetAsync($"https://api.github.com/repos/URA-Plugins/{Name}/releases/latest");
            var json = await resp.Content.ReadAsStringAsync();
            var jo = JObject.Parse(json);

            var isLatest = ("v" + Version.ToString()).Equals("v" + jo["tag_name"]?.ToString());
            if (isLatest)
            {
                progress.Increment(progress.MaxValue);
                progress.StopTask();
                return;
            }
            progress.Increment(25);

            var downloadUrl = jo["assets"][0]["browser_download_url"].ToString().AllowMirror();
            using var msg = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            using var stream = await msg.Content.ReadAsStreamAsync();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                    break;
                progress.Increment(read / msg.Content.Headers.ContentLength ?? 1 * 0.5);
            }
            using var archive = new ZipArchive(stream);
            archive.ExtractToDirectory(Path.Combine("Plugins", Name), true);
            progress.Increment(25);

            progress.StopTask();
        }

        [System.Text.RegularExpressions.GeneratedRegex("「(.*?)」のスキルヒント")]
        private static partial System.Text.RegularExpressions.Regex FactorEffectRegex();
    }
}
