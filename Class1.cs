using Gallop;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.IO.Compression;
using System.Text;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using static WinSaddleAnalyzer.i18n.ParseFriendSearchResponse;
using static WinSaddleAnalyzer.i18n.ParseTrainedCharaLoadResponse;

namespace WinSaddleAnalyzer
{
    public class WinSaddleAnalyzer : IPlugin
    {
        [PluginDescription("显示殿堂马&好友种马信息")]
        public string Name => "WinSaddleAnalyzer";
        public string Author => "离披";
        public Version Version => new(1, 0, 0, 0);
        public string[] Targets => [];

        [PluginSetting, PluginDescription("显示顺序: 0为从老到新，1是从高胜鞍到低胜鞍，2是从高分到低分")]
        public int DisplayOrder { get; set; } = 0;
        [PluginSetting, PluginDescription("是否只显示收藏了的马")]
        public bool OnlyFavourites { get; set; } = false;

        [Analyzer]
        public void Analyze(JObject jo)
        {
            if (!jo.ContainsKey("data")) return;
            var data = jo["data"] as JObject;
            if (data.ContainsKey("trained_chara_array") && data.ContainsKey("trained_chara_favorite_array") && data.ContainsKey("room_match_entry_chara_id_array"))
            {
                AnalyzeTrainedCharaLoadResponse(jo);
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

        void AnalyzeTrainedCharaLoadResponse(JObject jo)
        {
            dynamic dyn = jo;
            var data = ((TrainedCharaLoadResponse)dyn.ToObject<TrainedCharaLoadResponse>()).data;
            var fav_ids = data.trained_chara_favorite_array.Select(x => x.trained_chara_id).ToList();
            var chara = OnlyFavourites
                ? data.trained_chara_array.Where(x => x.is_locked == 1 && fav_ids.Contains(x.trained_chara_id))
                : data.trained_chara_array;
            var win_saddle_result = new List<(string Name, int WinSaddleBonus, string WinSaddleArray, int Score, string CreateTime)>();
            foreach (var i in chara)
            {
                var charaWinSaddle = i.win_saddle_id_array.Intersect(Database.SaddleIds);
                var parentWinSaddle_a = i.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
                var parentWinSaddle_b = i.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
                var win_saddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3
                    + charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;
                win_saddle_result.Add((Database.Names.GetUmamusume(i.card_id).FullName, win_saddle, string.Join(',', charaWinSaddle), i.rank_score, i.create_time));
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
            table.AddColumns(I18N_UmaName, I18N_WinSaddleBonus, i18n.ParseFriendSearchResponse.I18N_WinSaddle, I18N_Score);
            foreach (var (Name, WinSaddleBonus, WinSaddleArray, Score, _) in win_saddle_result)
                table.AddRow(Name.EscapeMarkup(), WinSaddleBonus.ToString(), WinSaddleArray, Score.ToString());
            AnsiConsole.Write(table);
        }

        void ParseFriendSearchResponse(FriendSearchResponse @event)
        {
            var data = @event.data;
            var chara = data.practice_partner_info;
            // 每个相同的重赏胜场加3胜鞍加成
            var charaWinSaddle = chara.win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_a = chara.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_b = chara.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
            var win_saddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3
                + charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;
            // 应用因子强化
            if (chara.factor_extend_array != null)
            {
                foreach (var i in chara.factor_extend_array)
                {
                    if (i.position_id == 1)
                    {
                        var extendedFactor = chara.factor_info_array.FirstOrDefault(x => x.factor_id == i.base_factor_id);
                        if (extendedFactor == default) continue;
                        extendedFactor.factor_id = i.factor_id;
                    }
                    else
                    {
                        var successionChara = chara.succession_chara_array.FirstOrDefault(x => x.position_id == i.position_id);
                        if (successionChara == default) continue;
                        var extendedFactor = successionChara.factor_info_array.FirstOrDefault(x => x.factor_id == i.base_factor_id);
                        if (extendedFactor == default) continue;
                        extendedFactor.factor_id = i.factor_id;
                    }
                }
            }

            AnsiConsole.Write(new Rule());
            AnsiConsole.WriteLine(I18N_Friend, data.user_info_summary.name, data.user_info_summary.viewer_id, data.follower_num);
            AnsiConsole.WriteLine(I18N_Uma, Database.Names.GetUmamusume(chara.card_id).FullName, win_saddle, chara.rank_score);
            AnsiConsole.WriteLine(i18n.ParseFriendSearchResponse.I18N_WinSaddle, string.Join(',', charaWinSaddle));
            if (Database.SaddleNames.Count != 0)
                AnsiConsole.WriteLine(I18N_WinSaddleDetail, string.Join(',', charaWinSaddle.Select(x => Database.SaddleNames[x])));
            var tree = new Tree(I18N_Factor);

            var max = chara.factor_info_array.Select(x => x.factor_id).Concat(chara.succession_chara_array[0].factor_info_array.Select(x => x.factor_id))
                .Concat(chara.succession_chara_array[1].factor_info_array.Select(x => x.factor_id))
                .Where((x, index) => index % 2 == 0)
                .Max(x => GetRenderWidth(Database.FactorIds[x]));
            var representative = AddFactors(I18N_UmaFactor, chara.factor_info_array.Select(x => x.factor_id).ToArray(), max);
            var inheritanceA = AddFactors(string.Format(I18N_ParentFactor, chara.succession_chara_array[0].owner_viewer_id), chara.succession_chara_array[0].factor_info_array.Select(x => x.factor_id).ToArray(), max);
            var inheritanceB = AddFactors(string.Format(I18N_ParentFactor, chara.succession_chara_array[1].owner_viewer_id), chara.succession_chara_array[1].factor_info_array.Select(x => x.factor_id).ToArray(), max);

            tree.AddNodes(representative, inheritanceA, inheritanceB);
            AnsiConsole.Write(tree);
            AnsiConsole.Write(new Rule());
        }
        void ParseFriendSearchResponseSimple(FriendSearchResponse @event)
        {
            var data = @event.data;
            AnsiConsole.Write(new Rule());
            AnsiConsole.WriteLine(I18N_FriendSimple, data.user_info_summary.name, data.user_info_summary.viewer_id);
            AnsiConsole.WriteLine(I18N_UmaSimple, Database.Names.GetUmamusume(data.user_info_summary.user_trained_chara.card_id).FullName);
            var tree = new Tree(I18N_Factor);

            var i = data.user_info_summary.user_trained_chara;
            var max = i.factor_info_array.Select(x => x.factor_id)
                .Where((x, index) => index % 2 == 0)
                .Max(x => GetRenderWidth(Database.FactorIds[x]));
            var representative = AddFactors(I18N_UmaFactor, i.factor_info_array.Select(x => x.factor_id).ToArray(), max);

            tree.AddNodes(representative);
            AnsiConsole.Write(tree);
            AnsiConsole.Write(new Rule());
        }
        Tree AddFactors(string title, int[] id_array, int max)
        {
            var tree = new Tree(title);
            var ordered = id_array.Take(2).Append(id_array[^1]).Concat(id_array.Skip(2).SkipLast(1));
            var even = ordered.Where((x, index) => index % 2 == 0).ToArray();
            var odd = ordered.Where((x, index) => index % 2 != 0).ToArray();
            foreach (var index in Enumerable.Range(0, even.Length))
            {
                var sb = new StringBuilder();
                sb.Append(FactorName(even[index]));
                var gap = 12 + max - GetRenderWidth(Database.FactorIds[even[index]]);
                if (gap < 0) gap = 2; sb.Append(string.Join(string.Empty, Enumerable.Repeat(' ', gap)));
                sb.Append(odd.Length > index ? FactorName(odd[index]) : "");
                tree.AddNode(sb.ToString());
            }
            return tree;
        }
        string FactorName(int factorId)
        {
            var name = Database.FactorIds[factorId];
            return factorId.ToString().Length switch
            {
                3 => $"[#FFFFFF on #37B8F4]{name}[/]", // 蓝
                4 => $"[#FFFFFF on #FF78B2]{name}[/]", // 红
                8 => $"[#794016 on #91D02E]{name}[/]", // 固有
                _ => $"[#794016 on #E1E2E1]{name}[/]", // 白
            };
        }
        int GetRenderWidth(string text)
        {
            return text.Sum(x => x.GetCellWidth());
        }

        public async Task UpdatePlugin(ProgressContext ctx)
        {
            var progress = ctx.AddTask($"[{Name}] 更新");

            using var client = new HttpClient();
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

            var downloadUrl = jo["assets"][0]["browser_download_url"].ToString();
            if (Config.Updater.IsGithubBlocked && !Config.Updater.ForceUseGithubToUpdate)
            {
                downloadUrl = downloadUrl.Replace("https://", "https://gh.shuise.dev/");
            }
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
    }
}
