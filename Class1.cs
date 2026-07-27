using Gallop;
using Gallop.Endpoints;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.LiveDisplay;
using UmamusumeResponseAnalyzer.Plugin;
using static WinSaddleAnalyzer.i18n.ParseTrainedCharaLoadResponse;

namespace WinSaddleAnalyzer
{
    public partial class WinSaddleAnalyzer : IPlugin
    {
        public string Name => "WinSaddleAnalyzer";
        public string Author => "离披";
        public string[] Targets => [];
        public string DataDirectory => Path.Combine("PluginData", Name);
        public string SettingsFilePath => Path.Combine("PluginData", Name, "settings.json");

        public TrainedCharaSortOrder TrainedCharaSort { get; set; } = TrainedCharaSortOrder.不排序;
        public bool OnlyFavourites { get; set; } = true;

        public int TargetHorseId { get; set; } = 0;
        public int ParentHorseId { get; set; } = 0;
        ILiveDisplayOutput? liveDisplay;
        LiveDisplayWorkspace? workspace;
        TrainedCharaLoadResponse.CommonResponse? CurrentTrainedCharaData { get; set; }

        static readonly JsonSerializerSettings SettingsJson = new()
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            Converters = { new StringEnumConverter { AllowIntegerValues = false } },
        };

        internal string PLUGIN_DATA_DIRECTORY = string.Empty;
        internal string DATABASE_DIRECTORY = string.Empty;
        internal string SETTINGS_FILEPATH = string.Empty;
        internal string TRAINED_CHARA_FILEPATH = string.Empty;
        internal string FACTOR_EFFECT_FILEPATH = string.Empty;
        internal TrainedChara[] TrainedChara = [];
        internal Dictionary<int, string> FactorEffects = [];
        internal Dictionary<string, double> SkillEffects = [];
        /// <summary>
        /// 指定的前提种马
        /// </summary>
        internal static TrainedChara? Parent { get; set; } = default!;

        public void Initialize(IPluginContext context)
        {
            liveDisplay = context.LiveDisplay;
            PLUGIN_DATA_DIRECTORY = DataDirectory;
            Directory.CreateDirectory(PLUGIN_DATA_DIRECTORY);
            DATABASE_DIRECTORY = Directory.GetCurrentDirectory();
            SETTINGS_FILEPATH = SettingsFilePath;
            TRAINED_CHARA_FILEPATH = Path.Combine(PLUGIN_DATA_DIRECTORY, "trained_chara.json");
            FACTOR_EFFECT_FILEPATH = Path.Combine(DATABASE_DIRECTORY, "factor_effects.br");
            LoadSettings();

            TrainedChara = File.Exists(TRAINED_CHARA_FILEPATH)
                ? JsonConvert.DeserializeObject<TrainedChara[]>(File.ReadAllText(TRAINED_CHARA_FILEPATH)) ?? []
                : [];

            RefreshParent();
        }

        public void Dispose()
        {
            var output = liveDisplay;
            var target = workspace;
            liveDisplay = null;
            workspace = null;

            if (output is not null && target is not null)
                output.RemoveWorkspace(target);
        }

