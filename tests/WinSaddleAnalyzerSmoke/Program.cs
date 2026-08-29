using System.Drawing;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Gallop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Text;
using Terminal.Gui.Time;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Entities;
using UmamusumeResponseAnalyzer.TerminalGui;
using PluginType = WinSaddleAnalyzer.WinSaddleAnalyzer;
using SortOrder = WinSaddleAnalyzer.WinSaddleAnalyzer.TrainedCharaSortOrder;
using TColor = Terminal.Gui.Drawing.Color;
using TrainedCharaI18n = WinSaddleAnalyzer.i18n.ParseTrainedCharaLoadResponse;
using UraDatabase = UmamusumeResponseAnalyzer.Database;

var failures = new List<string>();
using var ui = new WorkspaceSmokeSession();

Check("project build contract", AssertProjectBuildContract);
Check("initialize without factor effect data", () => AssertInitializeDoesNotRequireFactorEffectData(ui));
await CheckAsync("workspace publications and persistence", () => AssertWorkspacePublicationsAndPersistence(ui));
await CheckAsync("trained-character sorting and configuration synchronization", () => AssertTrainedCharaSortingAndConfigurationSynchronization(ui));
Check("legacy sorting configuration fails fast", () => AssertLegacySortingConfigurationFailsFast(ui));
Check("configuration draft save and cancel", () => AssertConfigurationDraftSemantics(ui));
await CheckAsync("optional skill expected effects", () => AssertOptionalSkillExpectedEffects(ui));

if (failures.Count != 0)
{
    Console.Error.WriteLine("FAILED WinSaddleAnalyzer smoke:");
    foreach (var failure in failures)
        Console.Error.WriteLine($"- {failure}");

    Environment.Exit(1);
}

Console.WriteLine("PASS WinSaddleAnalyzer smoke");

void Check(string name, Action assertion)
{
    try
    {
        assertion();
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.GetBaseException().Message}");
    }
}

async Task CheckAsync(string name, Func<Task> assertion)
{
    try
    {
        await assertion();
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.GetBaseException().Message}");
    }
}

static void AssertProjectBuildContract()
{
    var repoRoot = FindRepositoryRoot();
    var pluginDir = Path.Combine(repoRoot, "WinSaddleAnalyzer");
    var projectPath = Path.Combine(pluginDir, "WinSaddleAnalyzer.csproj");
    var project = XDocument.Load(projectPath);

    Assert(
        project.Descendants("IsUraPlugin").SingleOrDefault()?.Value.Trim() == "true",
        "WinSaddleAnalyzer.csproj must opt into root URA plugin targets with IsUraPlugin=true.");

    var hostProjectReferences = project
        .Descendants("ProjectReference")
        .Select(x => x.Attribute("Include")?.Value)
        .Where(x => x?.Contains("UmamusumeResponseAnalyzer", StringComparison.OrdinalIgnoreCase) == true)
        .ToArray();
    Assert(
        hostProjectReferences.Length == 0,
        $"WinSaddleAnalyzer.csproj must not hand-write host ProjectReference entries: {string.Join(", ", hostProjectReferences)}");

    var packageReferences = project
        .Descendants("PackageReference")
        .Select(x => x.Attribute("Include")?.Value)
        .Where(x => x is "Gallop" or "UmamusumeResponseAnalyzer.Plugin.Abstractions" or "Spectre.Console")
        .ToArray();
    Assert(
        packageReferences.Length == 0,
        $"WinSaddleAnalyzer.csproj must not reference old host/UI packages: {string.Join(", ", packageReferences)}");

    Assert(
        !project.Descendants("None").Any(x => string.Equals(x.Attribute("Update")?.Value, "manifest.json", StringComparison.OrdinalIgnoreCase)),
        "WinSaddleAnalyzer.csproj must not copy a source manifest.json.");

    Assert(
        !project.Descendants("Target").Any(x => string.Equals(x.Attribute("Name")?.Value, "PostBuild", StringComparison.OrdinalIgnoreCase)),
        "WinSaddleAnalyzer.csproj must not use the old PostBuild LocalAppData copy.");

    Assert(
        !File.Exists(Path.Combine(pluginDir, "manifest.json")),
        "WinSaddleAnalyzer source manifest.json must be removed; root targets generate it during build.");
}

static void AssertInitializeDoesNotRequireFactorEffectData(WorkspaceSmokeSession ui)
{
    var originalCwd = Directory.GetCurrentDirectory();
    var workspace = Path.Combine(Path.GetTempPath(), "win-saddle-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspace);
    try
    {
        Directory.SetCurrentDirectory(workspace);
        ui.Bootstrap.SwitchTo();
        var before = ui.CaptureScreen();
        using var context = new RuntimePluginContext(ui.Application);
        var plugin = new PluginType();
        plugin.Initialize(context);
        Assert(
            ReferenceEquals(Workspace.Current, ui.Bootstrap)
            && string.Equals(before, ui.CaptureScreen(), StringComparison.Ordinal),
            "Initialize must not change the visible workspace or framebuffer.");
        plugin.Dispose();
        Assert(
            ReferenceEquals(Workspace.Current, ui.Bootstrap)
            && string.Equals(before, ui.CaptureScreen(), StringComparison.Ordinal),
            "Unused Dispose must not change the visible workspace or framebuffer.");
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCwd);
        Directory.Delete(workspace, recursive: true);
    }
}

