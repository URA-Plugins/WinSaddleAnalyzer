using Gallop;
using UmamusumeResponseAnalyzer;
using static WinSaddleAnalyzer.i18n.ParseFriendSearchResponse;

namespace WinSaddleAnalyzer
{
    public partial class WinSaddleAnalyzer
    {
        public void ParseFriendSearchResponse(Gallop.FriendSearchResponse @event)
        {
            var data = @event.data;
            var chara = data.partner_chara_info_array[0];
            // 每个相同的重赏胜场加3胜鞍加成
            var charaWinSaddle = chara.win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_a = chara.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
            var parentWinSaddle_b = chara.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
            var friendAndDadWinSaddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3;
            var friendAndMomWinSaddle = charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;

            ApplyFactorExtend(chara);

            var rows = new List<string>
            {
                string.Format(I18N_Friend, data.user_info_summary.name, data.user_info_summary.viewer_id, data.follower_num),
                string.Format(I18N_Uma, Database.Names.GetUmamusume(chara.card_id).FullName, friendAndDadWinSaddle + friendAndMomWinSaddle, chara.rank_score),
                string.Format(I18N_WinSaddle, string.Join(',', charaWinSaddle)),
                I18N_Factor,
            };

            rows.AddRange(FormatFactors(I18N_UmaFactor, [.. chara.factor_info_array.Select(x => x.factor_id)]));
            rows.AddRange(FormatFactors(
                string.Format(I18N_ParentFactor, chara.succession_chara_array[0].owner_viewer_id),
                [.. chara.succession_chara_array[0].factor_info_array.Select(x => x.factor_id)]));
            rows.AddRange(FormatFactors(
                string.Format(I18N_ParentFactor, chara.succession_chara_array[1].owner_viewer_id),
                [.. chara.succession_chara_array[1].factor_info_array.Select(x => x.factor_id)]));
            ShowPanel("friend", "好友", new(string.Join(Environment.NewLine, rows)));
            ShowPanel("inheritance", "相性分析", BuildRelationDisplay(chara));
        }
        public void ParseFriendSearchResponseSimple(UserInfoAtFriend userInfo)
        {
            var chara = userInfo.user_trained_chara_array[0];
            var rows = new List<string>
            {
                string.Format(I18N_FriendSimple, userInfo.name, userInfo.viewer_id),
                string.Format(I18N_UmaSimple, Database.Names.GetUmamusume(chara.card_id).FullName),
                I18N_Factor,
            };

            rows.AddRange(FormatFactors(I18N_UmaFactor, [.. chara.factor_info_array.Select(x => x.factor_id)]));
            ShowPanel("friend", "好友", new(string.Join(Environment.NewLine, rows)));
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
        static IEnumerable<string> FormatFactors(string title, int[] factorIds)
        {
            yield return title;
            if (factorIds.Length == 0)
                yield break;

            var ordered = factorIds.Take(2).Append(factorIds[^1]).Concat(factorIds.Skip(2).SkipLast(1));
            var even = ordered.Where((x, index) => index % 2 == 0).ToArray();
            var odd = ordered.Where((x, index) => index % 2 != 0).ToArray();
            foreach (var index in Enumerable.Range(0, even.Length))
            {
                var second = odd.Length > index ? $"    {Database.FactorIds[odd[index]]}" : string.Empty;
                yield return $"  {Database.FactorIds[even[index]]}{second}";
            }
        }
        DisplayResult BuildRelationDisplay(TrainedChara friend, TrainedChara? mine = null)
        {
            mine ??= Parent;
            if (mine is null)
            {
                const string warning = "未找到种马信息，请先查看一次殿堂马再尝试看相性。";
                return new(warning, warning);
            }

            var rows = new List<string>();
            CalculateRelation(friend, mine, rows);
            return new(string.Join(Environment.NewLine, rows));
        }

        public (int, int, int, int, int, int, int, int) CalculateRelation(TrainedChara friend, TrainedChara? mine = null)
            => CalculateRelation(friend, mine, null);

        (int, int, int, int, int, int, int, int) CalculateRelation(
            TrainedChara friend,
            TrainedChara? mine,
            List<string>? rows)
        {
            mine ??= Parent;
            if (mine is null)
                return (0, 0, 0, 0, 0, 0, 0, 0);
            var mineChara = mine;
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
            if (TargetHorseId != 0)
            {
                var friendWinSaddleWithParent = mineChara.win_saddle_id_array.Intersect(Database.SaddleIds).Intersect(charaWinSaddle).Count() * 3;

                var targetRelations = Database.SuccessionRelation.MemberDictionary.Where(x => x.Value.Contains(TargetHorseId)).ToDictionary();
                var relationWithFriendChara = SumWinSaddles(friend, targetRelations);
                friendDadTotalRelation = friendAndDadWinSaddle + relationWithFriendChara.Item2;
                friendMomTotalRelation = friendAndMomWinSaddle + relationWithFriendChara.Item3;

                var mineHorseWinSaddle = mineChara.win_saddle_id_array.Intersect(Database.SaddleIds);
                var mineWinSaddle_a = mineChara.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
                var mineWinSaddle_b = mineChara.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
                var mineAndDadWinSaddle = mineHorseWinSaddle.Intersect(mineWinSaddle_a).Count() * 3;
                var mineAndMomWinSaddle = mineHorseWinSaddle.Intersect(mineWinSaddle_b).Count() * 3;

                var relationWithMineHorse = SumWinSaddles(mineChara, targetRelations);
                mineDadTotalRelation = mineAndDadWinSaddle + relationWithMineHorse.Item2;
                mineMomTotalRelation = mineAndMomWinSaddle + relationWithMineHorse.Item3;

                var mineHorseCardId = int.Parse(mineChara.card_id.ToString()[..4]);
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
            rows?.Add($"好友总相性：{friendTotalRelation}\t好友单相性：{friendSingleRelation}\t好友祖1相性{friendDadTotalRelation}\t好友祖2相性{friendMomTotalRelation}");
            rows?.Add($"自己总相性：{mineTotalRelation}\t自己单相性：{mineSingleRelation}\t自己祖1相性{mineDadTotalRelation}\t自己祖2相性{mineMomTotalRelation}");

            var distanceFactorProbe = new Dictionary<string, decimal>();

            var friendDistanceFactors = friend.factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            var friendDadDistanceFactors = friend.succession_chara_array[0].factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            var friendMomDistanceFactors = friend.succession_chara_array[1].factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            CalculateProper(distanceFactorProbe, friendDistanceFactors, friendTotalRelation);
            CalculateProper(distanceFactorProbe, friendDadDistanceFactors, friendDadTotalRelation);
            CalculateProper(distanceFactorProbe, friendMomDistanceFactors, friendMomTotalRelation);

            var mineDistanceFactors = mineChara.factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            var mineDadDistanceFactors = mineChara.succession_chara_array[0].factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            var mineMomDistanceFactors = mineChara.succession_chara_array[1].factor_info_array.Where(x => (x.factor_id >= 1000 && x.factor_id < 10000) || (x.factor_id >= 5000000 && x.factor_id < 5001100));
            CalculateProper(distanceFactorProbe, mineDistanceFactors, mineTotalRelation);
            CalculateProper(distanceFactorProbe, mineDadDistanceFactors, mineDadTotalRelation);
            CalculateProper(distanceFactorProbe, mineMomDistanceFactors, mineMomTotalRelation);
            rows?.Add($"单次继承概率：{string.Join(',', distanceFactorProbe.Select(x => $"{Database.FactorIds[int.Parse($"{x.Key}1")].Replace("★", string.Empty)}: {1 - x.Value:0.00%}"))}");

            CalculateProper(distanceFactorProbe, friendDistanceFactors, friendTotalRelation);
            CalculateProper(distanceFactorProbe, friendDadDistanceFactors, friendDadTotalRelation);
            CalculateProper(distanceFactorProbe, friendMomDistanceFactors, friendMomTotalRelation);

            CalculateProper(distanceFactorProbe, mineDistanceFactors, mineTotalRelation);
            CalculateProper(distanceFactorProbe, mineDadDistanceFactors, mineDadTotalRelation);
            CalculateProper(distanceFactorProbe, mineMomDistanceFactors, mineMomTotalRelation);
            rows?.Add($"两次继承概率：{string.Join(',', distanceFactorProbe.Select(x => $"{Database.FactorIds[int.Parse($"{x.Key}1")].Replace("★", string.Empty)}: {1 - x.Value:0.00%}"))}");

            EnsureFactorEffectsLoaded();
            if (SkillEffects.Count == 0)
                SkillEffects = SkillEffectFileStore.Load();

            var friendSkillFactorProbe = CalculateSkillEffect(friend, friendTotalRelation, friendDadTotalRelation, friendMomTotalRelation);
            rows?.Add($"好友技能期望收益：{friendSkillFactorProbe.Sum(x => SkillEffects[x.Key] * (1 - x.Value)):0.00}");

            var mineSkillFactorProbe = CalculateSkillEffect(mineChara, mineTotalRelation, mineDadTotalRelation, mineMomTotalRelation);
            rows?.Add($"自己技能期望收益：{mineSkillFactorProbe.Sum(x => SkillEffects[x.Key] * (1 - x.Value)):0.00}");

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
