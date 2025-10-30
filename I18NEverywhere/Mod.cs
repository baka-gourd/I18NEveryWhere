using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.Logging;
using Colossal.Logging.Utils;
using Colossal.PSI.Common;
using Colossal.PSI.Environment;
using Colossal.PSI.PdxSdk;

using Game;
using Game.Modding;
using Game.PSI;
using Game.SceneFlow;

using HarmonyLib;

using I18NEverywhere.Models;

using JetBrains.Annotations;

using Newtonsoft.Json;

using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Service.Mods.Enums;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using Mod = PDX.SDK.Contracts.Service.Mods.Models.Mod;

// ReSharper disable NonReadonlyMemberInGetHashCode

namespace I18NEverywhere;

// ReSharper disable once ClassNeverInstantiated.Global
public class I18NEverywhere : IMod
{
    public static ILog Logger { get; } = LogManager
        .GetLogger($"{nameof(I18NEverywhere)}.{nameof(I18NEverywhere)}").SetShowsErrorsInUI(true);

    public static Dictionary<string, string> CurrentLocaleDictionary { get; set; } = new();

    public static Dictionary<string, string> FallbackLocaleDictionary { get; set; } = new();

    public static Dictionary<string, object> ModsFallbackDictionary { get; } = new();

    private static List<ModInfo> CachedMods { get; set; } = [];
    private static List<ModInfo> CachedLanguagePacks { get; set; } = [];

    [CanBeNull] private static string LocalizationsPath { get; set; }
    private Guid? Updater { get; set; }
    public bool GameLoaded { get; set; }
    public event EventHandler OnLocaleLoaded;

    public static Setting Setting { get; private set; }
    public static I18NEverywhere Instance { get; private set; }
    private static int _settingVersion;

    public void OnLoad(UpdateSystem updateSystem)
    {
        Instance = this;
        Logger.Info(nameof(OnLoad));

        if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
        {
            Logger.Info($"Current mod asset at {asset.path}");
            LocalizationsPath = Path.Combine(Path.GetDirectoryName(asset.path) ?? "", "Localization");
        }

        //try
        //{
        //    Util.MigrateSetting();
        //}
        //catch (Exception e)
        //{
        //    Logger.Warn(e, "Cannot migrate setting.");
        //}

        GameManager.instance.localizationManager.onActiveDictionaryChanged += ChangeCurrentLocale;
        GameManager.instance.settings.userInterface.onSettingsApplied += ChangeCurrentLocale;
        Logger.Info("Apply harmony patching...");
        var harmony = new Harmony("Nptr.I18nEverywhere");
        var originalMethod =
            typeof(LocalizationDictionary).GetMethod("TryGetValue", BindingFlags.Public | BindingFlags.Instance);
        var prefix =
            typeof(HookLocalizationDictionary).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);

        harmony.Patch(originalMethod, new HarmonyMethod(prefix));
        Logger.Info("Harmony patched.");

        Setting = new Setting(this);
        Setting.RegisterInOptionsUI();
        AssetDatabase.global.LoadSettings("I18NEverywhere", Setting, new Setting(this));

