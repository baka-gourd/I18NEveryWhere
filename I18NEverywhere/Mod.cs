﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Colossal.IO.AssetDatabase;
using Colossal.Localization;
using Colossal.Logging;
using Colossal.PSI.Common;
using Colossal.PSI.Environment;
using Colossal.Serialization.Entities;
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
        [CanBeNull] private static string LocalizationsPath { get; set; }
        private bool _gameLoaded;
        public event EventHandler OnLocaleLoaded;

        public static Setting Setting { get; private set; }
        public static I18NEverywhere Instance { get; private set; }

        private void OnLoadingGameComplete(Purpose p, GameMode m)
        {
            if (!_gameLoaded)
            {
                NotificationSystem.Pop("i18n-load", delay: 10f,
                    titleId: "I18NEverywhere",
                    textId: "I18NEverywhere.Detail",
                    progressState: ProgressState.Complete,
                    progress: 100);
                _gameLoaded = true;
            }
        }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            Logger.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                Logger.Info($"Current mod asset at {asset.path}");
                LocalizationsPath = Path.Combine(Path.GetDirectoryName(asset.path) ?? "", "Localization");
            }
            GameManager.instance.localizationManager.onActiveDictionaryChanged += ChangeCurrentLocale;
            GameManager.instance.onGameLoadingComplete += OnLoadingGameComplete;
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
            else
            {
                OnLocaleLoaded?.Invoke(this, EventArgs.Empty);
            }
        }

        public static bool LoadLocales(string localeId, string fallbackLocaleId, bool reloadFallback = true)
        {
            var restrict = Setting.Restrict;
            Logger.Info("Loading locales...");
            CurrentLocaleDictionary.Clear();
            if (string.IsNullOrEmpty(LocalizationsPath))
            {
                Logger.Warn("Cannot find localization path!");
                return false;
            }

            if (reloadFallback)
            {
                FallbackLocaleDictionary.Clear();
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
                                if (restrict)
                                {
                                    Logger.Warn($"{pair.Key}: overlap with existing key, skipped.");
                                    continue;
                                }

                                Logger.Info($"{pair.Key}: has be modified.");
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

            LoadEmbedLocales(localeId, fallbackLocaleId, reloadFallback);
            Logger.Info("Locales loaded.");
            return true;
        }

        private static bool LoadEmbedLocales(string localeId, string fallbackLocaleId, bool reloadFallback = true)
        {
            var restrict = Setting.Restrict;
            var set = new HashSet<string>();
            foreach (var modInfo in GameManager.instance.modManager)
            {
                if (modInfo.asset.isEnabled)
                {
                    var modDir = Path.GetDirectoryName(modInfo.asset.path);
                    if (modDir == null)
                    {
                        continue;
                    }

                    if (!set.Add(Path.Combine(modDir, "lang")))
                    {
                        continue;
                    }

                    if (Directory.Exists(Path.Combine(modDir, "lang")))
                    {
                        Logger.Info($"Load \"{modInfo.name}\"'s localization files.");
                        var current = Path.Combine(modDir, "lang", localeId + ".json");
                        var fallback = Path.Combine(modDir, "lang", fallbackLocaleId + ".json");

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

        void MigrateSetting()
        {
            var oldLocation = Path.Combine(EnvPath.kUserDataPath, $"{nameof(I18NEverywhere)}.coc");

            if (File.Exists(oldLocation))
            {
                var directory = Path.Combine(
                    EnvPath.kUserDataPath,
                    "ModSettings",
                    nameof(I18NEverywhere));

                var correctLocation = Path.Combine(
                    directory, nameof(I18NEverywhere), ".coc");

                Directory.CreateDirectory(directory);

                if (File.Exists(correctLocation))
                {
                    File.Delete(oldLocation);
                }
                else
                {
                    File.Move(oldLocation, correctLocation);
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
            GameManager.instance.localizationManager.onActiveDictionaryChanged -= ChangeCurrentLocale;
            GameManager.instance.onGameLoadingComplete -= OnLoadingGameComplete;
        }
    }
}
