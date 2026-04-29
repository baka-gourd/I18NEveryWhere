using Colossal.Core;
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

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using IMod = Game.Modding.IMod;

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

    /// <summary>
    /// Actual locale loading is deferred to <see cref="InitWhenGameAvailable"/>.
    /// </summary>
    public void OnLoad(UpdateSystem updateSystem)
    {
        Instance = this;
        Logger.keepStreamOpen = true;
        Logger.Info(nameof(OnLoad));

        if (GameManager.instance.modManager.TryGetExecutableAsset(this, out ExecutableAsset asset))
        {
            Logger.Info($"Current mod asset at {asset.path}");
            LocalizationsPath = Path.Combine(Path.GetDirectoryName(asset.path) ?? "", "Localization");
        }

        GameManager.instance.localizationManager.onActiveDictionaryChanged += ChangeCurrentLocale;
        GameManager.instance.settings.userInterface.onSettingsApplied += ChangeCurrentLocale;

        // Patch LocalizationDictionary.TryGetValue to intercept every key lookup
        // and redirect to our dictionaries before falling through to the game's own data.
        Logger.Info("Apply harmony patching...");
        Harmony harmony = new("Nptr.I18nEverywhere");
        MethodInfo originalMethod =
            typeof(LocalizationDictionary).GetMethod("TryGetValue", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo prefix =
            typeof(HookLocalizationDictionary).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);

        harmony.Patch(originalMethod, new HarmonyMethod(prefix));
        Logger.Info("Harmony patched.");

        Setting = new Setting(this);
        Setting.RegisterInOptionsUI();
        AssetDatabase.global.LoadSettings("I18NEverywhere", Setting, new Setting(this));
        CacheMods();
        GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));

        // Defer full initialization: poll each frame until the game reaches MainMenu.
        Updater = MainThreadDispatcher.RegisterUpdater(InitWhenGameAvailable);
    }

    /// <summary>
    /// Main locale loading pipeline. Builds new dictionaries in three phases:
    /// 1) Embed locales  - per-mod lang/ folders (lowest priority)
    /// 2) Centralized    - this mod's own Localization/ bundle or directory
    /// 3) Language packs - dedicated translation mods (highest priority, can overwrite all above)
    /// Dictionaries are swapped atomically after all phases complete.
    /// </summary>
    /// <param name="reloadFallback">False on locale-switch events (fallback rarely changes).</param>
    public static bool LoadLocales(string localeId, string fallbackLocaleId, bool reloadFallback = true)
    {
        Logger.Info("Loading locales...");

        try
        {
            Dictionary<string, string> currentLocaleDictionary = new(), fallbackLocaleDictionary = new();

            LoadEmbedLocales(currentLocaleDictionary, fallbackLocaleDictionary, localeId, fallbackLocaleId,
                reloadFallback);

            LoadCentralizedLocales(currentLocaleDictionary, fallbackLocaleDictionary, localeId, fallbackLocaleId,
                reloadFallback);

            LoadLanguagePacks(currentLocaleDictionary, fallbackLocaleDictionary, localeId, fallbackLocaleId,
                reloadFallback);

            // Atomic swap: replace both dictionaries at once.
            CurrentLocaleDictionary = currentLocaleDictionary;
            if (reloadFallback)
            {
                FallbackLocaleDictionary = fallbackLocaleDictionary;
            }

            ValidateEntries();
        }
        catch (Exception e)
        {
            Logger.Error(e, $"You can ignore this error and continue. Need investigating.\n{e.Message}");
        }

        Logger.Info("Locales loaded.");
        return true;
    }

    #region Locale Loading Helpers

    /// <summary>
    /// Merges entries from <paramref name="source"/> into <paramref name="target"/>.
    /// When restrict=true, existing keys are never overwritten (mod authors can protect their translations).
    /// When restrict=false, later sources win (allows language packs to override earlier entries).
    /// </summary>
    private static void MergeDictionary(
        Dictionary<string, string> target,
        Dictionary<string, string> source,
        bool restrict,
        string sourceName)
    {
        foreach (KeyValuePair<string, string> kv in source)
        {
            if (kv.Key is null || kv.Value is null)
            {
                Logger.WarnFormat("{0} have a null entry", sourceName);
                continue;
            }

            if (target.ContainsKey(kv.Key))
            {
                if (restrict)
                {
                    Logger.Warn($"{kv.Key}: overlap, skipped.");
                    continue;
                }

                Logger.Info($"{kv.Key}: overwritten.");
                target[kv.Key] = kv.Value;
            }
            else
            {
                target.Add(kv.Key, kv.Value);
            }
        }
    }

    /// <summary>
    /// Reads entries for the given locale from a <see cref="LanguageBundle"/> and merges them into the target dictionary.
    /// </summary>
    private static void ReadAndMergeBundleLocale(
        LanguageBundle bundle,
        Dictionary<string, string> target,
        string localeId,
        bool restrict,
        string bundlePath)
    {
        Logger.Info($"Load {localeId} from {bundlePath}");
        try
        {
            Dictionary<string, string> dict = bundle.ReadContent(localeId);
            MergeDictionary(target, dict, restrict, bundlePath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error reading {localeId} from {bundlePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads all JSON files under <c>{basePath}/{localeId}/</c> and merges them into the target dictionary.
    /// </summary>
    private static void LoadLocaleFromDirectory(
        string basePath,
        Dictionary<string, string> target,
        string localeId,
        bool restrict)
    {
        string localeDir = Path.Combine(basePath, localeId);
        if (!Directory.Exists(localeDir))
        {
            return;
        }

        Logger.Info($"Loading locale directory: {localeDir}");
        FileInfo[] files = new DirectoryInfo(localeDir).GetFiles("*.json", SearchOption.AllDirectories);
        foreach (FileInfo file in files)
        {
            try
            {
                Dictionary<string, string> dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                                                      File.ReadAllText(file.FullName))
                                                  ?? new Dictionary<string, string>();
                MergeDictionary(target, dict, restrict, file.FullName);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"Error reading {file.FullName}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Reads a single JSON file from <c>{modPath}/{localeId}.json</c> and merges it into the target dictionary.
    /// </summary>
    private static void LoadEmbedLocaleFromJson(
        Dictionary<string, string> target,
        string modPath,
        string localeId,
        bool restrict)
    {
        string filePath = Path.Combine(modPath, localeId + ".json");
        if (!File.Exists(filePath))
        {
            return;
        }

        Logger.Info($"Load {Path.GetFileName(filePath)}");
        try
        {
            Dictionary<string, string> dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                File.ReadAllText(filePath)) ?? new Dictionary<string, string>();
            MergeDictionary(target, dict, restrict, filePath);
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }
    }

    #endregion

    #region Locale Loading Methods

    /// <summary>
    /// Phase 2: Load from this mod's own Localization/ directory (or a language pack's).
    /// Strategy: prefer Locale.lb bundle if present; otherwise fall back to per-locale JSON directories.
    /// Also reused by <see cref="LoadLanguagePacks"/> with a different <paramref name="localizationsPath"/>.
    /// </summary>
    private static bool LoadCentralizedLocales(
        Dictionary<string, string> currentLocaleDictionary,
        Dictionary<string, string> fallbackLocaleDictionary,
        string localeId,
        string fallbackLocaleId,
        bool reloadFallback,
        string localizationsPath = null)
    {
        localizationsPath ??= LocalizationsPath;
        bool restrict = Setting.Restrict;

        if (string.IsNullOrEmpty(localizationsPath))
        {
            Logger.Warn("Cannot find localization path!");
            return false;
        }

        // Try bundle first: a single .lb archive containing all languages.
        string bundlePath = Path.Combine(localizationsPath, "Locale.lb");
        if (File.Exists(bundlePath))
        {
            try
            {
                using LanguageBundle bundle = LanguageBundle.ReadBundle(bundlePath);
                Logger.Info($"Loaded bundle: {bundlePath} (IsCentralized={bundle.IsCentralized})");

                // Case-insensitive match: centralized bundles may use different casing than the game.
                if (bundle.IncludedLanguage.Contains(localeId, StringComparer.InvariantCultureIgnoreCase))
                {
                    ReadAndMergeBundleLocale(bundle, currentLocaleDictionary, localeId, restrict, bundlePath);
                }

                if (reloadFallback &&
                    bundle.IncludedLanguage.Contains(fallbackLocaleId, StringComparer.InvariantCultureIgnoreCase))
                {
                    ReadAndMergeBundleLocale(bundle, fallbackLocaleDictionary, fallbackLocaleId, restrict, bundlePath);
                }

                return true;
            }
            catch (Exception bundleEx)
            {
                Logger.Error(bundleEx, $"Error loading bundle {bundlePath}: {bundleEx.Message}");
            }
        }

        // Fallback: load loose JSON files from {localizationsPath}/{localeId}/*.json.
        if (reloadFallback)
        {
            LoadLocaleFromDirectory(localizationsPath, fallbackLocaleDictionary, fallbackLocaleId, restrict);
        }

        LoadLocaleFromDirectory(localizationsPath, currentLocaleDictionary, localeId, restrict);

        return true;
    }

    /// <summary>
    /// Phase 1: Scan each cached mod's lang/ folder for locale files.
    /// Per mod: prefer Locale.lb bundle; fall back to {localeId}.json flat file.
    /// Mods with a ".nolang" sentinel file are skipped (opt-out mechanism).
    /// Note: bundle language check here is case-sensitive (unlike centralized loading).
    /// </summary>
    private static bool LoadEmbedLocales(
        Dictionary<string, string> currentLocaleDictionary,
        Dictionary<string, string> fallbackLocaleDictionary,
        string localeId,
        string fallbackLocaleId,
        bool reloadFallback)
    {
        bool restrict = Setting.Restrict;

        // Filter: skip null/missing/opted-out mods.
        foreach (ModInfo modInfo in CachedMods
                     .Where(mi => mi != null)
                     .Where(mi => !string.IsNullOrEmpty(mi.Path))
                     .Where(mi => Directory.Exists(mi.Path))
                     .Where(mi => !File.Exists(Path.Combine(mi.Path, ".nolang"))))
        {
            if (modInfo?.Name is null)
            {
                Logger.Warn("Load [null]'s localization files.");
                Logger.Warn($"Actual path: {modInfo?.Path}");
            }
            else
            {
                Logger.Info($"Load \"{modInfo.Name}\"'s localization files.");
            }

            // If this mod ships a bundle, use it exclusively and skip JSON fallback.
            string bundlePath = Path.Combine(modInfo.Path, "Locale.lb");
            if (File.Exists(bundlePath))
            {
                try
                {
                    using LanguageBundle bundle = LanguageBundle.ReadBundle(bundlePath);
                    Logger.Info($"Loaded bundle: {bundlePath} (contains {bundle.IncludedLanguage.Length} entries)");

                    if (bundle.IncludedLanguage.Contains(localeId))
                    {
                        ReadAndMergeBundleLocale(bundle, currentLocaleDictionary, localeId, restrict, bundlePath);
                    }

                    if (reloadFallback && bundle.IncludedLanguage.Contains(fallbackLocaleId))
                    {
                        ReadAndMergeBundleLocale(bundle, fallbackLocaleDictionary, fallbackLocaleId, restrict,
                            bundlePath);
                    }

                    // Bundle loaded successfully; skip JSON fallback for this mod.
                    continue;
                }
                catch (Exception bundleEx)
                {
                    Logger.Error(bundleEx, $"Error loading {bundlePath}: {bundleEx.Message}");
                }
            }

            // No bundle: try flat JSON files like {lang}/{localeId}.json.
            LoadEmbedLocaleFromJson(currentLocaleDictionary, modInfo.Path, localeId, restrict);

            if (reloadFallback)
            {
                LoadEmbedLocaleFromJson(fallbackLocaleDictionary, modInfo.Path, fallbackLocaleId, restrict);
            }
        }

        return true;
    }

    /// <summary>
    /// Phase 3: Load dedicated language pack mods (highest priority).
    /// Each pack must have an i18n.json manifest in its parent directory.
    /// Delegates to <see cref="LoadCentralizedLocales"/> with the pack's own path,
    /// so packs follow the same bundle-first-then-directory loading strategy.
    /// </summary>
    private static bool LoadLanguagePacks(
        Dictionary<string, string> currentLocaleDictionary,
        Dictionary<string, string> fallbackLocaleDictionary,
        string localeId,
        string fallbackLocaleId,
        bool reloadFallback)
    {
        bool loadLanguagePacks = Setting.CanLoadLanguagePacks;

        // Always build the status string for the settings UI, even if loading is disabled.
        Setting.LanguagePacksState = string.Empty;
        Setting.LanguagePacksState +=
            "There are language packs (The following will likely overwrite the text above, so please note the loading order):\n---\n";

        if (!loadLanguagePacks)
        {
            return true;
        }

        foreach (ModInfo modInfo in CachedLanguagePacks)
        {
            if (!modInfo.IsLanguagePack) continue;

            // i18n.json lives one level above the Localization/ directory.
            DirectoryInfo packDirectory = new(modInfo.Path);
            if (packDirectory.Parent == null) continue;
            string infoFile = Path.Combine(packDirectory.Parent.FullName, "i18n.json");
            if (!File.Exists(infoFile))
            {
                Logger.WarnFormat("{0} is broken, skip load.", modInfo.Name);
                continue;
            }

            try
            {
                LanguagePackInfo packInfo = JsonConvert.DeserializeObject<LanguagePackInfo>(File.ReadAllText(infoFile));
                Setting.LanguagePacksState +=
                    $"{packInfo.Name} by {packInfo.Author}\n\tDescription: {packInfo.Description}\n\tIncluded language: {packInfo.IncludedLanguage}\n---\n";

                // Reuse centralized loading logic with the pack's own Localization/ path.
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

    #endregion

    /// <summary>
    /// Uses reflection to access PDX SDK internals and retrieve the active playset's enabled mods.
    /// </summary>
    private static HashSet<Mod> TrickyGetActiveMods()
    {
        PdxSdkPlatform manager = PlatformManager.instance.GetPSI<PdxSdkPlatform>("PdxSdk");

        // PdxSdk now handle errors, they will return an empty HashSet when error. 
        HashSet<Mod> playsetResult = manager.GetModsInActivePlayset()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
        return playsetResult;
    }

    /// <summary>Removes entries with null keys or values that could cause NREs in the Harmony patch.</summary>
    private static void ValidateEntries()
    {
        List<string> keys = [.. CurrentLocaleDictionary
            .Where(kv => kv.Key == null || kv.Value == null)
            .Select(kv => kv.Key)];
        foreach (string key in keys)
        {
            CurrentLocaleDictionary.Remove(key);
        }
    }

    /// <summary>
    /// Discovers all mods and classifies them into CachedMods vs CachedLanguagePacks.
    /// New method scans three sources in order:
    ///   1) ModsData/ - mod data directories (language packs only)
    ///   2) Mods/     - local (development) mods
    ///   3) PDX SDK   - subscribed workshop mods via <see cref="TrickyGetActiveMods"/>
    /// Falls back to <see cref="LegacyCacheMods"/> on failure or if disabled in settings.
    /// A mod is a language pack if it contains i18n.json; otherwise it's a regular mod.
    /// </summary>
    private static void CacheMods()
    {
        if (Setting.UseNewModDetectMethod)
        {
            try
            {
                // Source 1: ModsData/ - check for language packs whose name ends with "Localization".
                if (Directory.Exists(Path.Combine(EnvPath.kUserDataPath, "ModsData")))
                {
                    DirectoryInfo[] modsData =
                        new DirectoryInfo(Path.Combine(EnvPath.kUserDataPath, "ModsData")).GetDirectories("*",
                            SearchOption.TopDirectoryOnly);
                    foreach (DirectoryInfo info in modsData)
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

                // Source 2: Mods/ - local mods (skip hidden/disabled directories starting with . or ~).
                if (Directory.Exists(Path.Combine(EnvPath.kUserDataPath, "Mods")))
                {
                    DirectoryInfo[] localMods = new DirectoryInfo(Path.Combine(EnvPath.kUserDataPath, "Mods")).GetDirectories();

                    foreach (DirectoryInfo localMod in localMods)
                    {
                        if (localMod.Name.StartsWith('.') || localMod.Name.StartsWith('~'))
                        {
                            continue;
                        }

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

                // Source 3: PDX SDK subscribed mods (only Subscribed type, skip local duplicates).
                var mods = TrickyGetActiveMods();

                foreach (Mod mod in mods)
                {
                    string absolutePath = Path.GetFullPath(mod.path);

                    if (File.Exists(Path.Combine(absolutePath, "i18n.json")))
                    {
                        CachedLanguagePacks.Add(new ModInfo
                        {
                            Name = mod.displayName ?? absolutePath,
                            Path = Path.Combine(absolutePath, "Localization"),
                            IsLanguagePack = true
                        });
                        continue;
                    }

                    CachedMods.Add(new ModInfo
                    {
                        Name = mod.displayName ?? absolutePath,
                        Path = Path.Combine(absolutePath, "lang")
                    });
                }
            }
            catch (Exception trickyException)
            {
                // Reflection-based detection failed; fall back to the safer legacy approach.
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

    /// <summary>
    /// Fallback mod detection: iterates the game's own ModManager.
    /// Simpler but cannot detect language packs (no i18n.json check) and may miss some mods.
    /// Uses HashSet to deduplicate mods that appear multiple times in the manager.
    /// </summary>
    private static void LegacyCacheMods()
    {
        HashSet<ModInfo> set = new();
        try
        {
            foreach (ModManager.ModInfo modInfo in GameManager.instance.modManager)
            {
                // Skip harmony
                if (modInfo.asset.name == "0Harmony") continue;
                string modDir = Path.GetDirectoryName(modInfo.asset.path);
                if (string.IsNullOrEmpty(modDir))
                {
                    continue;
                }

                ModInfo info = new() { Name = modInfo.asset.name, Path = Path.Combine(modDir, "lang") };
                set.Add(info);
            }
        }
        catch (Exception modManagerException)
        {
            Logger.Error(modManagerException, modManagerException.Message);
        }

        CachedMods = [.. set];
    }

    /// <summary>
    /// Reloads only the current locale (not fallback) when the user switches language in-game.
    /// Skipped before initial load is complete to avoid partial state.
    /// </summary>
    private void ChangeCurrentLocale()
    {
        if (!GameLoaded) return;
        string localeId = GameManager.instance.localizationManager.activeLocaleId;
        string fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
        // reloadFallback=false: fallback (en-US) doesn't change on locale switch.
        if (!LoadLocales(localeId, fallbackLocaleId, false))
        {
            Logger.Error("Cannot reload locales.");
        }
    }

    /// <summary>Adapter for the settings-applied event signature.</summary>
    private void ChangeCurrentLocale(Game.Settings.Setting s)
    {
        ChangeCurrentLocale();
    }

    /// <summary>
    /// Reflects into LocalizationManager internals to snapshot the game's own fallback locale sources.
    /// Used by the settings UI to display what locale sources are registered (assets vs mod-provided).
    /// Keyed as "[A]index: name" for LocaleAssets, "[M]index: typeName" for mod IDictionarySources.
    /// </summary>
    public static void UpdateMods()
    {
        ModsFallbackDictionary.Clear();
        if (!Instance.GameLoaded)
        {
            return;
        }

        // Reflect into LocalizationManager.m_LocaleInfos[fallbackLocaleId].m_Sources
        // to enumerate all registered IDictionarySource instances for the fallback locale.
        Type localeInfo =
            typeof(LocalizationManager).GetNestedType("LocaleInfo", BindingFlags.NonPublic);
        object localeInfos = typeof(LocalizationManager)
            .GetField("m_LocaleInfos", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(GameManager.instance.localizationManager);
        if (localeInfos is not IDictionary dict) return;
        object sources = localeInfo.GetField("m_Sources", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(dict[GameManager.instance.localizationManager.fallbackLocaleId]);
        if (sources is not IEnumerable enumerable) return;
        int modCounter = 0;
        int assetCounter = 0;
        foreach (object o in enumerable)
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
        if (Setting != null)
        {
            Setting.UnregisterInOptionsUI();
            Setting = null;
        }

        if (Updater.HasValue)
        {
            MainThreadDispatcher.UnregisterUpdater(Updater.Value);
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
        // Keep polling until the game reaches a stable state.
        if (!GameManager.instance.modManager.isInitialized ||
            GameManager.instance.gameMode != GameMode.MainMenu ||
            GameManager.instance.state == GameManager.State.Loading ||
            GameManager.instance.state == GameManager.State.Booting
           ) return false;

        string localeId = GameManager.instance.localizationManager.activeLocaleId;
        string fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
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

        // Guard: if already initialized (re-entry from event), just confirm completion.
        if (Instance.GameLoaded)
        {
            return true;
        }

        // First-time initialization: notify user, snapshot game state, bump version.
        NotificationSystem.Pop("i18n-load", delay: 10f,
            titleId: "I18NEverywhere",
            textId: "I18NEverywhere.Detail",
            progressState: ProgressState.Complete,
            progress: 100);
        Instance.GameLoaded = true;
        UpdateMods();
        IncrementVersion();
        Logger.InfoFormat("I18NEverywhere initialized on {0}", GameManager.instance.gameMode);
        // Return true to unregister this updater; polling is no longer needed.
        return true;
    }
}