static async Task AssertWorkspacePublicationsAndPersistence(WorkspaceSmokeSession ui)
{
    var originalCwd = Directory.GetCurrentDirectory();
    var workspace = Path.Combine(Path.GetTempPath(), "win-saddle-smoke-" + Guid.NewGuid().ToString("N"));
    PluginType? plugin = null;
    Workspace? target = null;
    Directory.CreateDirectory(workspace);
    try
    {
        Directory.SetCurrentDirectory(workspace);
        InitializeHostConfigForSmoke();
        await InitializeSmokeDatabase([
            new BaseName(1001, "短名", "短名"),
            new UmaName(100100, "[短]", "[短]", 1001),
            new BaseName(1002, "シリウスシンボリ", "シリウス"),
            new UmaName(100200, "[Féroce]", "[Féroce]", 1002)
        ], [1, 2]);
        var trainedCharas = CreateOverflowTrainedCharas();
        ui.Bootstrap.SwitchTo();
        var before = ui.CaptureScreen();
        using var context = new RuntimePluginContext(ui.Application);
        plugin = new PluginType { OnlyFavourites = false };
        plugin.Initialize(context);
        Assert(
            ReferenceEquals(Workspace.Current, ui.Bootstrap)
            && string.Equals(before, ui.CaptureScreen(), StringComparison.Ordinal),
            "Initialize must not change the visible workspace or framebuffer.");

        var trainedResponse = new TrainedCharaLoadResponse
        {
            data = new()
            {
                trained_chara_array = trainedCharas,
                trained_chara_favorite_array = [],
                room_match_entry_chara_id_array = []
            }
        };
        plugin.AnalyzeTrainedCharaLoadResponse(trainedResponse).GetAwaiter().GetResult();
        target = Workspace.Current is { Title: "WinSaddleAnalyzer" } published
            ? published
            : throw new InvalidOperationException("First valid output did not switch to WinSaddleAnalyzer.");
        Assert(
            ui.CaptureScreen().Contains("殿堂马", StringComparison.Ordinal),
            "First valid output must create and switch to the WinSaddleAnalyzer workspace.");
        AssertPublishedAlternatingRowBackgrounds(ui);

        plugin.AnalyzeSingleModeStart(new()
        {
            data = new()
            {
                single_mode_start_common = new()
                {
                    chara_info = new()
                    {
                        card_id = 100100,
                        succession_trained_chara_id_1 = 1,
                        succession_trained_chara_id_2 = 2
                    },
                    add_trained_chara_array = []
                }
            }
        }).GetAwaiter().GetResult();
        Assert(
            ReferenceEquals(Workspace.Current, target)
            && ui.CaptureScreen() is { } inheritanceFrame
            && inheritanceFrame.Contains("相性分析", StringComparison.Ordinal)
            && !inheritanceFrame.Contains("TrainedCharaId", StringComparison.Ordinal)
            && !inheritanceFrame.Contains("好友", StringComparison.Ordinal),
            "The inheritance analyzer must replace the trained-character output group.");

        plugin.AnalyzeFriendSimpleSearchResponse(CreateFriendSimpleSearchResponse()).GetAwaiter().GetResult();

        Assert(
            ReferenceEquals(Workspace.Create("winsaddleanalyzer"), target)
            && ReferenceEquals(Workspace.Current, target),
            "Repeated output and case-insensitive lookup must reuse the canonical WinSaddleAnalyzer workspace.");
        var visibleFriend = ui.CaptureScreen();
        Assert(
            visibleFriend.Contains("好友", StringComparison.Ordinal)
            && visibleFriend.Contains("WARN ", StringComparison.Ordinal)
            && !visibleFriend.Contains("TrainedCharaId", StringComparison.Ordinal)
            && !visibleFriend.Contains("相性分析", StringComparison.Ordinal),
            "The simple-friend output group must replace prior panels without clearing notifications.");

        plugin.AnalyzeFriendSearchResponse(CreateFriendSearchResponse()).GetAwaiter().GetResult();
        var fullFriendFrame = ui.CaptureScreen();
        Assert(
            ReferenceEquals(Workspace.Current, target)
            && fullFriendFrame.Contains("好友", StringComparison.Ordinal)
            && fullFriendFrame.Contains("相性分析", StringComparison.Ordinal)
            && !fullFriendFrame.Contains("TrainedCharaId", StringComparison.Ordinal),
            "The full-friend output group must contain friend and inheritance panels but no trained-character panel.");

        plugin.AnalyzeFriendSearchResponse(new()
        {
            data = new() { partner_chara_info_array = [] }
        }).GetAwaiter().GetResult();
        plugin.AnalyzeFriendSimpleSearchResponse(new()
        {
            data = new()
            {
                user_info_summary = new() { user_trained_chara_array = [] }
            }
        }).GetAwaiter().GetResult();
        plugin.AnalyzeSingleModeStart(new()
        {
            data = new() { single_mode_start_common = new() }
        }).GetAwaiter().GetResult();
        var unchangedFriendFrame = ui.CaptureScreen();
        Assert(
            ReferenceEquals(Workspace.Current, target)
            && unchangedFriendFrame.Contains("好友", StringComparison.Ordinal)
            && unchangedFriendFrame.Contains("相性分析", StringComparison.Ordinal)
            && !unchangedFriendFrame.Contains("TrainedCharaId", StringComparison.Ordinal),
            "Responses without valid output must preserve the current full-friend output group.");

        plugin.AnalyzeTrainedCharaLoadResponse(trainedResponse).GetAwaiter().GetResult();
        var trainedFrame = ui.CaptureScreen();
        Assert(
            ReferenceEquals(Workspace.Current, target)
            && trainedFrame.Contains("殿堂马", StringComparison.Ordinal)
            && trainedFrame.Contains("TrainedCharaId", StringComparison.Ordinal)
            && !trainedFrame.Contains("好友", StringComparison.Ordinal)
            && !trainedFrame.Contains("相性分析", StringComparison.Ordinal)
            && trainedFrame.Contains("[Féroce]シリウスシンボリ", StringComparison.Ordinal)
            && trainedFrame.Contains("[短]短名", StringComparison.Ordinal)
            && trainedFrame.Contains("700000", StringComparison.Ordinal),
            "The trained-character output group must replace friend panels and expose its real table.");
        AssertPublishedTableWheelScrolls(ui, trainedCharas[^1].trained_chara_id);

        var trainedCharaPath = Path.Combine(workspace, "PluginData", "WinSaddleAnalyzer", "trained_chara.json");
        Assert(File.Exists(trainedCharaPath), "Trained-character load must persist trained_chara.json.");
        var persistedTrainedCharas = JArray.Parse(File.ReadAllText(trainedCharaPath));
        Assert(
            persistedTrainedCharas.Count == trainedCharas.Length,
            "Persisted trained_chara.json must contain every overflowing table row.");
        Assert(
            persistedTrainedCharas
                .Select(x => x.Value<int>("trained_chara_id"))
                .SequenceEqual(trainedCharas.Select(x => x.trained_chara_id)),
            "Persisted trained_chara.json must preserve trained-character ids and input order.");

        var settingsPath = Path.Combine(workspace, "PluginData", "WinSaddleAnalyzer", "settings.json");
        Assert(File.Exists(settingsPath), "Start response without usable parents must persist settings.json.");
        var settings = JObject.Parse(File.ReadAllText(settingsPath));
        Assert(settings.Value<int>("TargetHorseId") == 1001, "Start response must persist the derived TargetHorseId.");
        Assert(settings.Value<int>("ParentHorseId") == 0, "Missing parents must preserve ParentHorseId.");

        plugin.Dispose();
        target.SwitchTo();
        var disposed = ui.CaptureScreen();
        Assert(
            ReferenceEquals(Workspace.Create("WINSADDLEANALYZER"), target),
            "Dispose must retain the canonical WinSaddleAnalyzer workspace generation.");
        Assert(
            disposed.Contains("WinSaddleAnalyzer 还没有输出。", StringComparison.Ordinal),
            "Dispose must explicitly remove every panel key published by WinSaddleAnalyzer.");
    }
    finally
    {
        plugin?.Dispose();
        if (target is not null)
        {
            target.RemovePanel("trained-characters");
            target.RemovePanel("inheritance");
            target.RemovePanel("friend");
        }
        Directory.SetCurrentDirectory(originalCwd);
        Directory.Delete(workspace, recursive: true);
    }
}

static async Task AssertOptionalSkillExpectedEffects(WorkspaceSmokeSession ui)
{
    const int factorId = 10_000_001;
    var originalCwd = Directory.GetCurrentDirectory();
    var workspace = Path.Combine(Path.GetTempPath(), "win-saddle-skill-effects-smoke-" + Guid.NewGuid().ToString("N"));
    PluginType? plugin = null;
    Workspace? target = null;
    Directory.CreateDirectory(workspace);
    try
    {
        Directory.SetCurrentDirectory(workspace);
        InitializeHostConfigForSmoke();
        await InitializeSmokeDatabase([
            new BaseName(1001, "测试马一", "测试马一"),
            new UmaName(100100, "[一]", "[一]", 1001),
            new BaseName(1002, "测试马二", "测试马二"),
            new UmaName(100200, "[二]", "[二]", 1002),
            new BaseName(1003, "测试马三", "测试马三"),
            new UmaName(100300, "[三]", "[三]", 1003),
            new BaseName(1004, "测试马四", "测试马四"),
            new UmaName(100400, "[四]", "[四]", 1004)
        ], [], new() { [factorId] = "测试技能因子" });

        ui.Bootstrap.SwitchTo();
        using var context = new RuntimePluginContext(ui.Application);
        plugin = new PluginType
        {
            OnlyFavourites = false,
            ParentHorseId = 900_001
        };
        plugin.Initialize(context);
        plugin.AnalyzeTrainedCharaLoadResponse(new()
        {
            data = new()
            {
                trained_chara_array = [CreateSkillEffectTrainedChara(900_001, 100100, factorId)],
                trained_chara_favorite_array = [],
                room_match_entry_chara_id_array = []
            }
        }).GetAwaiter().GetResult();
        target = Workspace.Current is { Title: "WinSaddleAnalyzer" } published
            ? published
            : throw new InvalidOperationException("Skill-effect smoke did not publish its trained-character fixture.");
        var friendResponse = CreateSkillEffectFriendSearchResponse(factorId);
        var existingWarningCount = ui.CaptureScreen(160, 50)
            .Split("WARN ", StringSplitOptions.None)
            .Length - 1;

        plugin.AnalyzeFriendSearchResponse(friendResponse).GetAwaiter().GetResult();
        AssertSkillEffectsUnavailable(ui, target, existingWarningCount, "missing SkillEffectPlugin settings");

        var skillEffectDirectory = Path.Combine(workspace, "PluginData", "SkillEffectPlugin");
        var settingsPath = Path.Combine(skillEffectDirectory, "settings.json");
        WriteSkillEffectSettings(settingsPath, string.Empty, string.Empty);
        plugin.AnalyzeFriendSearchResponse(friendResponse).GetAwaiter().GetResult();
        AssertSkillEffectsUnavailable(ui, target, existingWarningCount, "blank Race and RunningStyle");

        WriteSkillEffectSettings(settingsPath, "missing-course", "missing-style");
        plugin.AnalyzeFriendSearchResponse(friendResponse).GetAwaiter().GetResult();
        AssertSkillEffectsUnavailable(ui, target, existingWarningCount, "missing selected skill-effect table");

        File.WriteAllText(settingsPath, "{");
        AssertThrows<System.Text.Json.JsonException>(
            () => plugin.AnalyzeFriendSearchResponse(friendResponse).GetAwaiter().GetResult(),
            "Malformed SkillEffectPlugin settings must fail explicitly.");

        const string race = "test-course";
        const string runningStyle = "test-style";
        WriteSkillEffectSettings(settingsPath, race, runningStyle);
        var effectDirectory = Path.Combine(skillEffectDirectory, race);
        Directory.CreateDirectory(effectDirectory);
        var effectPath = Path.Combine(effectDirectory, $"{runningStyle}.json");

        File.WriteAllText(effectPath, "{");
        AssertThrows<System.Text.Json.JsonException>(
            () => plugin.AnalyzeFriendSearchResponse(friendResponse).GetAwaiter().GetResult(),
            "Malformed skill-effect table JSON must fail explicitly.");

        File.WriteAllText(effectPath, "[]");
        AssertThrows<InvalidDataException>(
            () => plugin.AnalyzeFriendSearchResponse(friendResponse).GetAwaiter().GetResult(),
            "An empty skill-effect table must fail explicitly.");

        File.WriteAllText(effectPath, "[{\"Name\":\"\",\"Effect\":\"1.50\"}]");
        AssertThrows<InvalidDataException>(
            () => plugin.AnalyzeFriendSearchResponse(friendResponse).GetAwaiter().GetResult(),
            "A skill-effect table with an invalid entry must fail explicitly.");

        WriteBrotliJson(
            Path.Combine(workspace, "factor_effects.br"),
            new[] { new { index = factorId, text = "「测试技能」のスキルヒント" } });
        File.WriteAllText(effectPath, "[{\"Name\":\"测试技能\",\"Effect\":\"1.50\"}]");
        plugin.AnalyzeFriendSearchResponse(friendResponse).GetAwaiter().GetResult();
        var recovered = ui.CaptureScreen(160, 50);
        Assert(
            ReferenceEquals(Workspace.Current, target)
            && recovered.Contains("好友技能期望收益", StringComparison.Ordinal)
            && recovered.Contains("自己技能期望收益", StringComparison.Ordinal),
            "The same WinSaddleAnalyzer instance must retry unavailable skill effects and recover after valid files appear.");
    }
    finally
    {
        plugin?.Dispose();
        if (target is not null)
        {
            target.RemovePanel("trained-characters");
            target.RemovePanel("inheritance");
            target.RemovePanel("friend");
        }
        Directory.SetCurrentDirectory(originalCwd);
        Directory.Delete(workspace, recursive: true);
    }
}

