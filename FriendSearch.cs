using Gallop;
using Spectre.Console;
using System;
using System.Text;
using UmamusumeResponseAnalyzer;
using static WinSaddleAnalyzer.i18n.ParseFriendSearchResponse;

namespace WinSaddleAnalyzer
{
    public partial class WinSaddleAnalyzer
    {
        public void ParseFriendSearchResponse(Gallop.FriendSearchResponse @event)
        {
            var data = @event.data;
            var chara = data.practice_partner_info;
            // 每个相同的重赏胜场加3胜鞍加成
            var charaWinSaddle = chara.win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_a = chara.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_b = chara.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
            var friendAndDadWinSaddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3;
            var friendAndMomWinSaddle = charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;

            ApplyFactorExtend(chara);

            AnsiConsole.Write(new Rule());
            AnsiConsole.WriteLine(I18N_Friend, data.user_info_summary.name, data.user_info_summary.viewer_id, data.follower_num);
            AnsiConsole.WriteLine(I18N_Uma, Database.Names.GetUmamusume(chara.card_id).FullName, friendAndDadWinSaddle + friendAndMomWinSaddle, chara.rank_score);
            AnsiConsole.WriteLine(I18N_WinSaddle, string.Join(',', charaWinSaddle));
            var tree = new Tree(I18N_Factor);

            var max = chara.factor_info_array.Select(x => x.factor_id).Concat(chara.succession_chara_array[0].factor_info_array.Select(x => x.factor_id))
                .Concat(chara.succession_chara_array[1].factor_info_array.Select(x => x.factor_id))
                .Where((x, index) => index % 2 == 0)
                .Max(x => GetRenderWidth(Database.FactorIds[x]));
            var representative = AddFactors(I18N_UmaFactor, [.. chara.factor_info_array.Select(x => x.factor_id)], max);
            var inheritanceA = AddFactors(string.Format(I18N_ParentFactor, chara.succession_chara_array[0].owner_viewer_id), chara.succession_chara_array[0].factor_info_array.Select(x => x.factor_id).ToArray(), max);
            var inheritanceB = AddFactors(string.Format(I18N_ParentFactor, chara.succession_chara_array[1].owner_viewer_id), chara.succession_chara_array[1].factor_info_array.Select(x => x.factor_id).ToArray(), max);

            tree.AddNodes(representative, inheritanceA, inheritanceB);
            AnsiConsole.Write(tree);

            CalculateRelation(chara);

            AnsiConsole.Write(new Rule());
        }
        public static void ParseFriendSearchResponseSimple(Gallop.FriendSearchResponse @event)
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
        public void ApplyFactorExtend(TrainedChara chara)
        {
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
        }
        public static Tree AddFactors(string title, int[] id_array, int max)
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
                if (gap < 0) { gap = 2; }
                sb.Append(string.Join(string.Empty, Enumerable.Repeat(' ', gap)));
                sb.Append(odd.Length > index ? FactorName(odd[index]) : "");
                tree.AddNode(sb.ToString());
            }
            return tree;
        }
        public static string FactorName(int factorId)
        {
            var name = Database.FactorIds[factorId];
            return factorId.ToString().Length switch
            {
                3 => $"[#FFFFFF on #37B8F4]{name} [/]", // 蓝
                4 => $"[#FFFFFF on #FF78B2]{name} [/]", // 红
                8 => $"[#794016 on #91D02E]{name} [/]", // 固有
                _ => $"[#794016 on #E1E2E1]{name} [/]", // 白
            };
        }
        public static int GetRenderWidth(string text)
        {
            return text.Sum(x => x.GetCellWidth());
        }
        public (int, int, int, int, int, int, int, int) CalculateRelation(TrainedChara friend, TrainedChara mine = null!)
        {
            mine ??= Parent;
            if (mine is null)
            {
                AnsiConsole.MarkupLine($"[red]未找到种马信息，请先查看一次殿堂马再尝试看相性。[/]");
                return (0, 0, 0, 0, 0, 0, 0, 0);
            }
            var charaWinSaddle = friend.win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_a = friend.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_b = friend.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
            var friendAndDadWinSaddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3;
            var friendAndMomWinSaddle = charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;

            var friendTotalRelation = 0;
            var friendSingleRelation = 0;
            var friendDadTotalRelation = 0;
            var friendMomTotalRelation = 0;

            var mineTotalRelation = 0;
            var mineSingleRelation = 0;
            var mineDadTotalRelation = 0;
            var mineMomTotalRelation = 0;

            // https://www.bilibili.com/video/BV1tX96YMEZ9?t=205.9
            if (TargetHorseId != 0 && mine != null)
            {
                var friendWinSaddleWithParent = mine.win_saddle_id_array.Intersect(Database.SaddleIds).Intersect(charaWinSaddle).Count() * 3;

                var targetRelations = Database.SuccessionRelation.MemberDictionary.Where(x => x.Value.Contains(TargetHorseId)).ToDictionary();
                var relationWithFriendChara = SumWinSaddles(friend, targetRelations);
                friendDadTotalRelation = friendAndDadWinSaddle + relationWithFriendChara.Item2;
                friendMomTotalRelation = friendAndMomWinSaddle + relationWithFriendChara.Item3;

                var mineHorseWinSaddle = mine.win_saddle_id_array.Intersect(Database.SaddleIds);
                var mineWinSaddle_a = mine.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
                var mineWinSaddle_b = mine.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
                var mineAndDadWinSaddle = mineHorseWinSaddle.Intersect(mineWinSaddle_a).Count() * 3;
                var mineAndMomWinSaddle = mineHorseWinSaddle.Intersect(mineWinSaddle_b).Count() * 3;

                var relationWithMineHorse = SumWinSaddles(mine, targetRelations);
                mineDadTotalRelation = mineAndDadWinSaddle + relationWithMineHorse.Item2;
                mineMomTotalRelation = mineAndMomWinSaddle + relationWithMineHorse.Item3;

                var mineHorseCardId = int.Parse(mine.card_id.ToString()[..4]);
                var friendHorseCardId = int.Parse(friend.card_id.ToString()[..4]);
                var mineHorseRelations = Database.SuccessionRelation.MemberDictionary.Where(x => x.Value.Contains(mineHorseCardId)).ToDictionary();
                var friendHorseRelations = Database.SuccessionRelation.MemberDictionary.Where(x => x.Value.Contains(friendHorseCardId)).ToDictionary();
                var mineAndFriendRelations = mineHorseRelations.Keys.Intersect(friendHorseRelations.Keys);
                var mineAndFriendRelationPoint = mineAndFriendRelations.Sum(x => Database.SuccessionRelation.PointDictionary[x]);

                friendSingleRelation = relationWithFriendChara.Item1 + friendDadTotalRelation + friendMomTotalRelation;
                friendTotalRelation = friendSingleRelation + friendWinSaddleWithParent + mineAndFriendRelationPoint;
                mineSingleRelation = relationWithMineHorse.Item1 + mineDadTotalRelation + mineMomTotalRelation;
                mineTotalRelation = mineSingleRelation + friendWinSaddleWithParent + mineAndFriendRelationPoint;
            }
            AnsiConsole.WriteLine($"好友总相性：{friendTotalRelation}\t好友单相性：{friendSingleRelation}\t好友祖1相性{friendDadTotalRelation}\t好友祖2相性{friendMomTotalRelation}");
            AnsiConsole.WriteLine($"自己总相性：{mineTotalRelation}\t自己单相性：{mineSingleRelation}\t自己祖1相性{mineDadTotalRelation}\t自己祖2相性{mineMomTotalRelation}");

            var distanceFactorProbe = new Dictionary<string, decimal>();

            var friendDistanceFactors = friend.factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            var friendDadDistanceFactors = friend.succession_chara_array[0].factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            var friendMomDistanceFactors = friend.succession_chara_array[1].factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            CalculateProper(distanceFactorProbe, friendDistanceFactors, friendTotalRelation);
            CalculateProper(distanceFactorProbe, friendDadDistanceFactors, friendDadTotalRelation);
            CalculateProper(distanceFactorProbe, friendMomDistanceFactors, friendMomTotalRelation);

            var mineDistanceFactors = mine.factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            var mineDadDistanceFactors = mine.succession_chara_array[0].factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            var mineMomDistanceFactors = mine.succession_chara_array[1].factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            CalculateProper(distanceFactorProbe, mineDistanceFactors, mineTotalRelation);
            CalculateProper(distanceFactorProbe, mineDadDistanceFactors, mineDadTotalRelation);
            CalculateProper(distanceFactorProbe, mineMomDistanceFactors, mineMomTotalRelation);
            AnsiConsole.WriteLine($"单次继承概率：{string.Join(',', distanceFactorProbe.Select(x => $"{Database.FactorIds[int.Parse($"{x.Key}1")].Replace("★", string.Empty)}: {1 - x.Value:0.00%}"))}");

            CalculateProper(distanceFactorProbe, friendDistanceFactors, friendTotalRelation);
            CalculateProper(distanceFactorProbe, friendDadDistanceFactors, friendDadTotalRelation);
            CalculateProper(distanceFactorProbe, friendMomDistanceFactors, friendMomTotalRelation);

            CalculateProper(distanceFactorProbe, mineDistanceFactors, mineTotalRelation);
            CalculateProper(distanceFactorProbe, mineDadDistanceFactors, mineDadTotalRelation);
            CalculateProper(distanceFactorProbe, mineMomDistanceFactors, mineMomTotalRelation);
            AnsiConsole.WriteLine($"两次继承概率：{string.Join(',', distanceFactorProbe.Select(x => $"{Database.FactorIds[int.Parse($"{x.Key}1")].Replace("★", string.Empty)}: {1 - x.Value:0.00%}"))}");

            var friendSkillFactorProbe = CalculateSkillEffect(friend, friendTotalRelation, friendDadTotalRelation, friendMomTotalRelation);
            AnsiConsole.WriteLine($"好友技能期望收益：{friendSkillFactorProbe.Sum(x => SkillEffects[x.Key] * (1 - x.Value)):0.00}");

            var mineSkillFactorProbe = CalculateSkillEffect(mine, mineTotalRelation, mineDadTotalRelation, mineMomTotalRelation);
            AnsiConsole.WriteLine($"自己技能期望收益：{mineSkillFactorProbe.Sum(x => SkillEffects[x.Key] * (1 - x.Value)):0.00}");

            return (friendTotalRelation, friendSingleRelation, friendDadTotalRelation, friendMomTotalRelation, mineTotalRelation, mineSingleRelation, mineDadTotalRelation, mineMomTotalRelation);
        }
        public (int, int, int) SumWinSaddles(TrainedChara chara, Dictionary<int, List<int>> targetRelations)
        {
            var charaId = int.Parse(chara.card_id.ToString()[..4]);
            var dadCharaId = int.Parse(chara.succession_chara_array[0].card_id.ToString()[..4]);
            var momCharaId = int.Parse(chara.succession_chara_array[1].card_id.ToString()[..4]);

            var charaRelations = Database.SuccessionRelation.MemberDictionary.Where(x => x.Value.Contains(charaId)).ToDictionary();
            var charaAndTargetRelations = targetRelations.Keys.Intersect(charaRelations.Keys);
            var charaAndTargetRelationPoint = charaAndTargetRelations.Sum(x => Database.SuccessionRelation.PointDictionary[x]);

            var dadRelations = Database.SuccessionRelation.MemberDictionary.Where(x => x.Value.Contains(dadCharaId)).ToDictionary();
            var charaAndTargetAndDadRelations = targetRelations.Keys.Intersect(charaRelations.Keys).Intersect(dadRelations.Keys);
            var charaAndTargetAndDadPoint = charaAndTargetAndDadRelations.Sum(x => Database.SuccessionRelation.PointDictionary[x]);

            var momRelations = Database.SuccessionRelation.MemberDictionary.Where(x => x.Value.Contains(momCharaId)).ToDictionary();
            var charaAndTargetAndMomRelations = targetRelations.Keys.Intersect(charaRelations.Keys).Intersect(momRelations.Keys);
            var charaAndTargetAndMomPoint = charaAndTargetAndMomRelations.Sum(x => Database.SuccessionRelation.PointDictionary[x]);

            return (charaAndTargetRelationPoint, charaAndTargetAndDadPoint, charaAndTargetAndMomPoint);
        }
        public void CalculateProper(Dictionary<string, decimal> distanceFactorProbe, IEnumerable<FactorInfo> factors, int relation)
        {
            foreach (var factor in factors)
            {
                decimal probe = factor.factor_id % 10 * 2 - 1;
                var type = factor.factor_id < 10000 ? factor.factor_id.ToString()[..3] : factor.factor_id.ToString().Substring(3, 3);
                type = type
                    .Replace("010", "110")
                    .Replace("020", "120")
                    .Replace("030", "310")
                    .Replace("040", "320")
                    .Replace("050", "330")
                    .Replace("060", "340")
                    .Replace("070", "210")
                    .Replace("080", "220")
                    .Replace("090", "230")
                    .Replace("100", "240");
                distanceFactorProbe.TryAdd(type, 1);
                distanceFactorProbe[type] *= 1 - probe * (100 + relation) / 10000;
            }
        }
        public Dictionary<string, double> CalculateSkillEffect(TrainedChara chara, int charaRelation, int dadRelation, int momRelation)
        {
            var skillFactorProbe = new Dictionary<string, double>();
            foreach (var factor in chara.succession_chara_array[0].factor_info_array.Where(x => x.factor_id < 10000000)) // 亲辈不看固有
            {
                var factorId = factor.factor_id;
                if (FactorEffects.TryGetValue(factorId, out var skillName))
                {
                    if (SkillEffects.ContainsKey(skillName))
                    {
                        double probe = factorId % 10 * 3;
                        skillFactorProbe.TryAdd(skillName, 1);
                        skillFactorProbe[skillName] *= 1 - probe * (100 + charaRelation) / 10000; // 技能收益算两次继承之后的
                        skillFactorProbe[skillName] *= 1 - probe * (100 + charaRelation) / 10000;
                    }
                }
            }
            foreach (var factor in chara.succession_chara_array[0].factor_info_array)
            {
                var factorId = factor.factor_id;
                if (FactorEffects.TryGetValue(factorId, out var skillName))
                {
                    if (SkillEffects.ContainsKey(skillName))
                    {
                        double probe = factorId % 10 * (factorId < 10000000 ? 3 : 5);
                        skillFactorProbe.TryAdd(skillName, 1);
                        skillFactorProbe[skillName] *= 1 - probe * (100 + dadRelation) / 10000;
                        skillFactorProbe[skillName] *= 1 - probe * (100 + dadRelation) / 10000;
                    }
                }
            }
            foreach (var factor in chara.succession_chara_array[1].factor_info_array)
            {
                var factorId = factor.factor_id;
                if (FactorEffects.TryGetValue(factorId, out var skillName))
                {
                    if (SkillEffects.ContainsKey(skillName))
                    {
                        double probe = factorId % 10 * (factorId < 10000000 ? 3 : 5);
                        skillFactorProbe.TryAdd(skillName, 1);
                        skillFactorProbe[skillName] *= 1 - probe * (100 + momRelation) / 10000;
                        skillFactorProbe[skillName] *= 1 - probe * (100 + momRelation) / 10000;
                    }
                }
            }
            return skillFactorProbe;
        }
    }
}
