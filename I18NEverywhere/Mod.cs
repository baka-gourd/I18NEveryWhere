using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.Logging;
using Colossal.Logging.Utils;
using Colossal.PSI.Common;
using Colossal.PSI.Environment;
using Colossal.PSI.PdxSdk;
using Colossal.Serialization.Entities;
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
using UnityEngine;

// ReSharper disable NonReadonlyMemberInGetHashCode

namespace I18NEverywhere
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class I18NEverywhere : IMod
    {
        public static ILog Logger { get; } = LogManager
            .GetLogger($"{nameof(I18NEverywhere)}.{nameof(I18NEverywhere)}").SetShowsErrorsInUI(true);

        public static Dictionary<string, string> CurrentLocaleDictionary { get; } = new();

        public static Dictionary<string, string> FallbackLocaleDictionary { get; } = new();

        public static Dictionary<string, object> ModsFallbackDictionary { get; } = new();

        private static List<ModInfo> CachedInfos { get; set; } = [];
        private static List<ModInfo> CachedLanguagePacks { get; set; } = [];

        [CanBeNull] private static string LocalizationsPath { get; set; }
        private bool _gameLoaded;
        public event EventHandler OnLocaleLoaded;

        public static Setting Setting { get; private set; }
        public static I18NEverywhere Instance { get; private set; }

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
            //    Logger.Error(e, "Cannot migrate setting.");
            //}

            GameManager.instance.localizationManager.onActiveDictionaryChanged += ChangeCurrentLocale;
            GameManager.instance.onGameLoadingComplete += OnLoadingGameComplete;
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

            var localeId = GameManager.instance.localizationManager.activeLocaleId;
            var fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;

            CacheMods();
            Logger.Info($"{nameof(localeId)}: {localeId}");
            Logger.Info($"{nameof(fallbackLocaleId)}: {fallbackLocaleId}");
            if (!LoadLocales(localeId, fallbackLocaleId))
            {
                Logger.Error("Cannot load locales.");
            }
            else
            {
                OnLocaleLoaded?.Invoke(this, EventArgs.Empty);
            }

            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));
        }

        public static bool LoadLocales(string localeId, string fallbackLocaleId, bool reloadFallback = true)
        {
            Logger.Info("Loading locales...");
            CurrentLocaleDictionary.Clear();
            if (reloadFallback)
            {
                FallbackLocaleDictionary.Clear();
            }

            try
            {
                LoadCentralizedLocales(localeId, fallbackLocaleId, reloadFallback);
                LoadEmbedLocales(localeId, fallbackLocaleId, reloadFallback);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"You can ignore this error and continue. Need investigating.\n{e.Message}");
            }

            Logger.Info("Locales loaded.");
            return true;
        }

        private static bool LoadCentralizedLocales(string localeId, string fallbackLocaleId, bool reloadFallback,
            string localizationsPath = null)
        {
            localizationsPath ??= LocalizationsPath;

            var restrict = Setting.Restrict;
            if (string.IsNullOrEmpty(localizationsPath))
            {
                Logger.Warn("Cannot find localization path!");
                return false;
            }

            if (reloadFallback)
            {
                if (Directory.Exists(Path.Combine(localizationsPath, fallbackLocaleId)))
                {
                    var directoryInfo = new DirectoryInfo(Path.Combine(localizationsPath, fallbackLocaleId));
                    Logger.Info($"{nameof(fallbackLocaleId)} directory: {directoryInfo.FullName}");
                    var files = directoryInfo.GetFiles("*.json", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        Logger.Info($"Load {file.Name}");
                        try
                        {
                            var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                                File.ReadAllText(file.FullName)) ?? new Dictionary<string, string>();
                            foreach (var pair in dict)
                            {
                                if (FallbackLocaleDictionary.ContainsKey(pair.Key))
                                {
                                    if (restrict)
                                    {
                                        Logger.Warn($"{pair.Key}: overlap with existing key, skipped.");
                                        continue;
                                    }

                                    Logger.Info($"{pair.Key}: has be modified.");
                                    FallbackLocaleDictionary[pair.Key] = pair.Value;
                                }
                                else
                                {
                                    FallbackLocaleDictionary.Add(pair.Key, pair.Value);
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

            if (Directory.Exists(Path.Combine(localizationsPath, localeId)))
            {
                var directoryInfo = new DirectoryInfo(Path.Combine(localizationsPath, localeId));
                Logger.Info($"{nameof(localeId)} directory: {directoryInfo.FullName}");
                var files = directoryInfo.GetFiles("*.json", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    Logger.InfoFormat("Load {0}", file.Name);
                    try
                    {
                        var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                            File.ReadAllText(file.FullName)) ?? new Dictionary<string, string>();
                        foreach (var pair in dict)
                        {
                            if (CurrentLocaleDictionary.ContainsKey(pair.Key))
                            {
                                if (restrict)
                                {
                                    Logger.WarnFormat("{0}: overlap with existing key, skipped.", pair.Key);
                                    continue;
                                }

                                Logger.InfoFormat("{0}: has be modified.", pair.Key);
                                CurrentLocaleDictionary[pair.Key] = pair.Value;
                            }
                            else
                            {
                                CurrentLocaleDictionary.Add(pair.Key, pair.Value);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }
            }

            return true;
        }

        private static bool LoadEmbedLocales(string localeId, string fallbackLocaleId, bool reloadFallback)
        {
            var restrict = Setting.Restrict;
            var loadLanguagePacks = Setting.CanLoadLanguagePacks;
            Setting.LanguagePacksState +=
                "There are language packs (The following will likely overwrite the text above, so please note the loading order):\n---\n";

            foreach (var modInfo in CachedInfos)
            {
                if (Directory.Exists(Path.Combine(modInfo.Path)))
                {
                    if (File.Exists(Path.Combine(modInfo.Path, ".nolang")))
                    {
                        continue;
                    }

                    Logger.InfoFormat("Load \"{0}\"'s localization files.", modInfo.Name);
                    var current = Path.Combine(modInfo.Path, localeId + ".json");
                    var fallback = Path.Combine(modInfo.Path, fallbackLocaleId + ".json");

                    if (current is not null && File.Exists(current))
                    {
                        Logger.Info($"Load {Path.GetFileName(current)}");
                        try
                        {
                            var currDict =
                                JsonConvert.DeserializeObject<Dictionary<string, string>>(
                                    File.ReadAllText(current)) ??
                                new Dictionary<string, string>();
                            foreach (var pair in currDict)
                            {
                                if (CurrentLocaleDictionary.ContainsKey(pair.Key))
                                {
                                    if (restrict)
                                    {
                                        Logger.Warn($"{pair.Key}: overlap with existing key, skipped.");
                                        continue;
                                    }

                                    Logger.Info($"{pair.Key}: has be modified");
                                    CurrentLocaleDictionary[pair.Key] = pair.Value;
                                }
                                else
                                {
                                    CurrentLocaleDictionary.Add(pair.Key, pair.Value);
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
                        if (fallback is not null && File.Exists(fallback))
                        {
                            Logger.Info($"Load {Path.GetFileName(fallback)}");
                            try
                            {
                                var fallbackDict =
                                    JsonConvert.DeserializeObject<Dictionary<string, string>>(
                                        File.ReadAllText(fallback)) ?? new Dictionary<string, string>();
                                foreach (var pair in fallbackDict)
                                {
                                    if (FallbackLocaleDictionary.ContainsKey(pair.Key))
                                    {
                                        if (restrict)
                                        {
                                            Logger.Warn($"{pair.Key}: overlap with existing key, skipped.");
                                            continue;
                                        }

                                        Logger.Info($"{pair.Key}: has be modified.");
                                        FallbackLocaleDictionary[pair.Key] = pair.Value;
                                    }
                                    else
                                    {
                                        FallbackLocaleDictionary.Add(pair.Key, pair.Value);
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
            }

            foreach (var modInfo in CachedLanguagePacks)
            {
                if (loadLanguagePacks)
                {
                    if (modInfo.IsLanguagePack)
                    {
                        var packDirectory = new DirectoryInfo(modInfo.Path);
                        var infoFile = Path.Combine(packDirectory.Parent.FullName, "i18n.json");
                        if (!File.Exists(infoFile))
                        {
                            Logger.WarnFormat("{0} is broken, skip load.", modInfo.Name);
                        }

                        try
                        {
                            var packInfo = JsonConvert.DeserializeObject<LanguagePackInfo>(File.ReadAllText(infoFile));
                            Setting.LanguagePacksState +=
                                $"{packInfo.Name} by {packInfo.Author}\n\tDescription: {packInfo.Description}\n\tIncluded language: {packInfo.IncludedLanguage}\n---\n";

                            Logger.InfoFormat("Load language pack: {0}", packInfo.Name);
                            LoadCentralizedLocales(localeId, fallbackLocaleId, reloadFallback, modInfo.Path);
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, $"Error when load \"{packDirectory.Parent.Name}\": {e.Message}");
                        }
                    }
                }
            }

            return true;
        }

        private static IEnumerable<PDX.SDK.Contracts.Service.Mods.Models.Mod> TrickyGetActiveMods()
        {
            var manager = PlatformManager.instance.GetPSI<PdxSdkPlatform>("PdxSdk");
            var mods = manager.GetModsInActivePlayset().GetAwaiter().GetResult();
            var context =
                (IContext) typeof(PdxSdkPlatform).GetField("m_SDKContext",
                    BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager);
            var playsetResult = context.Mods.GetActivePlayset().Result;
            return !playsetResult.Success
                ? []
                : mods.Where(mod => mod.Playsets.First(p => p.PlaysetId == playsetResult.PlaysetId).ModIsEnabled);
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
                            new DirectoryInfo(Path.Combine(EnvPath.kUserDataPath, "ModsData")).GetDirectories();
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

                            CachedInfos.Add(new ModInfo
                                {Name = localMod.Name, Path = Path.Combine(localMod.FullName, "lang")});
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
                                Name = mod.DisplayName, Path = Path.Combine(absolutePath, "Localization"),
                                IsLanguagePack = true
                            });
                            continue;
                        }

                        CachedInfos.Add(new ModInfo
                            {Name = mod.DisplayName, Path = Path.Combine(absolutePath, "lang")});
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

            Logger.InfoFormat("Cached mods: \n{0}", string.Join("\n", CachedInfos.Select(x => $"\t\t{x.Name}")));
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
                    if (modInfo.asset.isEnabled)
                    {
                        var modDir = Path.GetDirectoryName(modInfo.asset.path);
                        if (string.IsNullOrEmpty(modDir))
                        {
                            continue;
                        }

                        var info = new ModInfo {Name = modInfo.asset.name, Path = Path.Combine(modDir, "lang")};
                        set.Add(info);
                    }
                }
            }
            catch (Exception modManagerException)
            {
                Logger.Error(modManagerException, modManagerException.Message);
            }

            CachedInfos = set.ToList();
        }

        private void ChangeCurrentLocale()
        {
            if (_gameLoaded)
            {
                var localeId = GameManager.instance.localizationManager.activeLocaleId;
                var fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
                if (!LoadLocales(localeId, fallbackLocaleId, false))
                {
                    Logger.Error("Cannot reload locales.");
                }
            }
        }

        public static void UpdateMods()
        {
            ModsFallbackDictionary.Clear();
            var localeInfo =
                typeof(LocalizationManager).GetNestedType("LocaleInfo", BindingFlags.NonPublic | BindingFlags.Instance);
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

            GameManager.instance.localizationManager.onActiveDictionaryChanged -= ChangeCurrentLocale;
            GameManager.instance.onGameLoadingComplete -= OnLoadingGameComplete;
        }

        private void OnLoadingGameComplete(Purpose p, GameMode m)
        {
            if (_gameLoaded) return;
            NotificationSystem.Pop("i18n-load", delay: 10f,
                titleId: "I18NEverywhere",
                textId: "I18NEverywhere.Detail",
                progressState: ProgressState.Complete,
                progress: 100);
            _gameLoaded = true;
        }
    }
}