static void AssertSkillEffectsUnavailable(
    WorkspaceSmokeSession ui,
    Workspace target,
    int expectedWarningCount,
    string operation)
{
    var screen = ui.CaptureScreen(160, 50);
    Assert(
        ReferenceEquals(Workspace.Current, target)
        && screen.Contains("好友总相性", StringComparison.Ordinal)
        && screen.Contains("单次继承概率", StringComparison.Ordinal)
        && screen.Contains("两次继承概率", StringComparison.Ordinal)
        && !screen.Contains("技能期望收益", StringComparison.Ordinal)
        && screen.Split("WARN ", StringSplitOptions.None).Length - 1 == expectedWarningCount,
        $"{operation} must publish the core inheritance result without skill-effect rows or new warnings.");
}

static TrainedChara[] CreateOverflowTrainedCharas()
    => Enumerable.Range(0, 64)
        .Select(index => new TrainedChara
        {
            trained_chara_id = 700_000 + index,
            card_id = index % 2 == 0 ? 100200 : 100100,
            rank_score = 880_000 - index,
            is_locked = 1,
            win_saddle_id_array = [1, 2],
            succession_chara_array =
            [
                new() { win_saddle_id_array = [1, 2] },
                new() { win_saddle_id_array = [1, 2] }
            ],
            create_time = $"2026-07-26 00:00:{index:00}"
        })
        .ToArray();

static void AssertPublishedTableWheelScrolls(WorkspaceSmokeSession ui, int finalTrainedCharaId)
{
    var before = ui.CaptureScreen();
    Assert(
        !before.Contains(finalTrainedCharaId.ToString(), StringComparison.Ordinal),
        "The overflow fixture must begin with its final trained-character row outside the visible framebuffer.");
    var point = FindScreenPoint(before, "TrainedCharaId", 0, 2);
    var after = InteractWithWinSaddle(ui, application =>
    {
        for (var i = 0; i < 80; i++)
        {
            application.Mouse.RaiseMouseEvent(new()
            {
                ScreenPosition = point,
                Flags = MouseFlags.WheeledDown
            });
        }
    });
    Assert(
        after.Text.Contains(finalTrainedCharaId.ToString(), StringComparison.Ordinal)
        && !after.Text.Contains("700000", StringComparison.Ordinal),
        "Mouse wheel input on the real published table must reach the final row and move the first row out of view.");
}

static void AssertPublishedAlternatingRowBackgrounds(WorkspaceSmokeSession ui)
{
    var frame = InteractWithWinSaddle(ui, static _ => { });
    var darkRow = new TColor(14, 14, 14);
    var alternateRow = new TColor(41, 55, 67);
    var headerStarts = GetPublishedHeaderStarts(
        frame,
        [
            TrainedCharaI18n.I18N_UmaName,
            "TrainedCharaId",
            TrainedCharaI18n.I18N_WinSaddleBonus,
            TrainedCharaI18n.I18N_Score
        ]);
    var verticalLine = Glyphs.VLine.ToString();
    var headerY = FindScreenPoint(frame.Text, "TrainedCharaId").Y;
    var internalColumns = Enumerable.Range(
        headerStarts[0],
        headerStarts[^1] - headerStarts[0]);
    Assert(
        headerStarts.Skip(1).All(x => frame.Cells[headerY, x - 1].Grapheme == verticalLine),
        "The trained-character header must retain every internal vertical column separator.");
    foreach (var (y, _) in GetPublishedTrainedRows(frame, headerStarts))
    {
        Assert(
            internalColumns.All(x => frame.Cells[y, x].Grapheme is not "|"
                && frame.Cells[y, x].Grapheme != verticalLine),
            $"Published trained-character data row {y} must not contain vertical cell separators.");
    }

    var nameX = FindScreenPoint(frame.Text, "[短]短名").X;
    var darkRowPoint = FindScreenPoint(frame.Text, "700001");
    var alternateRowPoint = FindScreenPoint(frame.Text, "700002");
    var sampleColumns = new[]
    {
        nameX,
        darkRowPoint.X,
        headerStarts[2],
        headerStarts[3]
    };

    foreach (var x in sampleColumns)
    {
        Assert(
            frame.Cells[darkRowPoint.Y, x].Attribute?.Background == darkRow,
            $"Odd trained-character row background at ({x}, {darkRowPoint.Y}) must be {darkRow}.");
        Assert(
            frame.Cells[alternateRowPoint.Y, x].Attribute?.Background == alternateRow,
            $"Even trained-character row background at ({x}, {alternateRowPoint.Y}) must be {alternateRow}.");
    }

    var selectedName = FindScreenPoint(frame.Text, "[Féroce]シリウスシンボリ");
    var selectedId = FindScreenPoint(frame.Text, "700000");
    Assert(
        frame.Cells[selectedId.Y, selectedId.X].Attribute?.Background == alternateRow
        && frame.Cells[selectedName.Y, selectedName.X].Attribute?.Background != alternateRow,
        "The selected name cell must retain the Host focus scheme while the rest of its row keeps the alternating background.");

    var trailingX = frame.Cells.GetLength(1) - 3;
    Assert(
        frame.Cells[darkRowPoint.Y, trailingX].Attribute?.Background is { } darkTrailing
        && darkTrailing != darkRow
        && darkTrailing != alternateRow
        && frame.Cells[alternateRowPoint.Y, trailingX].Attribute?.Background is { } alternateTrailing
        && alternateTrailing != darkRow
        && alternateTrailing != alternateRow,
        "Alternating backgrounds must stop at the last data column and leave the trailing table viewport unchanged.");
}