        CacheMods();
        GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));
        Updater = GameManager.instance.RegisterUpdater(InitWhenGameAvailable);
    }

    public static bool LoadLocales(string localeId, string fallbackLocaleId, bool reloadFallback = true)
    {
        Logger.Info("Loading locales...");

        try
        {
            Dictionary<string, string> currentLocaleDictionary = new(), fallbackLocaleDictionary = new();
            LoadCentralizedLocales(currentLocaleDictionary, fallbackLocaleDictionary, localeId, fallbackLocaleId,
                reloadFallback);
            LoadEmbedLocales(currentLocaleDictionary, fallbackLocaleDictionary, localeId, fallbackLocaleId,
                reloadFallback);

            CurrentLocaleDictionary = currentLocaleDictionary;
            if (reloadFallback)
            {
                FallbackLocaleDictionary = fallbackLocaleDictionary;
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, $"You can ignore this error and continue. Need investigating.\n{e.Message}");
        }

        Logger.Info("Locales loaded.");
        return true;
    }

    private static bool LoadCentralizedLocales(
        Dictionary<string, string> currentLocaleDictionary,
        Dictionary<string, string> fallbackLocaleDictionary,
        string localeId,
        string fallbackLocaleId,
        bool reloadFallback,
        string localizationsPath = null)
    {
        localizationsPath ??= LocalizationsPath;
        var restrict = Setting.Restrict;

        if (string.IsNullOrEmpty(localizationsPath))
        {
            Logger.Warn("Cannot find localization path!");
            return false;
        }

        var bundlePath = Path.Combine(localizationsPath, "Locale.lb");
        if (File.Exists(bundlePath))
        {
            try
            {
                using var bundle = LanguageBundle.ReadBundle(bundlePath);
                Logger.Info($"Loaded bundle: {bundlePath} (IsCentralized={bundle.IsCentralized})");

                if (!bundle.IsCentralized)
                {
                    if (bundle.IncludedLanguage.Contains(localeId, StringComparer.InvariantCultureIgnoreCase))
                    {
                        var entryName = $"{localeId}.json";
                        Logger.Info($"Load {entryName} from {bundlePath}");
                        try
                        {
                            var dict = bundle.ReadContent(localeId);
                            foreach (var kv in dict)
                            {
                                if (currentLocaleDictionary.ContainsKey(kv.Key))
                                {
                                    if (restrict)
                                    {
                                        Logger.Warn($"{kv.Key}: overlap, skipped.");
                                        continue;
                                    }

                                    Logger.Info($"{kv.Key}: overwritten.");
                                    currentLocaleDictionary[kv.Key] = kv.Value;
                                }
                                else
                                {
                                    currentLocaleDictionary.Add(kv.Key, kv.Value);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Error reading {entryName}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    if (bundle.IncludedLanguage.Contains(localeId, StringComparer.InvariantCultureIgnoreCase))
                    {
                        Logger.Info($"Load all JSON under directory \"{localeId}/\" from {bundlePath}");
                        try
                        {
                            var dict = bundle.ReadContent(localeId);
                            foreach (var kv in dict)
                            {
                                if (currentLocaleDictionary.ContainsKey(kv.Key))
                                {
                                    if (restrict)
                                    {
                                        Logger.Warn($"{kv.Key}: overlap, skipped.");
                                        continue;
                                    }

                                    Logger.Info($"{kv.Key}: overwritten.");
                                    currentLocaleDictionary[kv.Key] = kv.Value;
                                }
                                else
                                {
                                    currentLocaleDictionary.Add(kv.Key, kv.Value);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Error reading {localeId} directory: {ex.Message}");
                        }
                    }
                }

                if (reloadFallback &&
                    bundle.IncludedLanguage.Contains(fallbackLocaleId, StringComparer.InvariantCultureIgnoreCase))
                {
                    Logger.Info($"Load all JSON under directory \"{fallbackLocaleId}/\" from {bundlePath}");
                    try
                    {
                        var fallbackDict = bundle.ReadContent(fallbackLocaleId);
                        foreach (var kv in fallbackDict)
                        {
                            if (fallbackLocaleDictionary.ContainsKey(kv.Key))
                            {
                                if (restrict)
                                {
                                    Logger.Warn($"{kv.Key}: overlap, skipped.");
                                    continue;
                                }

                                Logger.Info($"{kv.Key}: overwritten.");
                                fallbackLocaleDictionary[kv.Key] = kv.Value;
                            }
                            else
                            {
                                fallbackLocaleDictionary.Add(kv.Key, kv.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error reading fallback {fallbackLocaleId} directory: {ex.Message}");
                    }
                }

                return true;
            }
            catch (Exception bundleEx)
            {
                Logger.Error(bundleEx, $"Error loading bundle {bundlePath}: {bundleEx.Message}");
            }
        }

        if (reloadFallback)
        {
            var fallbackDir = Path.Combine(localizationsPath, fallbackLocaleId);
            if (Directory.Exists(fallbackDir))
            {
                Logger.Info($"Loading fallback directory: {fallbackDir}");
                var files = new DirectoryInfo(fallbackDir).GetFiles("*.json", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                                       File.ReadAllText(file.FullName))
                                   ?? new Dictionary<string, string>();
                        foreach (var kv in dict)
                        {
                            if (fallbackLocaleDictionary.ContainsKey(kv.Key))
                            {
                                if (restrict)
                                {
                                    Logger.Warn($"{kv.Key}: overlap, skipped.");
                                    continue;
                                }

                                Logger.Info($"{kv.Key}: overwritten.");
                                fallbackLocaleDictionary[kv.Key] = kv.Value;
                            }
                            else
                            {
                                fallbackLocaleDictionary.Add(kv.Key, kv.Value);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, $"Error reading {file.FullName}: {e.Message}");
                    }
                }
            }
        }

        var localeDir = Path.Combine(localizationsPath, localeId);
        if (Directory.Exists(localeDir))
        {
            Logger.Info($"Loading locale directory: {localeDir}");
            var files = new DirectoryInfo(localeDir).GetFiles("*.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    var dict =
                        JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file.FullName))
                        ?? new Dictionary<string, string>();
                    foreach (var kv in dict)
                    {
                        if (currentLocaleDictionary.ContainsKey(kv.Key))
                        {
                            if (restrict)
                            {
                                Logger.Warn($"{kv.Key}: overlap, skipped.");
                                continue;
                            }

                            Logger.Info($"{kv.Key}: overwritten.");
                            currentLocaleDictionary[kv.Key] = kv.Value;
                        }
                        else
                        {
                            currentLocaleDictionary.Add(kv.Key, kv.Value);
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Error reading {file.FullName}: {e.Message}");
                }
            }
        }

        return true;
    }


    private static bool LoadEmbedLocales(
        Dictionary<string, string> currentLocaleDictionary,
        Dictionary<string, string> fallbackLocaleDictionary,
        string localeId,
        string fallbackLocaleId,
        bool reloadFallback)
    {
        var restrict = Setting.Restrict;
        var loadLanguagePacks = Setting.CanLoadLanguagePacks;
        Setting.LanguagePacksState = string.Empty;
        Setting.LanguagePacksState +=
            "There are language packs (The following will likely overwrite the text above, so please note the loading order):\n---\n";

        foreach (var modInfo in CachedMods.Where(modInfo => Directory.Exists(modInfo.Path))
                     .Where(modInfo => !File.Exists(Path.Combine(modInfo.Path, ".nolang"))))
        {
            Logger.InfoFormat("Load \"{0}\"'s localization files.", modInfo.Name);

            var bundlePath = Path.Combine(modInfo.Path, "Locale.lb");
            if (File.Exists(bundlePath))
            {
                try
                {
                    using var bundle = LanguageBundle.ReadBundle(bundlePath);
                    Logger.Info($"Loaded bundle: {bundlePath} (contains {bundle.IncludedLanguage.Length} entries)");

                    if (bundle.IncludedLanguage.Contains(localeId))
                    {
                        Logger.Info($"Load {localeId} from {bundlePath}");
                        try
                        {
                            var dictFromLb = bundle.ReadContent(localeId);
                            foreach (var pair in dictFromLb)
                            {
                                if (currentLocaleDictionary.ContainsKey(pair.Key))
                                {
                                    if (restrict)
                                    {
                                        Logger.Warn($"{pair.Key}: overlap with existing key, skipped.");
                                        continue;
                                    }

                                    Logger.Info($"{pair.Key}: has been modified.");
                                    currentLocaleDictionary[pair.Key] = pair.Value;
                                }
                                else
                                {
                                    currentLocaleDictionary.Add(pair.Key, pair.Value);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Error deserializing {localeId} from {bundlePath}: {ex.Message}");
                        }
                    }

                    if (reloadFallback)
                    {
                        if (bundle.IncludedLanguage.Contains(fallbackLocaleId))
                        {
                            Logger.Info($"Load {fallbackLocaleId} from {bundlePath}");
                            try
                            {
                                var fallbackDictFromLb = bundle.ReadContent(fallbackLocaleId);
                                foreach (var pair in fallbackDictFromLb)
                                {
                                    if (fallbackLocaleDictionary.ContainsKey(pair.Key))
                                    {
                                        if (restrict)
                                        {
                                            Logger.Warn($"{pair.Key}: overlap with existing key, skipped.");
                                            continue;
                                        }

                                        Logger.Info($"{pair.Key}: has been modified.");
                                        fallbackLocaleDictionary[pair.Key] = pair.Value;
                                    }
                                    else
                                    {
                                        fallbackLocaleDictionary.Add(pair.Key, pair.Value);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error(ex,
                                    $"Error deserializing {fallbackLocaleId} from {bundlePath}: {ex.Message}");
                            }
                        }
                    }

                    // just skip prev file
                    continue;
                }
                catch (Exception bundleEx)
                {
                    Logger.Error(bundleEx, $"Error loading {bundlePath}: {bundleEx.Message}");
                }
            }

            var current = Path.Combine(modInfo.Path, localeId + ".json");
            if (File.Exists(current))
            {
                Logger.Info($"Load {Path.GetFileName(current)}");
                try
                {
                    var currDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                        File.ReadAllText(current)) ?? new Dictionary<string, string>();
                    foreach (var pair in currDict)
                    {
                        if (currentLocaleDictionary.ContainsKey(pair.Key))
                        {
                            if (restrict)
                            {
                                Logger.Warn($"{pair.Key}: overlap with existing key, skipped.");
                                continue;
                            }

                            Logger.Info($"{pair.Key} has been modified.");
                            currentLocaleDictionary[pair.Key] = pair.Value;
                        }
                        else
                        {
                            currentLocaleDictionary.Add(pair.Key, pair.Value);
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            }

            if (reloadFallback)
            {
                var fallback = Path.Combine(modInfo.Path, fallbackLocaleId + ".json");
                if (File.Exists(fallback))
                {
                    Logger.Info($"Load {Path.GetFileName(fallback)}");
                    try
                    {
                        var fallbackDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                            File.ReadAllText(fallback)) ?? new Dictionary<string, string>();
                        foreach (var pair in fallbackDict)
                        {
                            if (fallbackLocaleDictionary.ContainsKey(pair.Key))
                            {
                                if (restrict)
                                {
                                    Logger.Warn($"{pair.Key}: overlap with existing key, skipped.");
                                    continue;
                                }

                                Logger.Info($"{pair.Key}: has been modified.");
                                fallbackLocaleDictionary[pair.Key] = pair.Value;
                            }
                            else
                            {
                                fallbackLocaleDictionary.Add(pair.Key, pair.Value);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }
            }
        }

        foreach (var modInfo in CachedLanguagePacks)
        {
            if (!loadLanguagePacks) break;
            if (!modInfo.IsLanguagePack) continue;

            var packDirectory = new DirectoryInfo(modInfo.Path);
            var infoFile = Path.Combine(packDirectory.Parent.FullName, "i18n.json");
            if (!File.Exists(infoFile))
            {
                Logger.WarnFormat("{0} is broken, skip load.", modInfo.Name);
                continue;
            }

            try
            {
                var packInfo = JsonConvert.DeserializeObject<LanguagePackInfo>(File.ReadAllText(infoFile));
                Setting.LanguagePacksState +=
                    $"{packInfo.Name} by {packInfo.Author}\n\tDescription: {packInfo.Description}\n\tIncluded language: {packInfo.IncludedLanguage}\n---\n";

                Logger.InfoFormat("Load language pack: {0}", packInfo.Name);
                LoadCentralizedLocales(currentLocaleDictionary, fallbackLocaleDictionary, localeId,
                    fallbackLocaleId, reloadFallback, modInfo.Path);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Error when load \"{packDirectory.Parent.Name}\": {e.Message}");
            }
        }

        return true;
    }

    private static IEnumerable<Mod> TrickyGetActiveMods()
    {
        var manager = PlatformManager.instance.GetPSI<PdxSdkPlatform>("PdxSdk");
        var context =
            (IContext)typeof(PdxSdkPlatform).GetField("m_SDKContext",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager);
        var playsetResult = context.Mods.GetActivePlaysetEnabledMods().Result;
        return !playsetResult.Success
            ? new List<Mod>()
            : playsetResult.Mods.Where(m => !string.IsNullOrEmpty(m.LocalData.FolderAbsolutePath));
    }

    private static void CacheMods()
    {
        if (Setting.UseNewModDetectMethod)
        {
            try
            {
                if (Directory.Exists(Path.Combine(EnvPath.kUserDataPath, "ModsData")))
                {
                    var modsData =
                        new DirectoryInfo(Path.Combine(EnvPath.kUserDataPath, "ModsData")).GetDirectories("*",
                            SearchOption.TopDirectoryOnly);
                    foreach (var info in modsData)
                    {
                        if (!info.Name.EndsWith("Localization", StringComparison.InvariantCulture))
                        {
                            continue;
                        }

                        if (File.Exists(Path.Combine(info.FullName, "i18n.json")))
                        {
                            CachedLanguagePacks.Add(new ModInfo
                            {
                                Name = info.Name,
                                Path = Path.Combine(info.FullName, "Localization"),
                                IsLanguagePack = true
                            });

                            Logger.InfoFormat("{0} is loaded from ModsData, make sure its updated.", info.Name);
                        }
                    }
                }

                if (Directory.Exists(Path.Combine(EnvPath.kUserDataPath, "Mods")))
                {
                    var localMods = new DirectoryInfo(Path.Combine(EnvPath.kUserDataPath, "Mods")).GetDirectories();

                    foreach (var localMod in localMods)
                    {
                        if (File.Exists(Path.Combine(localMod.FullName, "i18n.json")))
                        {
                            CachedLanguagePacks.Add(new ModInfo
                            {
                                Name = localMod.Name,
                                Path = Path.Combine(localMod.FullName, "Localization"),
                                IsLanguagePack = true
                            });
                            continue;
                        }

                        CachedMods.Add(new ModInfo
                        { Name = localMod.Name, Path = Path.Combine(localMod.FullName, "lang") });
                    }
                }

                var mods = TrickyGetActiveMods();

                foreach (var mod in mods)
                {
                    if (mod.LocalData.LocalType is not LocalType.Subscribed)
                    {
                        continue;
                    }

                    var absolutePath = mod.LocalData.FolderAbsolutePath;

                    if (File.Exists(Path.Combine(absolutePath, "i18n.json")))
                    {
                        CachedLanguagePacks.Add(new ModInfo
                        {
                            Name = mod.DisplayName,
                            Path = Path.Combine(absolutePath, "Localization"),
                            IsLanguagePack = true
                        });
                        continue;
                    }

                    CachedMods.Add(new ModInfo
                    { Name = mod.DisplayName, Path = Path.Combine(absolutePath, "lang") });
                }
            }
            catch (Exception trickyException)
            {
                Logger.Error(trickyException, trickyException.Message);
                LegacyCacheMods();
            }
        }
        else
        {
            LegacyCacheMods();
        }

        Logger.InfoFormat("Cached mods: \n{0}", string.Join("\n", CachedMods.Select(x => $"\t\t{x.Name}")));
        Logger.InfoFormat("Cached language packs: \n{0}",
            string.Join("\n", CachedLanguagePacks.Select(x => $"\t\t{x.Name}")));
    }

    private static void LegacyCacheMods()
    {
        var set = new HashSet<ModInfo>();
        try
        {
            foreach (var modInfo in GameManager.instance.modManager)
            {
                // Skip harmony
                if (modInfo.asset.name == "0Harmony") continue;
                if (!modInfo.asset.isEnabled) continue;
                var modDir = Path.GetDirectoryName(modInfo.asset.path);
                if (string.IsNullOrEmpty(modDir))
                {
                    continue;
                }

                var info = new ModInfo { Name = modInfo.asset.name, Path = Path.Combine(modDir, "lang") };
                set.Add(info);
            }
        }
        catch (Exception modManagerException)
        {
            Logger.Error(modManagerException, modManagerException.Message);
        }

        CachedMods = set.ToList();
    }

    private void ChangeCurrentLocale()
    {
        if (!GameLoaded) return;
        var localeId = GameManager.instance.localizationManager.activeLocaleId;
        var fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
        if (!LoadLocales(localeId, fallbackLocaleId, false))
        {
            Logger.Error("Cannot reload locales.");
        }
    }

    private void ChangeCurrentLocale(Game.Settings.Setting s)
    {
        if (!GameLoaded) return;
        var localeId = GameManager.instance.localizationManager.activeLocaleId;
        var fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
        if (!LoadLocales(localeId, fallbackLocaleId, false))
        {
            Logger.Error("Cannot reload locales.");
        }
    }

    public static void UpdateMods()
    {
        ModsFallbackDictionary.Clear();
        if (!Instance.GameLoaded)
        {
            return;
        }

        var localeInfo =
            typeof(LocalizationManager).GetNestedType("LocaleInfo", BindingFlags.NonPublic);
        var localeInfos = typeof(LocalizationManager)
            .GetField("m_LocaleInfos", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(GameManager.instance.localizationManager);
        if (localeInfos is not IDictionary dict) return;
        var sources = localeInfo.GetField("m_Sources", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(dict[GameManager.instance.localizationManager.fallbackLocaleId]);
        if (sources is not IEnumerable enumerable) return;
        var modCounter = 0;
        var assetCounter = 0;
        foreach (var o in enumerable)
        {
            if (o is LocaleAsset local)
            {
                ModsFallbackDictionary.Add($"[A]{assetCounter++}: {local.name}", o);
                continue;
            }

            ModsFallbackDictionary.Add($"[M]{modCounter++}: {o.GetType().GetFriendlyName()}", o);
        }
    }

    public void OnDispose()
    {
        //Logger.Info(nameof(OnDispose));
        if (Setting != null)
        {
            Setting.UnregisterInOptionsUI();
            Setting = null;
        }

        if (Updater.HasValue)
        {
            GameManager.instance.UnregisterUpdater(Updater.Value);
        }

        GameManager.instance.localizationManager.onActiveDictionaryChanged -= ChangeCurrentLocale;
        GameManager.instance.settings.userInterface.onSettingsApplied -= ChangeCurrentLocale;
    }

    public void InvokeEvent()
    {
        OnLocaleLoaded?.Invoke(this, EventArgs.Empty);
    }

    public static int GetVersion()
    {
        return _settingVersion;
    }

    public static void IncrementVersion()
    {
        Interlocked.Increment(ref _settingVersion);
    }

    private static bool InitWhenGameAvailable()
    {
        if (!GameManager.instance.modManager.isInitialized ||
            GameManager.instance.gameMode != GameMode.MainMenu ||
            GameManager.instance.state == GameManager.State.Loading ||
            GameManager.instance.state == GameManager.State.Booting
           ) return false;

        var localeId = GameManager.instance.localizationManager.activeLocaleId;
        var fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
        Logger.Info("Init Load.");
        Logger.Info($"{nameof(localeId)}: {localeId}");
        Logger.Info($"{nameof(fallbackLocaleId)}: {fallbackLocaleId}");

        if (!LoadLocales(localeId, fallbackLocaleId))
        {
            Logger.Error("Cannot load locales.");
        }
        else
        {
            Instance.InvokeEvent();
        }

        if (Instance.GameLoaded)
        {
            return true;
        }

        NotificationSystem.Pop("i18n-load", delay: 10f,
            titleId: "I18NEverywhere",
            textId: "I18NEverywhere.Detail",
            progressState: ProgressState.Complete,
            progress: 100);
        Instance.GameLoaded = true;
        UpdateMods();
        IncrementVersion();
        Logger.InfoFormat("I18NEverywhere initialized on {0}", GameManager.instance.gameMode);
        return true;
    }
}