        public async Task ConfigPromptAsync(
            IApplication application,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(application);
            cancellationToken.ThrowIfCancellationRequested();
            if (application.TopRunnable is null &&
                Environment.CurrentManagedThreadId != application.MainThreadId)
                throw new InvalidOperationException(
                    "WinSaddleAnalyzer 无法从非 UI thread 启动配置：Terminal.Gui 当前没有正在运行的 session。");

            var draft = new WinSaddleSettings(TrainedCharaSort, OnlyFavourites, TargetHorseId, ParentHorseId);
            WinSaddleSettings saved;
            if (Environment.CurrentManagedThreadId == application.MainThreadId)
            {
                saved = RunConfigDialog(application, draft, cancellationToken);
            }
            else
            {
                var completion = new TaskCompletionSource<WinSaddleSettings>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                application.Invoke(() =>
                {
                    try
                    {
                        completion.SetResult(RunConfigDialog(application, draft, cancellationToken));
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }
                });
                saved = await completion.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            TrainedCharaSort = saved.TrainedCharaSort;
            OnlyFavourites = saved.OnlyFavourites;
            TargetHorseId = saved.TargetHorseId;
            ParentHorseId = saved.ParentHorseId;
            SaveSettings();
            RefreshParent();
            PublishTrainedCharaTable();
        }

        static WinSaddleSettings RunConfigDialog(
            IApplication application,
            WinSaddleSettings draft,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var dialog = new Dialog
            {
                Title = "WinSaddleAnalyzer 配置",
                Width = 72,
                Height = 24,
            };
            var trainedCharaSort = new OptionSelector<TrainedCharaSortOrder>
            {
                X = 1,
                Y = 2,
                Value = draft.TrainedCharaSort,
            };
            var onlyFavourites = new CheckBox
            {
                X = 1,
                Y = 13,
                Text = "只显示收藏了的马",
                Value = draft.OnlyFavourites ? CheckState.Checked : CheckState.UnChecked,
            };
            var targetHorseId = new NumericUpDown<int>
            {
                X = 28,
                Y = 15,
                Width = 20,
                Value = draft.TargetHorseId,
                Increment = 1,
            };
            var parentHorseId = new NumericUpDown<int>
            {
                X = 28,
                Y = 17,
                Width = 20,
                Value = draft.ParentHorseId,
                Increment = 1,
            };
            dialog.Add(
                new Label { X = 1, Y = 1, Text = "显示顺序" },
                trainedCharaSort,
                onlyFavourites,
                new Label { X = 1, Y = 15, Text = "要养的马的 CharaId" },
                targetHorseId,
                new Label { X = 1, Y = 17, Text = "另一个种马的 TrainedCharaId" },
                parentHorseId);

            var accepted = false;
            var save = new Button { Text = "保存", IsDefault = true };
            save.Accepting += (_, e) =>
            {
                accepted = true;
                application.RequestStop(dialog);
                e.Handled = true;
            };
            var cancel = new Button { Text = "取消" };
            cancel.Accepting += (_, e) =>
            {
                application.RequestStop(dialog);
                e.Handled = true;
            };
            dialog.AddButton(cancel);
            dialog.AddButton(save);
            trainedCharaSort.SetFocus();

            using (cancellationToken.Register(
                       () => application.Invoke(() => application.RequestStop(dialog))))
                application.Run(dialog);
            cancellationToken.ThrowIfCancellationRequested();
            if (!accepted)
                throw new OperationCanceledException("WinSaddleAnalyzer 配置已取消。", cancellationToken);

            return new(
                trainedCharaSort.Value
                    ?? throw new InvalidOperationException("WinSaddleAnalyzer 排序状态未选择。"),
                onlyFavourites.Value == CheckState.Checked,
                targetHorseId.Value,
                parentHorseId.Value);
        }

        void LoadSettings()
        {
            if (!File.Exists(SETTINGS_FILEPATH)) return;

            WinSaddleSettings settings;
            try
            {
                settings = JsonConvert.DeserializeObject<WinSaddleSettings>(
                        File.ReadAllText(SETTINGS_FILEPATH),
                        SettingsJson)
                    ?? throw new InvalidDataException(
                        $"WinSaddleAnalyzer 配置文件反序列化失败: {SETTINGS_FILEPATH}");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"WinSaddleAnalyzer 配置文件无效: {SETTINGS_FILEPATH}。{ex.Message}",
                    ex);
            }

            if (!Enum.IsDefined(settings.TrainedCharaSort))
                throw new InvalidDataException(
                    $"WinSaddleAnalyzer 配置文件包含无效排序状态: {settings.TrainedCharaSort}。");

            TrainedCharaSort = settings.TrainedCharaSort;
            OnlyFavourites = settings.OnlyFavourites;
            TargetHorseId = settings.TargetHorseId;
            ParentHorseId = settings.ParentHorseId;
        }

        void SaveSettings()
        {
            Directory.CreateDirectory(PLUGIN_DATA_DIRECTORY);
            var settings = new WinSaddleSettings(
                TrainedCharaSort,
                OnlyFavourites,
                TargetHorseId,
                ParentHorseId);
            File.WriteAllText(
                SETTINGS_FILEPATH,
                JsonConvert.SerializeObject(settings, Formatting.Indented, SettingsJson));
        }

        void RefreshParent()
        {
            Parent = TrainedChara.FirstOrDefault(x => x.trained_chara_id == ParentHorseId);
            if (Parent is not null)
                ApplyFactorExtend(Parent);
        }

        static string ReadBrotliUtf8(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"WinSaddleAnalyzer 缺少宿主同源数据库文件: {path}。请执行插件更新以下载 Assets/GameData/ja-JP/factor_effects.br。", path);