static WinSaddleFrame InteractWithWinSaddle(
    WorkspaceSmokeSession ui,
    Action<IApplication> input,
    int screenHeight = 40)
{
    ui.Flush();
    var completion = new TaskCompletionSource<WinSaddleFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
    ui.Application.Invoke(() =>
    {
        try
        {
            var driver = ui.Application.Driver
                ?? throw new InvalidOperationException("The real Host driver was not initialized.");
            WinSaddleFrame capture;
            try
            {
                driver.SetScreenSize(120, screenHeight);
                ui.Application.LayoutAndDraw(forceRedraw: true);
                input(ui.Application);
                ui.Application.LayoutAndDraw(forceRedraw: true);
                capture = new(
                    driver.ToString(),
                    (Cell[,])(driver.Contents
                        ?? throw new InvalidOperationException("The real Host framebuffer was not initialized."))
                    .Clone());
            }
            finally
            {
                driver.SetScreenSize(120, 40);
                ui.Application.LayoutAndDraw(forceRedraw: true);
            }

            completion.SetResult(capture);
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
    });
    return completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
}

static Point FindScreenPoint(string screen, string text, int xOffset = 0, int yOffset = 0)
{
    var lines = screen.ReplaceLineEndings("\n").Split('\n');
    for (var y = 0; y < lines.Length; y++)
    {
        var index = lines[y].IndexOf(text, StringComparison.Ordinal);
        if (index >= 0)
            return new(lines[y][..index].GetColumns() + xOffset, y + yOffset);
    }

    throw new InvalidOperationException($"The real WinSaddle framebuffer is missing '{text}'.");
}

static async Task AssertTrainedCharaSortingAndConfigurationSynchronization(WorkspaceSmokeSession ui)
{
    var originalCwd = Directory.GetCurrentDirectory();
    var workspace = Path.Combine(Path.GetTempPath(), "win-saddle-sort-smoke-" + Guid.NewGuid().ToString("N"));
    PluginType? plugin = null;
    PluginType? restarted = null;
    Workspace? target = null;
    Directory.CreateDirectory(workspace);
    try
    {
        Directory.SetCurrentDirectory(workspace);
        InitializeHostConfigForSmoke();
        await InitializeSmokeDatabase([
            new BaseName(1001, "短名", "短名"),
            new UmaName(100100, "[短]", "[短]", 1001),
            new BaseName(1002, "シリウスシンボリ", "シリウス"),
            new UmaName(100200, "[Féroce]", "[Féroce]", 1002),
            new BaseName(1003, "アルファ", "アルファ"),
            new UmaName(100300, "[Alpha]", "[Alpha]", 1003),
            new BaseName(1004, "ゼータ", "ゼータ"),
            new UmaName(100400, "[Zulu]", "[Zulu]", 1004)
        ], [1, 2, 3, 4]);

        var trainedCharas = CreateSortingTrainedCharas();
        var response = new TrainedCharaLoadResponse
        {
            data = new()
            {
                trained_chara_array = trainedCharas,
                trained_chara_favorite_array = [],
                room_match_entry_chara_id_array = []
            }
        };
        using var context = new RuntimePluginContext(ui.Application);
        plugin = new PluginType { OnlyFavourites = false };
        plugin.Initialize(context);
        plugin.AnalyzeTrainedCharaLoadResponse(response).GetAwaiter().GetResult();

        var settingsPath = Path.Combine(workspace, "PluginData", "WinSaddleAnalyzer", "settings.json");
        target = Workspace.Current is { Title: "WinSaddleAnalyzer" } current
            ? current
            : throw new InvalidOperationException("Trained-character output did not switch to its visible Workspace.");
        var hostPopovers = ui.Application.Popovers
            ?? throw new InvalidOperationException("The Host application has no popover manager.");
        var baselinePopoverCount = hostPopovers.Popovers.Count;
        AssertHeaderSorting(ui, plugin, settingsPath);
        Assert(plugin.TrainedCharaSort == SortOrder.胜鞍加成降序, "Column switching must leave the runtime state on win-saddle descending.");
        AssertPublishedTrainedState(ui, target, SortOrder.胜鞍加成降序, "Column switching");
        AssertTrainedCharaContextMenu(ui, plugin, settingsPath, baselinePopoverCount);

        using var application = Terminal.Gui.App.Application.Create(new VirtualTimeProvider())
            .Init(DriverRegistry.Names.ANSI);
        application.Driver!.SetScreenSize(100, 30);

        plugin.AnalyzeFriendSimpleSearchResponse(CreateFriendSimpleSearchResponse()).GetAwaiter().GetResult();
        var beforeSaveScreen = ui.CaptureScreen();
        Assert(
            beforeSaveScreen.Contains("好友", StringComparison.Ordinal)
            && !beforeSaveScreen.Contains("TrainedCharaId", StringComparison.Ordinal),
            "The config refresh fixture must begin on the simple-friend output group.");
        OnNextConfigDialog(application, _ =>
        {
            ClickVisible(application, "分数降序");
            ClickVisible(application, "保存");
        });
        plugin.ConfigPromptAsync(application).GetAwaiter().GetResult();

        Assert(plugin.TrainedCharaSort == SortOrder.分数降序, "Config Save must update the runtime sort state.");
        AssertSettingsSort(settingsPath, SortOrder.分数降序, "Config Save");
        var savedScreen = ui.CaptureScreen();
        Assert(
            ReferenceEquals(Workspace.Current, target)
            && !string.Equals(savedScreen, beforeSaveScreen, StringComparison.Ordinal)
            && !savedScreen.Contains("好友", StringComparison.Ordinal),
            "Config Save must replace the friend output group with the trained-character panel.");
        AssertPublishedTrainedState(ui, target, SortOrder.分数降序, "Config Save");
        Assert(
            hostPopovers.Popovers.Count == baselinePopoverCount,
            "Replacing the trained-character panel must de-register its context menu.");
        AssertSingleContextMenuSelection(ui, plugin, settingsPath, column: 1, rowIndex: 0);
        Assert(
            hostPopovers.Popovers.Count == baselinePopoverCount + 1,
            "The republished trained-character table must register only one context menu.");

        var baseline = File.ReadAllBytes(settingsPath);
        OnNextConfigDialog(application, _ =>
        {
            ClickVisible(application, "TrainedCharaId升序");
            ClickVisible(application, "取消");
        });
        AssertCanceled(plugin.ConfigPromptAsync(application), "Cancel must cancel the sorting draft.");
        AssertUnchangedSortDraft(ui, target, plugin, settingsPath, baseline, "Cancel");

        OnNextConfigDialog(application, _ =>
        {
            ClickVisible(application, "种马名降序");
            application.Keyboard.RaiseKeyDownEvent(Key.Esc);
        });
        AssertCanceled(plugin.ConfigPromptAsync(application), "Esc must cancel the sorting draft.");
        AssertUnchangedSortDraft(ui, target, plugin, settingsPath, baseline, "Esc");

        OnNextConfigDialog(application, dialog =>
        {
            ClickVisible(application, "种马名升序");
            application.RequestStop(dialog);
        });
        AssertCanceled(plugin.ConfigPromptAsync(application), "Closing must cancel the sorting draft.");
        AssertUnchangedSortDraft(ui, target, plugin, settingsPath, baseline, "Close");

        plugin.Dispose();
        ui.Flush();
        Assert(
            hostPopovers.Popovers.Count == baselinePopoverCount,
            "Disposing WinSaddleAnalyzer must de-register the trained-character context menu.");
        target.SwitchTo();
        Assert(
            ui.CaptureScreen().Contains("WinSaddleAnalyzer 还没有输出。", StringComparison.Ordinal),
            "Dispose must explicitly remove the trained-character panel.");
        using var restartedContext = new RuntimePluginContext(ui.Application);
        restarted = new PluginType();
        restarted.Initialize(restartedContext);
        Assert(restarted.TrainedCharaSort == SortOrder.分数降序, "Plugin restart must load the persisted sort state.");
        restarted.AnalyzeTrainedCharaLoadResponse(response).GetAwaiter().GetResult();
        AssertPublishedTrainedState(ui, target, SortOrder.分数降序, "Plugin restart");
        restarted.Dispose();
    }
    finally
    {
        restarted?.Dispose();
        plugin?.Dispose();
        target?.RemovePanel("trained-characters");
        Directory.SetCurrentDirectory(originalCwd);
        Directory.Delete(workspace, recursive: true);
    }
}

static TrainedChara[] CreateSortingTrainedCharas()
{
    var rows = new (int Id, int CardId, int Score, int[] ParentSaddles)[]
    {
        (40, 100400, 200, [1, 2, 3]),
        (2, 100300, 6, []),
        (100, 100200, 1000, [1, 2]),
        (20, 100300, 50, [1, 2, 3, 4]),
        (5, 100100, 300, [1]),
        (30, 100200, 40, [1, 2]),
        (10, 100400, 2, []),
        (200, 100100, 500, [1, 2, 3]),
        (3, 100300, 30, [1]),
        (50, 100200, 10, [1, 2, 3, 4]),
        (1, 100400, 100, [1, 2]),
        (60, 100100, 20, [1]),
    };
    return rows.Select(row => new TrainedChara
    {
        trained_chara_id = row.Id,
        card_id = row.CardId,
        rank_score = row.Score,
        is_locked = 1,
        win_saddle_id_array = [1, 2, 3, 4],
        succession_chara_array =
        [
            new() { win_saddle_id_array = row.ParentSaddles },
            new() { win_saddle_id_array = [] }
        ],
        create_time = $"2026-07-26 01:00:{row.Id:00}"
    }).ToArray();
}

static void AssertHeaderSorting(
    WorkspaceSmokeSession ui,
    PluginType plugin,
    string settingsPath)
{
    if (Workspace.Current is not { Title: "WinSaddleAnalyzer" })
        throw new InvalidOperationException("The trained-character sorting panel is not the current real Workspace.");
    var unsortedIds = new[] { 40, 2, 100, 20, 5, 30, 10, 200, 3, 50, 1, 60 };
    var cases = new[]
    {
        (
            Title: TrainedCharaI18n.I18N_UmaName,
            Column: 0,
            Ascending: SortOrder.种马名升序,
            Descending: SortOrder.种马名降序,
            AscendingIds: new[] { 2, 20, 3, 100, 30, 50, 40, 10, 1, 5, 200, 60 },
            DescendingIds: new[] { 5, 200, 60, 40, 10, 1, 100, 30, 50, 2, 20, 3 }),
        (
            Title: "TrainedCharaId",
            Column: 1,
            Ascending: SortOrder.TrainedCharaId升序,
            Descending: SortOrder.TrainedCharaId降序,
            AscendingIds: new[] { 1, 2, 3, 5, 10, 20, 30, 40, 50, 60, 100, 200 },
            DescendingIds: new[] { 200, 100, 60, 50, 40, 30, 20, 10, 5, 3, 2, 1 }),
        (
            Title: TrainedCharaI18n.I18N_WinSaddleBonus,
            Column: 2,
            Ascending: SortOrder.胜鞍加成升序,
            Descending: SortOrder.胜鞍加成降序,
            AscendingIds: new[] { 2, 10, 5, 3, 60, 100, 30, 1, 40, 200, 20, 50 },
            DescendingIds: new[] { 20, 50, 40, 200, 100, 30, 1, 5, 3, 60, 2, 10 }),
        (
            Title: TrainedCharaI18n.I18N_Score,
            Column: 3,
            Ascending: SortOrder.分数升序,
            Descending: SortOrder.分数降序,
            AscendingIds: new[] { 10, 2, 50, 60, 3, 30, 20, 1, 40, 5, 200, 100 },
            DescendingIds: new[] { 100, 200, 5, 40, 1, 20, 30, 3, 60, 50, 2, 10 })
    };
    var titles = cases.Select(test => test.Title).ToArray();
    var initial = InteractWithWinSaddle(ui, static _ => { }, 60);
    var headerStarts = GetPublishedHeaderStarts(initial, titles);
    AssertPublishedHeaderState(initial, titles, headerStarts, null, null, "Initial unsorted table");
    AssertPublishedRowOrder(initial, headerStarts, unsortedIds, "Initial unsorted table");

    foreach (var test in cases)
    {
        var descending = ClickPublishedHeader(ui, test.Title, MouseFlags.LeftButtonClicked, 60);
        Assert(plugin.TrainedCharaSort == test.Descending, $"{test.Title} first click must select descending order.");
        AssertSettingsSort(settingsPath, test.Descending, $"{test.Title} first click");
        AssertPublishedHeaderState(descending, titles, headerStarts, test.Column, '▼', $"{test.Title} descending");
        AssertPublishedRowOrder(descending, headerStarts, test.DescendingIds, $"{test.Title} descending");

        var ascending = ClickPublishedHeader(ui, test.Title, MouseFlags.LeftButtonDoubleClicked, 60);
        Assert(plugin.TrainedCharaSort == test.Ascending, $"{test.Title} second click must select ascending order.");
        AssertSettingsSort(settingsPath, test.Ascending, $"{test.Title} second click");
        AssertPublishedHeaderState(ascending, titles, headerStarts, test.Column, '▲', $"{test.Title} ascending");
        AssertPublishedRowOrder(ascending, headerStarts, test.AscendingIds, $"{test.Title} ascending");

        var scrolled = ScrollPublishedTable(ui, test.Title);
        AssertPublishedHeaderState(scrolled, titles, headerStarts, test.Column, '▲', $"{test.Title} scrolled ascending");
        var scrolledIds = GetPublishedTrainedIds(scrolled, headerStarts);
        var scrolledOffset = Array.IndexOf(test.AscendingIds, scrolledIds.FirstOrDefault());
        Assert(
            scrolledIds.Length > 0
            && scrolledOffset > 0
            && scrolledIds.SequenceEqual(test.AscendingIds.Skip(scrolledOffset).Take(scrolledIds.Length)),
            $"{test.Title} wheel input must move the real sorted viewport away from its first row.");

        var canceled = ClickPublishedHeader(ui, test.Title, MouseFlags.LeftButtonTripleClicked, 16);
        Assert(plugin.TrainedCharaSort == SortOrder.不排序, $"{test.Title} third click must cancel sorting.");
        AssertSettingsSort(settingsPath, SortOrder.不排序, $"{test.Title} third click");
        AssertPublishedHeaderState(canceled, titles, headerStarts, null, null, $"{test.Title} canceled");
        var canceledIds = GetPublishedTrainedIds(canceled, headerStarts);
        Assert(
            canceledIds.Length > 0
            && canceledIds.SequenceEqual(unsortedIds.Take(canceledIds.Length)),
            $"{test.Title} third click must reset the visible viewport to the first unsorted row.");
        var expandedCanceled = InteractWithWinSaddle(ui, static _ => { }, 60);
        AssertPublishedHeaderState(expandedCanceled, titles, headerStarts, null, null, $"{test.Title} expanded canceled");
        AssertPublishedRowOrder(expandedCanceled, headerStarts, unsortedIds, $"{test.Title} canceled");
    }

    var nameDescending = ClickPublishedHeader(ui, cases[0].Title, MouseFlags.LeftButtonClicked, 60);
    AssertPublishedHeaderState(nameDescending, titles, headerStarts, 0, '▼', "Name descending before column switch");
    AssertPublishedRowOrder(nameDescending, headerStarts, cases[0].DescendingIds, "Name descending before column switch");
    var switched = ClickPublishedHeader(ui, cases[2].Title, MouseFlags.LeftButtonClicked, 60);
    Assert(plugin.TrainedCharaSort == SortOrder.胜鞍加成降序, "Switching columns must start the new column in descending order.");
    AssertSettingsSort(settingsPath, SortOrder.胜鞍加成降序, "Column switch");
    AssertPublishedHeaderState(switched, titles, headerStarts, 2, '▼', "Column switch");
    AssertPublishedRowOrder(switched, headerStarts, cases[2].DescendingIds, "Column switch");
}

static void AssertTrainedCharaContextMenu(
    WorkspaceSmokeSession ui,
    PluginType plugin,
    string settingsPath,
    int baselinePopoverCount)
{
    for (var column = 0; column < 4; column++)
        AssertSingleContextMenuSelection(ui, plugin, settingsPath, column, column);
    ScrollPublishedTable(ui, "TrainedCharaId");
    AssertSingleContextMenuSelection(ui, plugin, settingsPath, column: 3, rowIndex: 0);

    var popovers = ui.Application.Popovers
        ?? throw new InvalidOperationException("The Host application has no popover manager.");
    Assert(
        popovers.Popovers.Count == baselinePopoverCount + 1,
        "Repeated row context actions must reuse one registered popover.");

    var frame = InteractWithWinSaddle(ui, static _ => { }, 60);
    var titles = new[]
    {
        TrainedCharaI18n.I18N_UmaName,
        "TrainedCharaId",
        TrainedCharaI18n.I18N_WinSaddleBonus,
        TrainedCharaI18n.I18N_Score,
    };
    var headerStarts = GetPublishedHeaderStarts(frame, titles);
    var rows = GetPublishedTrainedRows(frame, headerStarts);
    Assert(rows.Length != 0, "The context-menu fixture must expose at least one trained-character row.");
    var headerY = FindScreenPoint(frame.Text, "TrainedCharaId").Y;
    var trailingX = frame.Cells.GetLength(1) - 3;
    var scrollBarX = frame.Cells.GetLength(1) - 1;
    AssertNoContextMenuAt(
        ui,
        plugin,
        settingsPath,
        new(headerStarts[0], headerY),
        "header");
    AssertNoContextMenuAt(
        ui,
        plugin,
        settingsPath,
        new(trailingX, rows[0].Y),
        "trailing blank viewport");
    AssertNoContextMenuAt(
        ui,
        plugin,
        settingsPath,
        new(scrollBarX, rows[0].Y),
        "vertical scrollbar");
}

static int AssertSingleContextMenuSelection(
    WorkspaceSmokeSession ui,
    PluginType plugin,
    string settingsPath,
    int column,
    int rowIndex)
{
    const string menuText = "设为种马分析的 TrainedCharaId";
    var before = InteractWithWinSaddle(ui, static _ => { }, 60);
    var titles = new[]
    {
        TrainedCharaI18n.I18N_UmaName,
        "TrainedCharaId",
        TrainedCharaI18n.I18N_WinSaddleBonus,
        TrainedCharaI18n.I18N_Score,
    };
    var headerStarts = GetPublishedHeaderStarts(before, titles);
    var rows = GetPublishedTrainedRows(before, headerStarts);
    Assert(
        rowIndex >= 0 && rowIndex < rows.Length,
        $"The context-menu fixture has no visible row {rowIndex}.");
    var expectedId = rows[rowIndex].Id;
    var point = new Point(headerStarts[column], rows[rowIndex].Y);
    var after = InteractWithWinSaddle(ui, application =>
    {
        application.Mouse.RaiseMouseEvent(new()
        {
            ScreenPosition = point,
            Flags = MouseFlags.RightButtonClicked,
        });
        application.LayoutAndDraw(forceRedraw: true);

        Assert(
            application.Popovers?.GetActivePopover() is PopoverMenu { Visible: true },
            $"Right-clicking trained-character column {column} must open a popover menu.");
        var table = Descendants(
                application.TopRunnableView
                    ?? throw new InvalidOperationException("The Host has no active runnable view."))
            .OfType<TableView>()
            .Single(candidate => candidate.Table is { } source
                && source.ColumnNames.Any(
                    name => name.Contains("TrainedCharaId", StringComparison.Ordinal)));
        var tableSource = table.Table
            ?? throw new InvalidOperationException("The trained-character table has no source.");
        var selectedCell = table.Value?.SelectedCell
            ?? throw new InvalidOperationException("The trained-character table has no selected cell.");
        Assert(
            selectedCell.X == column
            && Convert.ToInt32(tableSource[selectedCell.Y, 1]) == expectedId,
            $"Right-clicking column {column} must select trained-character {expectedId}.");

        var menuScreen = application.Driver?.ToString()
            ?? throw new InvalidOperationException("The Host driver is unavailable while the context menu is open.");
        Assert(
            menuScreen.Contains(menuText, StringComparison.Ordinal),
            "The trained-character context menu must expose its configured action.");
        application.Keyboard.RaiseKeyDownEvent(Key.Enter);
    }, 60);

    var menuStillVisible = after.Text.Contains(menuText, StringComparison.Ordinal);
    var successTextVisible = after.Text.Contains("已设置", StringComparison.Ordinal);
    var successSeverityVisible = after.Text.Contains("OK ", StringComparison.Ordinal);
    Assert(
        !menuStillVisible && !successTextVisible && !successSeverityVisible,
        $"The context action must close silently without a success notification. "
        + $"menu={menuStillVisible}, text={successTextVisible}, severity={successSeverityVisible}.");
    Assert(
        plugin.ParentHorseId == expectedId,
        $"The context action must set runtime ParentHorseId to {expectedId}.");
    Assert(
        JObject.Parse(File.ReadAllText(settingsPath)).Value<int>("ParentHorseId") == expectedId,
        $"The context action must persist ParentHorseId {expectedId}.");
    Assert(
        GetRefreshedParentId() == expectedId,
        $"The context action must refresh Parent to trained-character {expectedId}.");
    return expectedId;
}

static void AssertNoContextMenuAt(
    WorkspaceSmokeSession ui,
    PluginType plugin,
    string settingsPath,
    Point point,
    string operation)
{
    const string menuText = "设为种马分析的 TrainedCharaId";
    var expectedParentHorseId = plugin.ParentHorseId;
    var baseline = File.ReadAllBytes(settingsPath);
    var frame = InteractWithWinSaddle(ui, application =>
    {
        application.Mouse.RaiseMouseEvent(new()
        {
            ScreenPosition = point,
            Flags = MouseFlags.RightButtonClicked,
        });
        application.LayoutAndDraw(forceRedraw: true);
        Assert(
            application.Popovers?.GetActivePopover() is not { Visible: true },
            $"Right-clicking the {operation} must not open a popover.");
    }, 60);
    Assert(
        !frame.Text.Contains(menuText, StringComparison.Ordinal),
        $"Right-clicking the {operation} must not render the trained-character menu.");
    Assert(
        plugin.ParentHorseId == expectedParentHorseId
        && baseline.SequenceEqual(File.ReadAllBytes(settingsPath)),
        $"Right-clicking the {operation} must not change ParentHorseId or settings.json.");
}

static int? GetRefreshedParentId()
    => (typeof(PluginType).GetProperty(
            "Parent",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?.GetValue(null) as TrainedChara)?.trained_chara_id;

static WinSaddleFrame ClickPublishedHeader(
    WorkspaceSmokeSession ui,
    string title,
    MouseFlags flags,
    int screenHeight)
{
    return InteractWithWinSaddle(ui, application =>
    {
        var point = FindScreenPoint(
            application.Driver?.ToString()
                ?? throw new InvalidOperationException("The real Host driver was not initialized."),
            title);
        application.Mouse.RaiseMouseEvent(new()
        {
            ScreenPosition = point,
            Flags = flags
        });
    }, screenHeight);
}

static WinSaddleFrame ScrollPublishedTable(WorkspaceSmokeSession ui, string title)
    => InteractWithWinSaddle(ui, application =>
    {
        var point = FindScreenPoint(
            application.Driver?.ToString()
                ?? throw new InvalidOperationException("The real Host driver was not initialized."),
            title,
            yOffset: 2);
        for (var i = 0; i < 20; i++)
        {
            application.Mouse.RaiseMouseEvent(new()
            {
                ScreenPosition = point,
                Flags = MouseFlags.WheeledDown
            });
        }
    }, 16);

static int[] GetPublishedHeaderStarts(WinSaddleFrame frame, IReadOnlyList<string> titles)
    => [.. titles.Select(title => FindScreenPoint(frame.Text, title).X)];

static void AssertPublishedHeaderState(
    WinSaddleFrame frame,
    IReadOnlyList<string> titles,
    IReadOnlyList<int> expectedStarts,
    int? sortedColumn,
    char? marker,
    string operation)
{
    var headers = titles.Select(title => FindScreenPoint(frame.Text, title)).ToArray();
    Assert(
        headers.Select(header => header.X).SequenceEqual(expectedStarts)
        && headers.Select(header => header.Y).Distinct().Count() == 1,
        $"{operation}: marker cycling must not move any visible header start or column boundary.");

    var headerY = headers[0].Y;
    var markers = Enumerable.Range(0, frame.Cells.GetLength(1))
        .Where(x => frame.Cells[headerY, x].Grapheme is "▲" or "▼")
        .ToArray();
    if (sortedColumn is null)
    {
        Assert(markers.Length == 0, $"{operation}: the unsorted header must not expose a sorting marker.");
        return;
    }

    var columnEnd = sortedColumn.Value + 1 < expectedStarts.Count
        ? expectedStarts[sortedColumn.Value + 1]
        : frame.Cells.GetLength(1);
    Assert(
        marker is not null
        && markers.Length == 1
        && frame.Cells[headerY, markers[0]].Grapheme == marker.Value.ToString()
        && markers[0] >= expectedStarts[sortedColumn.Value]
        && markers[0] < columnEnd,
        $"{operation}: marker {marker} must belong only to clicked header column {sortedColumn}.");
}

static void AssertPublishedRowOrder(
    WinSaddleFrame frame,
    IReadOnlyList<int> headerStarts,
    IReadOnlyList<int> expectedIds,
    string operation)
{
    var actualIds = GetPublishedTrainedIds(frame, headerStarts);
    Assert(
        actualIds.SequenceEqual(expectedIds),
        $"{operation}: expected visible ids [{string.Join(", ", expectedIds)}], actual [{string.Join(", ", actualIds)}].");
}

static (int Y, int Id)[] GetPublishedTrainedRows(
    WinSaddleFrame frame,
    IReadOnlyList<int> headerStarts)
{
    var headerY = FindScreenPoint(frame.Text, "TrainedCharaId").Y;
    var rows = new List<(int Y, int Id)>();
    var started = false;
    for (var y = headerY + 1; y < frame.Cells.GetLength(0); y++)
    {
        var digits = new List<char>();
        for (var x = headerStarts[1]; x < headerStarts[2]; x++)
        {
            var grapheme = frame.Cells[y, x].Grapheme;
            digits.AddRange(grapheme.Where(char.IsAsciiDigit));
            x += Math.Max(1, grapheme.GetColumns()) - 1;
        }

        if (digits.Count == 0 || !int.TryParse(new string([.. digits]), out var id))
        {
            if (started)
                break;
            continue;
        }

        started = true;
        rows.Add((y, id));
    }

    return [.. rows];
}

static int[] GetPublishedTrainedIds(WinSaddleFrame frame, IReadOnlyList<int> headerStarts)
    => [.. GetPublishedTrainedRows(frame, headerStarts).Select(row => row.Id)];

static void AssertPublishedTrainedState(
    WorkspaceSmokeSession ui,
    Workspace target,
    SortOrder expected,
    string operation)
{
    target.SwitchTo();
    var screen = ui.CaptureScreen();
    var marker = (int)expected % 2 == 1 ? '▲' : '▼';
    Assert(
        ReferenceEquals(Workspace.Create("WINSADDLEANALYZER"), target)
        && screen.Contains("殿堂马", StringComparison.Ordinal)
        && screen.Contains("TrainedCharaId", StringComparison.Ordinal)
        && screen.Contains(marker),
        $"{operation}: the real published trained-character framebuffer is missing its expected sorting state.");
}

static void AssertSettingsSort(string settingsPath, SortOrder expected, string operation)
{
    Assert(File.Exists(settingsPath), $"{operation}: settings.json must exist.");
    var settings = JObject.Parse(File.ReadAllText(settingsPath));
    Assert(settings.Property("DisplayOrder") is null, $"{operation}: settings.json must not retain the legacy DisplayOrder field.");
    var actual = settings.Value<string>("TrainedCharaSort");
    Assert(actual == expected.ToString(), $"{operation}: expected persisted sort '{expected}', actual '{actual}'.");
}

static void OnNextConfigDialog(IApplication application, Action<Dialog> action)
{
    application.AddTimeout(TimeSpan.Zero, () =>
    {
        if (application.TopRunnableView is not Dialog dialog)
            return true;

        action(dialog);
        return false;
    });
}

static IEnumerable<View> Descendants(View root)
{
    yield return root;
    foreach (var child in root.SubViews)
    foreach (var descendant in Descendants(child))
        yield return descendant;
}

static void AssertUnchangedSortDraft(
    WorkspaceSmokeSession ui,
    Workspace target,
    PluginType plugin,
    string settingsPath,
    byte[] baseline,
    string operation)
{
    Assert(plugin.TrainedCharaSort == SortOrder.分数降序, $"{operation} must not change the runtime sort state.");
    Assert(baseline.SequenceEqual(File.ReadAllBytes(settingsPath)), $"{operation} must not write settings.json.");
    Assert(
        ReferenceEquals(Workspace.Current, target),
        $"{operation} must not switch the visible Workspace.");
    AssertPublishedTrainedState(ui, target, SortOrder.分数降序, operation);
}

static void AssertLegacySortingConfigurationFailsFast(WorkspaceSmokeSession ui)
{
    var originalCwd = Directory.GetCurrentDirectory();
    var workspace = Path.Combine(Path.GetTempPath(), "win-saddle-legacy-config-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(workspace, "PluginData", "WinSaddleAnalyzer"));
    try
    {
        Directory.SetCurrentDirectory(workspace);
        File.WriteAllText(
            Path.Combine("PluginData", "WinSaddleAnalyzer", "settings.json"),
            """
            {
              "DisplayOrder": 0,
              "OnlyFavourites": true,
              "TargetHorseId": 0,
              "ParentHorseId": 0
            }
            """);

        var plugin = new PluginType();
        using var context = new RuntimePluginContext(ui.Application);
        try
        {
            plugin.Initialize(context);
            throw new InvalidOperationException("Legacy DisplayOrder settings must fail fast.");
        }
        catch (InvalidDataException ex)
        {
            Assert(
                ex.Message.Contains("settings.json", StringComparison.Ordinal),
                "Legacy sorting config failure must identify settings.json.");
        }
        finally
        {
            plugin.Dispose();
        }
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCwd);
        Directory.Delete(workspace, recursive: true);
    }
}

static void AssertConfigurationDraftSemantics(WorkspaceSmokeSession ui)
{
    var originalCwd = Directory.GetCurrentDirectory();
    var workspace = Path.Combine(Path.GetTempPath(), "win-saddle-config-smoke-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workspace);
    try
    {
        Directory.SetCurrentDirectory(workspace);
        using var context = new RuntimePluginContext(ui.Application);
        var plugin = new PluginType();
        plugin.Initialize(context);
        using var application = Terminal.Gui.App.Application.Create(new VirtualTimeProvider())
            .Init(DriverRegistry.Names.ANSI);
        application.Driver!.SetScreenSize(100, 30);

        OnNextConfigDialog(application, dialog =>
        {
            var fields = GetNumericIdFields(dialog);
            Assert(
                fields.All(field => field.Provider is TextRegexProvider
                {
                    Pattern: "^[0-9]*$",
                    ValidateOnInput: true,
                }),
                "Both ID editors must use input-validating ASCII-digit regex providers.");
            var onlyFavourites = Descendants(dialog)
                .OfType<CheckBox>()
                .Single(box => box.Text?.ToString().Contains("只显示收藏了的马", StringComparison.Ordinal) == true);
            onlyFavourites.Value = CheckState.UnChecked;

            fields[0].Text = string.Empty;
            fields[0].SetFocus();
            Assert(
                ReferenceEquals(application.Navigation?.GetFocused(), fields[0]),
                "The TargetHorseId editor must own application focus before keyboard and paste input.");
            application.Keyboard.RaiseKeyDownEvent(new Key('1'));
            Assert(fields[0].Text == "1", "The TargetHorseId editor must accept typed ASCII digits.");
            foreach (var invalid in new[] { 'x', '-', '.' })
                application.Keyboard.RaiseKeyDownEvent(new Key(invalid));
            application.Keyboard.RaiseKeyDownEvent(Key.CursorUp);
            Assert(
                fields[0].Text == "1",
                "The TargetHorseId editor must reject non-digits and must not increment with CursorUp.");
            var mixedPasteHandled = application.RaisePasteEvent("23x");
            Assert(
                fields[0].Text == "1",
                $"A mixed TargetHorseId paste must leave the field unchanged; handled={mixedPasteHandled}, actual '{fields[0].Text}'.");

            fields[1].Text = string.Empty;
            fields[1].SetFocus();
            Assert(
                application.RaisePasteEvent("0002") && fields[1].Text == "0002",
                "The ParentHorseId editor must accept a pure ASCII-digit paste.");
            ClickVisible(application, "保存");
        });
        plugin.ConfigPromptAsync(application).GetAwaiter().GetResult();

        var settingsPath = Path.Combine("PluginData", "WinSaddleAnalyzer", "settings.json");
        var settings = JObject.Parse(File.ReadAllText(settingsPath));
        Assert(
            settings.Value<string>("TrainedCharaSort") == SortOrder.不排序.ToString(),
            "Save must persist the default unsorted state as the enum name.");
        Assert(settings.Value<int>("TargetHorseId") == 1, "Save must persist the draft TargetHorseId.");
        Assert(settings.Value<int>("ParentHorseId") == 2, "Save must persist the draft ParentHorseId.");
        Assert(settings.Value<bool>("OnlyFavourites") == false, "Save must persist the draft OnlyFavourites value.");

        OnNextConfigDialog(application, dialog =>
        {
            var fields = GetNumericIdFields(dialog);
            fields[0].Text = string.Empty;
            fields[1].Text = string.Empty;
            ClickVisible(application, "保存");
        });
        plugin.ConfigPromptAsync(application).GetAwaiter().GetResult();
        settings = JObject.Parse(File.ReadAllText(settingsPath));
        Assert(
            settings.Value<int>("TargetHorseId") == 0
            && settings.Value<int>("ParentHorseId") == 0
            && plugin.TargetHorseId == 0
            && plugin.ParentHorseId == 0,
            "Saving empty ID fields must normalize both values to 0 in runtime and settings.json.");
        var baseline = File.ReadAllBytes(settingsPath);

        OnNextConfigDialog(application, dialog =>
        {
            var fields = GetNumericIdFields(dialog);
            fields[0].Text = ((long)int.MaxValue + 1).ToString();
            fields[1].Text = "2";
            ClickVisible(application, "保存");
            application.LayoutAndDraw(forceRedraw: true);
            var overflowScreen = application.Driver?.ToString() ?? string.Empty;
            Assert(
                ReferenceEquals(application.TopRunnableView, dialog),
                "An overflowing TargetHorseId must keep the configuration dialog open.");
            Assert(
                overflowScreen.Contains("要养的马的 CharaId 必须是", StringComparison.Ordinal)
                && overflowScreen.Contains(int.MaxValue.ToString(), StringComparison.Ordinal),
                $"An overflowing TargetHorseId must show a field-specific range error. Screen:{Environment.NewLine}{overflowScreen}");
            ClickVisible(application, "取消");
        });
        AssertCanceled(plugin.ConfigPromptAsync(application), "Overflow validation must not save the draft.");
        Assert(
            plugin.TargetHorseId == 0
            && plugin.ParentHorseId == 0
            && baseline.SequenceEqual(File.ReadAllBytes(settingsPath)),
            "Overflow validation must not change runtime IDs or settings.json.");

        OnNextConfigDialog(application, dialog =>
        {
            var fields = GetNumericIdFields(dialog);
            fields[0].Text = "1234";
            fields[1].Text = "5678";
            ClickVisible(application, "取消");
        });
        AssertCanceled(plugin.ConfigPromptAsync(application), "Cancel must cancel the draft.");
        Assert(baseline.SequenceEqual(File.ReadAllBytes(settingsPath)), "Cancel must not write the edited draft.");

        application.AddTimeout(TimeSpan.Zero, () =>
        {
            if (application.TopRunnable is null)
                return true;

            application.RequestStop(application.TopRunnable);
            return false;
        });
        AssertCanceled(plugin.ConfigPromptAsync(application), "Closing the dialog must cancel the draft.");
        Assert(baseline.SequenceEqual(File.ReadAllBytes(settingsPath)), "Closing the dialog must not write the draft.");

        plugin.Dispose();
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCwd);
        Directory.Delete(workspace, recursive: true);
    }
}

static TextValidateField[] GetNumericIdFields(Dialog dialog)
{
    var views = Descendants(dialog).ToArray();
    Assert(
        !views.OfType<NumericUpDown<int>>().Any(),
        "WinSaddleAnalyzer config must not expose NumericUpDown ID editors.");
    var fields = views
        .OfType<TextValidateField>()
        .OrderBy(field => field.Frame.Y)
        .ToArray();
    Assert(fields.Length == 2, "WinSaddleAnalyzer config must expose exactly two validated ID text fields.");
    return fields;
}

static void AssertCanceled(Task task, string message)
{
    try
    {
        task.GetAwaiter().GetResult();
        throw new InvalidOperationException(message);
    }
    catch (OperationCanceledException)
    {
    }
}

static void ClickVisible(IApplication application, string text, int xOffset = 0)
{
    application.LayoutAndDraw(forceRedraw: true);
    var screen = application.Driver?.ToString()
        ?? throw new InvalidOperationException("The real config framebuffer was not initialized.");
    application.Mouse.RaiseMouseEvent(new()
    {
        ScreenPosition = FindScreenPoint(screen, text, xOffset),
        Flags = MouseFlags.LeftButtonClicked
    });
}

static void InitializeHostConfigForSmoke()
    => UmamusumeResponseAnalyzer.Config.Initialize();

static async Task InitializeSmokeDatabase(
    IEnumerable<BaseName> names,
    int[] saddleIds,
    Dictionary<int, string>? factorIds = null)
{
    WriteBrotliJson(UraDatabase.EVENT_NAME_FILEPATH, new List<Story>());
    WriteBrotliJson(
        UraDatabase.NAMES_FILEPATH,
        names.ToList(),
        new()
        {
            TypeNameHandling = TypeNameHandling.All,
            ContractResolver = new WritablePropertiesOnlyContractResolver()
        });
    WriteBrotliJson(UraDatabase.SKILLS_FILEPATH, new List<UmamusumeResponseAnalyzer.Entities.SkillData>());
    WriteBrotliJson(UraDatabase.SKILL_UPGRADE_SPECIALITY_FILEPATH, new List<SkillUpgradeSpeciality>());
    WriteBrotliJson(UraDatabase.TALENT_SKILLS_FILEPATH, new Dictionary<int, TalentSkillData[]>());
    WriteBrotliJson(
        UraDatabase.FACTOR_IDS_FILEPATH,
        factorIds ?? new Dictionary<int, string> { [1001] = "测试因子" });
    WriteBrotliJson(UraDatabase.SADDLE_IDS_FILEPATH, saddleIds);
    WriteBrotliJson(UraDatabase.SUCCESSION_RELATION_FILEPATH, new SuccessionRelationTable());
    if (await UraDatabase.Initialize() != UmamusumeResponseAnalyzer.DatabaseAvailability.Ready)
        throw new InvalidOperationException("WinSaddle smoke database fixture did not load atomically.");
}

static void WriteSkillEffectSettings(string path, string race, string runningStyle)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(
        path,
        JsonConvert.SerializeObject(
            new
            {
                DisplayOrder = 0,
                MinimumExpectedEffect = 0,
                Race = race,
                RunningStyle = runningStyle,
                URACloudBaseUrl = "http://127.0.0.1:4694",
                AutoUpdateSkillEffects = false
            },
            Formatting.Indented));
}

static void WriteBrotliJson<T>(string path, T value, JsonSerializerSettings? settings = null)
{
    using var file = File.Create(path);
    using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
    using var writer = new StreamWriter(brotli, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    using var json = new JsonTextWriter(writer);
    (settings is null ? JsonSerializer.CreateDefault() : JsonSerializer.Create(settings)).Serialize(json, value);
}

static FriendSimpleSearchResponse CreateFriendSimpleSearchResponse() => new()
{
    data = new()
    {
        user_info_summary = new()
        {
            name = "simple friend",
            viewer_id = 123456789,
            user_trained_chara_array =
            [
                new()
                {
                    card_id = 100100,
                    factor_info_array = [new() { factor_id = 1001 }]
                }
            ]
        }
    }
};

static FriendSearchResponse CreateFriendSearchResponse() => new()
{
    data = new()
    {
        user_info_summary = new()
        {
            name = "full friend",
            viewer_id = 987654321
        },
        follower_num = 42,
        partner_chara_info_array =
        [
            new()
            {
                card_id = 100200,
                rank_score = 654321,
                win_saddle_id_array = [1, 2],
                factor_info_array = [new() { factor_id = 1001 }],
                factor_extend_array = [],
                succession_chara_array =
                [
                    new()
                    {
                        card_id = 100100,
                        owner_viewer_id = 111,
                        win_saddle_id_array = [1],
                        factor_info_array = []
                    },
                    new()
                    {
                        card_id = 100200,
                        owner_viewer_id = 222,
                        win_saddle_id_array = [2],
                        factor_info_array = []
                    }
                ]
            }
        ]
    }
};

static FriendSearchResponse CreateSkillEffectFriendSearchResponse(int factorId) => new()
{
    data = new()
    {
        user_info_summary = new()
        {
            name = "skill effect friend",
            viewer_id = 246813579
        },
        follower_num = 7,
        partner_chara_info_array = [CreateSkillEffectTrainedChara(900_002, 100200, factorId)]
    }
};

static TrainedChara CreateSkillEffectTrainedChara(int id, int cardId, int factorId) => new()
{
    trained_chara_id = id,
    card_id = cardId,
    rank_score = 123456,
    is_locked = 1,
    win_saddle_id_array = [],
    factor_info_array = [],
    factor_extend_array = [],
    succession_chara_array =
    [
        new()
        {
            card_id = 100300,
            owner_viewer_id = 111,
            win_saddle_id_array = [],
            factor_info_array = [new() { factor_id = factorId }]
        },
        new()
        {
            card_id = 100400,
            owner_viewer_id = 222,
            win_saddle_id_array = [],
            factor_info_array = [new() { factor_id = factorId }]
        }
    ]
};

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WinSaddleAnalyzer", "WinSaddleAnalyzer.csproj")))
                return current.FullName;

            current = current.Parent;
        }
    }

    throw new DirectoryNotFoundException("Unable to locate URA-Plugins repository root.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static TException AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException(message);
}

sealed record WinSaddleFrame(string Text, Cell[,] Cells);

sealed class WritablePropertiesOnlyContractResolver : DefaultContractResolver
{
    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        => [.. base.CreateProperties(type, memberSerialization).Where(property => property.Writable)];
}
