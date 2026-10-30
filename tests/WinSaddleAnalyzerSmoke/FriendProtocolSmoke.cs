using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Gallop;
using Gallop.Endpoints;
using MessagePack;
using Newtonsoft.Json.Linq;
using Terminal.Gui.App;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

static class FriendProtocolSmoke
{
    const string InternalName = "WinSaddleAnalyzer";

    public static async Task Run(WorkspaceSmokeSession ui)
    {
        var package = FindPackage();
        var full = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "friend-search-tw.json")));
        var simple = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "friend-simple-search-tw.json")));
        using var directory = new TempCurrentDirectory("WinSaddleAnalyzer-fresh-zip");
        Config.Initialize();
        WriteDatabaseFile(Database.EVENT_NAME_FILEPATH, "[]");
        WriteDatabaseFile(Database.NAMES_FILEPATH, "[]");
        WriteDatabaseFile(Database.SKILLS_FILEPATH, "[]");
        WriteDatabaseFile(Database.SKILL_UPGRADE_SPECIALITY_FILEPATH, "[]");
        WriteDatabaseFile(Database.TALENT_SKILLS_FILEPATH, "{}");
        WriteDatabaseFile(Database.FACTOR_IDS_FILEPATH,
            """{"200001":"基础因子","200002":"扩展因子","200101":"第二因子","200201":"第三因子"}""");
        WriteDatabaseFile(Database.SADDLE_IDS_FILEPATH, "[1,2]");
        WriteDatabaseFile(Database.SUCCESSION_RELATION_FILEPATH, "{}");
        Require(await Database.Initialize() == DatabaseAvailability.Ready, "Protocol database did not initialize.");
        Directory.CreateDirectory("PluginData/WinSaddleAnalyzer");
        File.WriteAllText("PluginData/WinSaddleAnalyzer/settings.json",
            """{"TrainedCharaSort":"不排序","OnlyFavourites":false,"TargetHorseId":1001,"ParentHorseId":900001}""");
        Directory.CreateDirectory("Plugins");
        var isolatedPackage = Path.GetFullPath("Plugins/WinSaddleAnalyzer.zip");
        File.Copy(package, isolatedPackage);

        try
        {
            PluginManager.Init();
            PluginManager.InitializeLoadedPlugins();
            await PluginManager.TriggerStartedAsync();
            Require(PluginManager.FailedPlugins.Count == 0 && PluginManager.LoadedPlugins.Count == 1,
                $"Fresh ZIP failed to load: {string.Join(',', PluginManager.FailedPlugins)}");
            var plugin = PluginManager.LoadedPlugins.Single();
            var assembly = plugin.GetType().Assembly;
            var loadContext = AssemblyLoadContext.GetLoadContext(assembly)!;
            var metadata = PluginManager.Metadatas[InternalName];
            Require(metadata.PackagePath == isolatedPackage, "Loaded a package outside the isolated directory.");
            using var archive = ZipFile.OpenRead(package);
            using var entry = archive.GetEntry(metadata.AssemblyEntry)!.Open();
            using var bytes = new MemoryStream();
            entry.CopyTo(bytes);
            bytes.Position = 0;
            using var pe = new PEReader(bytes);
            var reader = pe.GetMetadataReader();
            Require(loadContext != AssemblyLoadContext.Default
                && assembly.ManifestModule.ModuleVersionId == reader.GetGuid(reader.GetModuleDefinition().Mvid),
                "Executed assembly did not come from the fresh ZIP.");
            foreach (var shared in new[] { typeof(IPlugin).Assembly, typeof(TrainedChara).Assembly, typeof(IApplication).Assembly, typeof(MessagePackSerializer).Assembly })
            {
                Require(ReferenceEquals(loadContext.LoadFromAssemblyName(shared.GetName()), shared),
                    $"Shared assembly was duplicated: {shared.GetName().Name}");
                Require(!archive.Entries.Any(x => x.Name == shared.GetName().Name + ".dll"),
                    $"Shared assembly was packaged: {shared.GetName().Name}");
            }
            Require(PluginManager.ResponseAnalyzerMethods.Count == 4 && PluginManager.RequestAnalyzerMethods.Count == 0,
                "Expected two raw friend and two typed response registrations.");
            foreach (var endpointType in new[] { typeof(GameApi.Friend.Search), typeof(GameApi.Friend.SimpleSearch) })
            {
                var registration = PluginManager.SnapshotAnalyzerRegistrations(AnalyzerKind.Response, endpointType).Single();
                Require(registration.Method is null, "Friend endpoint retained a typed attribute registration.");
            }

            var friend = MessagePackSerializer.Deserialize<TrainedChara>(
                MessagePackSerializer.ConvertFromJson(full["data"]!["practice_partner_info"]!.ToString()));
            var own = MessagePackSerializer.Deserialize<TrainedChara>(MessagePackSerializer.Serialize(friend));
            own.trained_chara_id = 900001;
            own.card_id = 100100;
            await Dispatch(typeof(GameApi.TrainedChara.Load), MessagePackSerializer.Serialize(new TrainedCharaLoadResponse
            {
                data = new() { trained_chara_array = [own], trained_chara_favorite_array = [] }
            }));
            Require(ui.CaptureScreen().Contains("900001", StringComparison.Ordinal), "Trained-character load did not publish the configured parent.");

            foreach (var arrayShape in new[] { false, true })
            {
                var fullPacket = (JObject)full.DeepClone();
                var simplePacket = (JObject)simple.DeepClone();
                if (arrayShape)
                {
                    var data = (JObject)fullPacket["data"]!;
                    var chara = data["practice_partner_info"]!;
                    data.Remove("practice_partner_info");
                    var second = (JObject)chara.DeepClone();
                    second["trained_chara_id"] = 900004;
                    second["rank_score"] = 777777;
                    second["card_id"] = 100300;
                    data["partner_chara_info_array"] = new JArray(chara, second);
                    var summary = (JObject)simplePacket["data"]!["user_info_summary"]!;
                    var simpleChara = summary["user_trained_chara"]!;
                    summary.Remove("user_trained_chara");
                    var simpleSecond = (JObject)simpleChara.DeepClone();
                    simpleSecond["trained_chara_id"] = 900005;
                    simpleSecond["card_id"] = 100300;
                    summary["user_trained_chara_array"] = new JArray(simpleChara, simpleSecond);
                }
                var fullBytes = MessagePackSerializer.ConvertFromJson(fullPacket.ToString());
                var simpleBytes = MessagePackSerializer.ConvertFromJson(simplePacket.ToString());
                AssertProjectedIds(assembly, fullBytes, simpleBytes, arrayShape);
                await Dispatch(typeof(GameApi.Friend.Search), fullBytes);
                var screen = ui.CaptureScreen(200, 80);
                foreach (var token in new[] { "测试好友", "123456789", "100200", "123456", "扩展因子", "第二因子", "第三因子", "相性分析", "好友总相性：12" })
                    Require(screen.Contains(token, StringComparison.Ordinal), $"Full friend omitted {token}:\n{screen}");
                Require(!screen.Contains("777777", StringComparison.Ordinal), "Selected a later representative.");
                await Dispatch(typeof(GameApi.Friend.SimpleSearch), simpleBytes);
                screen = ui.CaptureScreen(200, 80);
                foreach (var token in new[] { "测试简略好友", "987654321", "100200", "基础因子" })
                    Require(screen.Contains(token, StringComparison.Ordinal), $"Simple friend omitted {token}:\n{screen}");
                Require(!screen.Contains("100300", StringComparison.Ordinal) && !screen.Contains("相性分析", StringComparison.Ordinal),
                    "Simple friend selected a later representative or retained inheritance output.");
            }

            var unchanged = ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!)
                .OfType<Label>().Single(label => label.Text.Contains("测试简略好友", StringComparison.Ordinal)));
            foreach (var (endpoint, field, container) in new[]
            {
                (typeof(GameApi.Friend.Search), "practice_partner_info", "data"),
                (typeof(GameApi.Friend.Search), "partner_chara_info_array", "data"),
                (typeof(GameApi.Friend.SimpleSearch), "user_trained_chara", "summary"),
                (typeof(GameApi.Friend.SimpleSearch), "user_trained_chara_array", "summary")
            })
            foreach (var empty in new JToken?[] { null, JValue.CreateNull(), new JArray() })
            {
                var packet = JObject.Parse("""{"data":{"user_info_summary":{}}}""");
                var destination = (JObject)(container == "data" ? packet["data"]! : packet["data"]!["user_info_summary"]!);
                if (empty is not null) destination[field] = empty.DeepClone();
                await Dispatch(endpoint, MessagePackSerializer.ConvertFromJson(packet.ToString()));
                ui.Flush();
                Require(ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!).Contains(unchanged)),
                    $"An absent representative replaced the friend panel ({field}, {empty?.Type}).");
            }

            foreach (var endpoint in new[] { typeof(GameApi.Friend.Search), typeof(GameApi.Friend.SimpleSearch) })
            foreach (var invalid in new[] { "null", "{}", "{\"data\":{}}", "{\"data\":{\"user_info_summary\":7}}" })
                await AssertRejected(endpoint, MessagePackSerializer.ConvertFromJson(invalid));
            await AssertRejected(typeof(GameApi.Friend.Search), [0x81]);
            await AssertRejected(typeof(GameApi.Friend.Search), MessagePackSerializer.ConvertFromJson(
                """{"data":{"user_info_summary":{},"practice_partner_info":7}}"""));
            await AssertRejected(typeof(GameApi.Friend.SimpleSearch), MessagePackSerializer.ConvertFromJson(
                """{"data":{"user_info_summary":{"user_trained_chara_array":7}}}"""));
            ui.Flush();
            Require(ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!).Contains(unchanged)),
                "Malformed packets partially replaced panels.");

            await Dispatch(typeof(GameApi.SingleMode.Start), MessagePackSerializer.Serialize(new SingleModeStartResponse
            {
                data = new()
                {
                    single_mode_start_common = new()
                    {
                        chara_info = new() { card_id = 100100, succession_trained_chara_id_1 = 900001, succession_trained_chara_id_2 = 900002 },
                        add_trained_chara_array = [friend]
                    }
                }
            }));
            var inheritance = ui.CaptureScreen(200, 80);
            Require(inheritance.Contains("好友总相性：12", StringComparison.Ordinal)
                && !inheritance.Contains("测试好友", StringComparison.Ordinal), "Training start did not replace friend output with inheritance.");
            var settings = JObject.Parse(File.ReadAllText("PluginData/WinSaddleAnalyzer/settings.json"));
            Require(settings.Value<int>("ParentHorseId") == 900001 && settings.Value<int>("TargetHorseId") == 1001,
                "Training start did not preserve the selected parent and target.");
        }
        finally
        {
            await PluginManager.ShutdownAsync();
            ui.Flush();
        }
        Require(PluginManager.LoadedPlugins.Count == 0 && PluginManager.Contexts.Count == 0
            && PluginManager.ResponseAnalyzerMethods.Count == 0, "ZIP shutdown left plugin instances or registrations.");
        Workspace.Create(InternalName).SwitchTo();
        Require(!ui.CaptureScreen().Contains("好友总相性", StringComparison.Ordinal), "ZIP shutdown left panels mounted.");
        ui.Bootstrap.SwitchTo();
        Console.WriteLine("PASS fresh ZIP: TW/JP friend protocols, empty/malformed packets, inheritance flow, MVID, shared assemblies, shutdown");
    }

    static void AssertProjectedIds(Assembly assembly, byte[] full, byte[] simple, bool arrayShape)
    {
        var responseType = assembly.GetType("WinSaddleAnalyzer.FriendResponse", throwOnError: true)!;
        var fullResponse = MessagePackSerializer.Deserialize(responseType, full)!;
        var data = responseType.GetProperty("Data")!.GetValue(fullResponse)!;
        var value = data.GetType().GetProperty(arrayShape ? "PartnerCharacters" : "PracticePartner")!.GetValue(data)!;
        var chara = arrayShape ? ((TrainedChara[])value)[0] : (TrainedChara)value;
        Require(chara.trained_chara_id == 900002 && chara.factor_info_array[0].factor_id == 200001,
            "Full friend projection lost trained-character ID or factors.");
        var simpleResponse = MessagePackSerializer.Deserialize(responseType, simple)!;
        data = responseType.GetProperty("Data")!.GetValue(simpleResponse)!;
        var summary = data.GetType().GetProperty("UserInfo")!.GetValue(data)!;
        value = summary.GetType().GetProperty(arrayShape ? "TrainedCharacters" : "TrainedCharacter")!.GetValue(summary)!;
        var simpleChara = arrayShape ? ((UserTrainedCharaAtFriend[])value)[0] : (UserTrainedCharaAtFriend)value;
        Require(simpleChara.trained_chara_id == 900003 && simpleChara.factor_info_array[0].factor_id == 200001,
            "Simple friend projection lost trained-character ID or factors.");
    }

    static async ValueTask Dispatch(Type endpointType, byte[] payload)
    {
        var endpoint = GameEndpointCatalog.ByEndpointType[endpointType];
        var registration = PluginManager.SnapshotAnalyzerRegistrations(AnalyzerKind.Response, endpointType).Single();
        var context = new AnalyzerDispatchContext(endpoint, payload, new(null, null, null, null, null, null));
        using var callback = PluginManager.TryEnterPluginCallback(registration.Plugin);
        Require(callback is not null && !registration.IsFaulted, $"ZIP analyzer unavailable: {registration.Source}");
        using var owner = HotkeyManager.RegisterScope(registration.Plugin);
        await registration.Handler(context);
    }

    static async Task AssertRejected(Type endpoint, byte[] payload)
    {
        try { await Dispatch(endpoint, payload); }
        catch (InvalidDataException ex)
        {
            Require(ex.Message.Contains(GameEndpointCatalog.ByEndpointType[endpoint].Path, StringComparison.Ordinal),
                "Protocol failure did not identify its endpoint.");
            return;
        }
        throw new InvalidOperationException("Malformed friend response was silently accepted.");
    }

    static string FindPackage()
    {
        var root = Environment.GetEnvironmentVariable("URA_PLUGIN_SMOKE_PACKAGE_ROOT");
        if (string.IsNullOrWhiteSpace(root) && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            root = Path.Combine(Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")!, "artifacts");
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("URA_PLUGIN_SMOKE_PACKAGE_ROOT must point to this run's fresh ZIP output directory.");
        var matches = new List<string>();
        foreach (var candidate in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
        {
            using var archive = ZipFile.OpenRead(candidate);
            if (archive.GetEntry("manifest.json") is not { } entry) continue;
            using var stream = new StreamReader(entry.Open());
            if (JObject.Parse(stream.ReadToEnd()).Value<string>("InternalName") == InternalName) matches.Add(candidate);
        }
        Require(matches.Count == 1, $"Expected one fresh {InternalName} ZIP under {root}, found {matches.Count}.");
        var validated = PluginPackageValidator.Validate(matches[0], true);
        Require(validated.Manifest.Dependencies.Length == 0, "ZIP unexpectedly declares plugin dependencies.");
        return matches[0];
    }

    static void WriteDatabaseFile(string path, string json)
    {
        using var file = File.Create(path);
        using var brotli = new BrotliStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(brotli);
        writer.Write(json);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
