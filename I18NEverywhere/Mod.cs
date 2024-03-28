using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.Logging;
using Colossal.PSI.Common;

using Game;
using Game.Modding;
using Game.PSI;
using Game.SceneFlow;
using HarmonyLib;

using JetBrains.Annotations;

using Newtonsoft.Json;

namespace I18NEverywhere
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class I18NEverywhere : IMod
    {
        public static ILog Logger { get; set; } = LogManager.GetLogger($"{nameof(I18NEverywhere)}.{nameof(I18NEverywhere)}").SetShowsErrorsInUI(true);
        public static Dictionary<string, string> CurrentLocaleDictionary { get; set; } = new Dictionary<string, string>();
        public static Dictionary<string, string> FallbackLocaleDictionary { get; set; } = new Dictionary<string, string>();
        [CanBeNull] private string LocalizationsPath { get; set; }
        [CanBeNull] private string ModsDirectoryPath { get; set; }
        private bool _gameLoaded;

        public static Setting Setting { get; set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Logger.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                Logger.Info($"Current mod asset at {asset.path}");
                LocalizationsPath = Path.Combine(Path.GetDirectoryName(asset.path) ?? "", "Localization");
                var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(asset.path) ?? "").Parent;
                if (directoryInfo != null)
                    ModsDirectoryPath = directoryInfo.FullName;
            }

            GameManager.instance.localizationManager.onActiveDictionaryChanged += ChangeCurrentLocale;
            GameManager.instance.onGameLoadingComplete += (p, m) =>
            {
                if (!_gameLoaded)
                {
                    NotificationSystem.Pop("i18n-load", delay: 5f,
                        titleId: "I18NEverywhere",
                        textId: "I18NEverywhere.Detail",
                        progressState: ProgressState.Complete,
                        progress: 100);
                    _gameLoaded = true;
                }
            };

            Logger.Info("Apply harmony patching...");
            var harmony = new Harmony("Nptr.I18nEverywhere");
            var originalMethod = typeof(LocalizationDictionary).GetMethod("TryGetValue", BindingFlags.Public | BindingFlags.Instance);
            var prefix = typeof(HookLocalizationDictionary).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);

            harmony.Patch(originalMethod, new HarmonyMethod(prefix));
            Logger.Info("Harmony patched.");

            Setting = new Setting(this);
            Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));

            AssetDatabase.global.LoadSettings(nameof(I18NEverywhere), Setting, new Setting(this));

            var localeId = GameManager.instance.localizationManager.activeLocaleId;
            var fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
            Logger.Info($"{nameof(localeId)}: {localeId}");
            Logger.Info($"{nameof(fallbackLocaleId)}: {fallbackLocaleId}");
            if (!LoadLocales(localeId, fallbackLocaleId))
            {
                Logger.Error("Cannot load locales.");
            }
        }

        bool LoadLocales(string localeId, string fallbackLocaleId, bool reloadFallback = true)
        {
            Logger.Info("Loading locales...");
            CurrentLocaleDictionary.Clear();
            if (string.IsNullOrEmpty(LocalizationsPath))
            {
                Logger.Warn("Cannot find localization path!");
                return false;
            }

            if (reloadFallback)
            {
                if (Directory.Exists(Path.Combine(LocalizationsPath, fallbackLocaleId)))
                {
                    var directoryInfo = new DirectoryInfo(Path.Combine(LocalizationsPath, fallbackLocaleId));
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
                                    Logger.Warn($"{pair.Key}: overlap with existing key");
                                    continue;
                                }

                                FallbackLocaleDictionary.Add(pair.Key, pair.Value);
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e);
                        }
                    }
                }
            }

            if (Directory.Exists(Path.Combine(LocalizationsPath, localeId)))
            {
                var directoryInfo = new DirectoryInfo(Path.Combine(LocalizationsPath, localeId));
                Logger.Info($"{nameof(localeId)} directory: {directoryInfo.FullName}");
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
                            if (CurrentLocaleDictionary.ContainsKey(pair.Key))
                            {
                                Logger.Warn($"{pair.Key}: overlap with existing key");
                                continue;
                            }

                            CurrentLocaleDictionary.Add(pair.Key, pair.Value);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }
            }

            LoadEmbedLocales(localeId, fallbackLocaleId, reloadFallback);
            Logger.Info("Locales loaded.");
            return true;
        }

        bool LoadEmbedLocales(string localeId, string fallbackLocaleId, bool reloadFallback = true)
        {
            if (Setting.ScanLocalModDirectory)
            {
                var path = Environment.GetEnvironmentVariable("CSII_USERDATAPATH", EnvironmentVariableTarget.User);
                var fullPath = Path.Combine(path ?? "", "Mods");
                Logger.Info($"Scan {fullPath}");
                InnerLoadEmbedLocales(localeId, fallbackLocaleId, reloadFallback, fullPath);
            }

            if (Setting.ScanPModDirectory)
            {
                var path = Environment.GetEnvironmentVariable("CSII_USERDATAPATH", EnvironmentVariableTarget.User);
                var fullPath = Path.Combine(path ?? "", ".cache/Mods/mods_subscribed");
                Logger.Info($"Scan {fullPath}");
                InnerLoadEmbedLocales(localeId, fallbackLocaleId, reloadFallback, fullPath);
            }

            if (string.IsNullOrEmpty(ModsDirectoryPath))
            {
                Logger.Info("Cannot found mods directory.");
                return false;
            }

            return InnerLoadEmbedLocales(localeId, fallbackLocaleId, reloadFallback);
        }

        private bool InnerLoadEmbedLocales(string localeId, string fallbackLocaleId, bool reloadFallback = true, [CanBeNull] string customPath = null)
        {
            var dirs = !string.IsNullOrEmpty(customPath) ? new DirectoryInfo(customPath).GetDirectories() : new DirectoryInfo(ModsDirectoryPath).GetDirectories();

            foreach (var directoryInfo in dirs)
            {
                if (Directory.Exists(Path.Combine(directoryInfo.FullName, "lang")))
                {
                    Logger.Info($"{directoryInfo.Name} has localization files.");
                    var current = Path.Combine(directoryInfo.FullName, "lang", localeId + ".json");
                    var fallback = Path.Combine(directoryInfo.FullName, "lang", fallbackLocaleId + ".json");
                    if (File.Exists(current))
                    {
                        Logger.Info($"Load {Path.GetFileName(current)}");
                        try
                        {
                            var currDict =
                                JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(current)) ??
                                new Dictionary<string, string>();
                            foreach (var pair in currDict)
                            {
                                if (CurrentLocaleDictionary.ContainsKey(pair.Key))
                                {
                                    Logger.Warn($"{pair.Key}: overlap with existing key");
                                    continue;
                                }

                                CurrentLocaleDictionary.Add(pair.Key, pair.Value);
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e);
                        }
                    }

                    if (reloadFallback)
                    {
                        if (File.Exists(fallback))
                        {
                            Logger.Info($"Load {Path.GetFileName(fallback)}");
                            try
                            {
                                var fallbackDict =
                                    JsonConvert.DeserializeObject<Dictionary<string, string>>(
                                        File.ReadAllText(fallback)) ?? new Dictionary<string, string>();
                                foreach (var pair in fallbackDict)
                                {
                                    if (CurrentLocaleDictionary.ContainsKey(pair.Key))
                                    {
                                        Logger.Warn($"{pair.Key}: overlap with existing key");
                                        continue;
                                    }

                                    CurrentLocaleDictionary.Add(pair.Key, pair.Value);
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

            return true;
        }

        void ChangeCurrentLocale()
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

        public void OnDispose()
        {
            //Logger.Info(nameof(OnDispose));
            if (Setting != null)
            {
                Setting.UnregisterInOptionsUI();
                Setting = null;
            }
        }
    }
}