            using var input = File.OpenRead(path);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }

        void EnsureFactorEffectsLoaded()
        {
            if (FactorEffects.Count != 0)
                return;

            var factorEffects = JArray.Parse(ReadBrotliUtf8(FACTOR_EFFECT_FILEPATH));
            var regex = FactorEffectRegex();
            var localEffects = new Dictionary<int, string>();
            foreach (var effect in factorEffects)
            {
                var id = effect["index"]!.Value<int>();
                var text = effect["text"]?.ToString() ?? string.Empty;
                var skill = regex.Match(text).Groups[1].Value;
                if (!string.IsNullOrEmpty(skill))
                    localEffects.Add(id, skill.Replace("○", "◎"));
            }

            FactorEffects = localEffects;
        }

        sealed record WinSaddleSettings(
            [property: JsonProperty(Required = Required.Always)]
            TrainedCharaSortOrder TrainedCharaSort,
            [property: JsonProperty(Required = Required.Always)]
            bool OnlyFavourites,
            [property: JsonProperty(Required = Required.Always)]
            int TargetHorseId,
            [property: JsonProperty(Required = Required.Always)]
            int ParentHorseId);

        public enum TrainedCharaSortOrder
        {
            不排序,
            种马名升序,
            种马名降序,
            TrainedCharaId升序,
            TrainedCharaId降序,
            胜鞍加成升序,
            胜鞍加成降序,
            分数升序,
            分数降序,
        }

        sealed record DisplayResult(string Content, string? Warning = null);

        sealed record TrainedCharaRow(
            string Name,
            int TrainedCharaId,
            int WinSaddleBonus,
            int Score);

        void ShowPanel(string key, string title, DisplayResult result)
        {
            LiveDisplay.SetPanel(Workspace, key, title, LiveDisplayContent.Text(result.Content));
            if (result.Warning is { } warning)
                LiveDisplay.Notify(Workspace, warning, LiveDisplaySeverity.Warning);
        }

        [ResponseAnalyzer<GameApi.SingleMode.Start>]
        public ValueTask AnalyzeSingleModeStart(SingleModeStartResponse response)
        {
            var singleModeStartCommon = response.data.single_mode_start_common;
            if (singleModeStartCommon.add_trained_chara_array is null) return ValueTask.CompletedTask;

            var charaInfo = singleModeStartCommon.chara_info;
            TargetHorseId = int.Parse(charaInfo.card_id.ToString()[..4]);
            var successionTrainedCharaIdDad = charaInfo.succession_trained_chara_id_1;
            var successionTrainedCharaIdMom = charaInfo.succession_trained_chara_id_2;
            var addTrainedCharaArray = singleModeStartCommon.add_trained_chara_array;
            var rentalHorse = addTrainedCharaArray.FirstOrDefault(x => x.trained_chara_id == successionTrainedCharaIdDad)
                ?? addTrainedCharaArray.FirstOrDefault(x => x.trained_chara_id == successionTrainedCharaIdMom);
            rentalHorse ??= TrainedChara.FirstOrDefault(x => x.trained_chara_id == successionTrainedCharaIdDad)
                ?? TrainedChara.FirstOrDefault(x => x.trained_chara_id == successionTrainedCharaIdMom);
            var mineHorse = TrainedChara.FirstOrDefault(x => x.trained_chara_id != rentalHorse?.trained_chara_id && x.trained_chara_id == successionTrainedCharaIdDad)
                ?? TrainedChara.FirstOrDefault(x => x.trained_chara_id != rentalHorse?.trained_chara_id && x.trained_chara_id == successionTrainedCharaIdMom);
            if (rentalHorse != null && mineHorse != null)
            {
                ParentHorseId = mineHorse.trained_chara_id;
                SaveSettings();
                ApplyFactorExtend(rentalHorse);
                ApplyFactorExtend(mineHorse);
                ShowPanel("inheritance", "相性分析", BuildRelationDisplay(rentalHorse, mineHorse));
            }
            else
            {
                SaveSettings();
                const string warning = "未找到种马信息，请先查看一次殿堂马再尝试看相性。";
                ShowPanel("inheritance", "相性分析", new(warning, warning));
            }

            return ValueTask.CompletedTask;
        }

        [ResponseAnalyzer<GameApi.TrainedChara.Load>]
        public ValueTask AnalyzeTrainedCharaLoadResponse(TrainedCharaLoadResponse response)
        {
            var data = response.data;
            CurrentTrainedCharaData = data;
            TrainedChara = data.trained_chara_array;
            RefreshParent();
            File.WriteAllText(TRAINED_CHARA_FILEPATH, JsonConvert.SerializeObject(data.trained_chara_array));
            PublishTrainedCharaTable();
            return ValueTask.CompletedTask;
        }

        void PublishTrainedCharaTable()
        {
            if (CurrentTrainedCharaData is not { } data)
                return;

            LiveDisplay.SetPanel(
                Workspace,
                "trained-characters",
                "殿堂马",
                BuildTrainedCharaTable(data));
        }

        [ResponseAnalyzer<GameApi.Friend.Search>]
        public ValueTask AnalyzeFriendSearchResponse(FriendSearchResponse response)
        {
            var data = response.data;
            if (data.partner_chara_info_array is { Length: > 0 })
                ParseFriendSearchResponse(response);

            return ValueTask.CompletedTask;
        }

        [ResponseAnalyzer<GameApi.Friend.SimpleSearch>]
        public ValueTask AnalyzeFriendSimpleSearchResponse(FriendSimpleSearchResponse response)
        {
            if (response.data.user_info_summary.user_trained_chara_array is { Length: > 0 })
                ParseFriendSearchResponseSimple(response.data.user_info_summary);

            return ValueTask.CompletedTask;
        }

        LiveDisplayContent BuildTrainedCharaTable(TrainedCharaLoadResponse.CommonResponse data)
        {
            var favouriteIds = data.trained_chara_favorite_array
                .Select(x => x.trained_chara_id)
                .ToHashSet();
            var chara = OnlyFavourites
                ? data.trained_chara_array.Where(x => x.is_locked == 1 && favouriteIds.Contains(x.trained_chara_id))
                : data.trained_chara_array;
            var rows = new List<TrainedCharaRow>();
            foreach (var i in chara)
            {
                var charaWinSaddle = i.win_saddle_id_array.Intersect(Database.SaddleIds);
                var parentWinSaddle_a = i.succession_chara_array[0].win_saddle_id_array.Intersect(Database.SaddleIds);
                var parentWinSaddle_b = i.succession_chara_array[1].win_saddle_id_array.Intersect(Database.SaddleIds);
                var win_saddle = charaWinSaddle.Intersect(parentWinSaddle_a).Count() * 3
                    + charaWinSaddle.Intersect(parentWinSaddle_b).Count() * 3;
                rows.Add(new(
                    Database.Names.GetUmamusume(i.card_id).FullName,
                    i.trained_chara_id,
                    win_saddle,
                    i.rank_score));
            }

            var originalSnapshot = rows.ToArray();
            return new(() =>
            {
                var table = new TableView(CreateTrainedCharaSource(originalSnapshot))
                {
                    Width = Dim.Fill(),
                    Height = Dim.Fill(),
                    UseAllRowsForContentCalculation = true,
                };
                table.Style.AlwaysShowHeaders = true;
                var scheme = table.GetScheme();
                table.Style.HeaderScheme = new(scheme) { Focus = scheme.Normal };
                table.Style.ExpandLastColumn = false;
                table.Style.ColumnStyles[1] = new() { Alignment = Alignment.End };
                table.Style.ColumnStyles[2] = new() { Alignment = Alignment.End };
                table.Style.ColumnStyles[3] = new() { Alignment = Alignment.End };
                table.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;
                table.MouseEvent += (_, mouse) =>
                {
                    const MouseFlags leftClicks = MouseFlags.LeftButtonClicked
                        | MouseFlags.LeftButtonDoubleClicked
                        | MouseFlags.LeftButtonTripleClicked;
                    if ((mouse.Flags & leftClicks) != 0)
                    {
                        table.ScreenToCell(mouse.Position!.Value, out var headerColumn);
                        if (headerColumn is int column)
                        {
                            mouse.Handled = true;
                            TrainedCharaSort = NextTrainedCharaSort(TrainedCharaSort, column);
                            SaveSettings();
                            var selection = table.Value;
                            table.Table = CreateTrainedCharaSource(originalSnapshot);
                            table.Value = selection;
                            table.RowOffset = 0;
                            table.VerticalScrollBar.Value = 0;
                            return;
                        }
                    }

                    var scrollBar = table.VerticalScrollBar;
                    if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
                        scrollBar.Value += scrollBar.Increment;
                    else if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
                        scrollBar.Value -= scrollBar.Increment;
                    else
                        return;

                    mouse.Handled = true;
                };
                return table;
            });
        }

        EnumerableTableSource<TrainedCharaRow> CreateTrainedCharaSource(
            TrainedCharaRow[] originalSnapshot)
        {
            var sort = TrainedCharaSort;
            TrainedCharaRow[] rows = sort switch
            {
                TrainedCharaSortOrder.不排序 => [.. originalSnapshot],
                TrainedCharaSortOrder.种马名升序 => [..
                    originalSnapshot.OrderBy(row => row.Name, StringComparer.Ordinal)],
                TrainedCharaSortOrder.种马名降序 => [..
                    originalSnapshot.OrderByDescending(row => row.Name, StringComparer.Ordinal)],
                TrainedCharaSortOrder.TrainedCharaId升序 => [..
                    originalSnapshot.OrderBy(row => row.TrainedCharaId)],
                TrainedCharaSortOrder.TrainedCharaId降序 => [..
                    originalSnapshot.OrderByDescending(row => row.TrainedCharaId)],
                TrainedCharaSortOrder.胜鞍加成升序 => [..
                    originalSnapshot.OrderBy(row => row.WinSaddleBonus)],
                TrainedCharaSortOrder.胜鞍加成降序 => [..
                    originalSnapshot.OrderByDescending(row => row.WinSaddleBonus)],
                TrainedCharaSortOrder.分数升序 => [..
                    originalSnapshot.OrderBy(row => row.Score)],
                TrainedCharaSortOrder.分数降序 => [..
                    originalSnapshot.OrderByDescending(row => row.Score)],
                _ => throw new InvalidOperationException(
                    $"WinSaddleAnalyzer 排序状态无效: {sort}。"),
            };
            var activeColumn = sort == TrainedCharaSortOrder.不排序
                ? -1
                : ((int)sort - 1) / 2;
            var contentWidths = new[]
            {
                originalSnapshot.Select(row => row.Name.GetColumns()).DefaultIfEmpty().Max(),
                originalSnapshot.Select(row => row.TrainedCharaId.ToString().Length).DefaultIfEmpty().Max(),
                originalSnapshot.Select(row => row.WinSaddleBonus.ToString().Length).DefaultIfEmpty().Max(),
                originalSnapshot.Select(row => row.Score.ToString().Length).DefaultIfEmpty().Max(),
            };
            string Header(int column, string title)
            {
                var titleWidth = title.GetColumns();
                var width = Math.Max(titleWidth + 2, contentWidths[column]);
                var marker = column == activeColumn
                    ? (int)sort % 2 == 1 ? '▲' : '▼'
                    : ' ';
                return $"{title}{new string(' ', width - titleWidth - 1)}{marker}";
            }

            var columns = new Dictionary<string, Func<TrainedCharaRow, object>>
            {
                [Header(0, I18N_UmaName)] = row => row.Name,
                [Header(1, "TrainedCharaId")] = row => row.TrainedCharaId,
                [Header(2, I18N_WinSaddleBonus)] = row => row.WinSaddleBonus,
                [Header(3, I18N_Score)] = row => row.Score,
            };
            return new(rows, columns);
        }

        static TrainedCharaSortOrder NextTrainedCharaSort(
            TrainedCharaSortOrder current,
            int column)
        {
            var ascending = column switch
            {
                0 => TrainedCharaSortOrder.种马名升序,
                1 => TrainedCharaSortOrder.TrainedCharaId升序,
                2 => TrainedCharaSortOrder.胜鞍加成升序,
                3 => TrainedCharaSortOrder.分数升序,
                _ => throw new ArgumentOutOfRangeException(nameof(column), column, "未知的殿堂马表格列。"),
            };
            var descending = (TrainedCharaSortOrder)((int)ascending + 1);
            if (current == descending)
                return ascending;
            if (current == ascending)
                return TrainedCharaSortOrder.不排序;
            return descending;
        }

        ILiveDisplayOutput LiveDisplay => liveDisplay
            ?? throw new InvalidOperationException("WinSaddleAnalyzer 尚未初始化 LiveDisplay。");

        LiveDisplayWorkspace Workspace => workspace
            ??= LiveDisplay.CreateWorkspace(Name);

        [System.Text.RegularExpressions.GeneratedRegex("「(.*?)」のスキルヒント")]
        private static partial System.Text.RegularExpressions.Regex FactorEffectRegex();
    }
}
