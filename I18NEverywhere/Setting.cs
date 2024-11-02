﻿using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.SceneFlow;
using Game.Settings;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.UI.Widgets;
using Newtonsoft.Json;
using Colossal.PSI.Environment;

namespace I18NEverywhere
{
    [FileLocation("ModsSettings\\I18NEverywhere\\I18NEverywhere")]
    [SettingsUIShowGroupName(General, Developer)]
    [SettingsUIGroupOrder(General, Developer)]
    public class Setting : ModSetting
    {
        public const string General = "General";
        public const string Developer = "Developer";

        public Setting(IMod mod) : base(mod)
        {
            Overwrite = true;
            LoadLanguagePacks = true;
            UseNewModDetectMethod = true;
        }

        [SettingsUISection(General)] public bool Overwrite { get; set; }

        [SettingsUISection(General)] public bool Restrict { get; set; }

        [SettingsUISection(General)]
        [SettingsUIDisableByCondition(typeof(Setting), "NewModDetectMethodEnabled", true)]
        public bool LoadLanguagePacks { get; set; }

        public bool CanLoadLanguagePacks => LoadLanguagePacks && UseNewModDetectMethod;

        [SettingsUISection(Developer)] public bool LogKey { get; set; }

        [SettingsUISection(Developer)] public bool UseNewModDetectMethod { get; set; }

        [SettingsUISection(Developer)]
        [SettingsUIButton]
#pragma warning disable CA1822
        public bool ReloadLocalization
#pragma warning restore CA1822
        {
            // ReSharper disable once ValueParameterNotUsed
            set
            {
                var localeId = GameManager.instance.localizationManager.activeLocaleId;
                var fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
                I18NEverywhere.LoadLocales(localeId, fallbackLocaleId, reloadFallback: true);
            }
        }

        [SettingsUISection(Developer)] public bool HideLocaleAssets { get; set; }

        [SettingsUISection(Developer)]
        [SettingsUIDropdown(typeof(Setting), nameof(GetMods))]
        public string SelectedModDropDown { get; set; } = "None";

        public DropdownItem<string>[] GetMods()
        {
            var list = new List<DropdownItem<string>>();
            I18NEverywhere.UpdateMods();
            list.Add(new DropdownItem<string> {value = "None", displayName = "None"});
            list.Add(new DropdownItem<string> {value = "All", displayName = "All"});
            list.AddRange(I18NEverywhere.ModsFallbackDictionary
                .Where(kv => !HideLocaleAssets || kv.Value is not LocaleAsset)
                .Select(kv => kv.Key)
                .Select(key => new DropdownItem<string>
                    {value = key, displayName = key}));

            return list.ToArray();
        }

        [SettingsUISection(Developer)]
        [SettingsUIButton]
        [SettingsUIConfirmation(
            overrideConfirmMessageId: "I18NEverywhere.I18NEverywhere.I18NEverywhere.Setting.ExportModLocalization")]
        public bool ExportModLocalization
        {
            set
            {
                var directory = Path.Combine(
                    EnvPath.kUserDataPath,
                    "ModsData",
                    "I18NEverywhere");
                var dir = new DirectoryInfo(directory);

                if (!dir.Exists)
                {
                    dir.Create();
                }

                switch (SelectedModDropDown)
                {
                    case "None":
                        return;
                    case "All":
                    {
                        foreach (var pairD in I18NEverywhere.ModsFallbackDictionary)
                        {
                            if (!(pairD.Value is IDictionarySource isss)) continue;
                            var dict = isss
                                .ReadEntries(new List<IDictionaryEntryError>(), new Dictionary<string, int>())
                                .ToDictionary(pair => pair.Key, pair => pair.Value);
                            var str = JsonConvert.SerializeObject(dict, Formatting.Indented);
                            File.WriteAllText(
                                Path.Combine(dir.FullName, Util.SanitizeFileName(pairD.Key + ".json")), str);
                        }

                        return;
                    }
                }

                var obj = I18NEverywhere.ModsFallbackDictionary[SelectedModDropDown];
                if (obj is not IDictionarySource iss) return;
                {
                    var dict = iss.ReadEntries(new List<IDictionaryEntryError>(), new Dictionary<string, int>())
                        .ToDictionary(pair => pair.Key, pair => pair.Value);
                    var str = JsonConvert.SerializeObject(dict, Formatting.Indented);
                    File.WriteAllText(
                        System.IO.Path.Combine(dir.FullName, Util.SanitizeFileName(SelectedModDropDown + ".json")),
                        str);
                }
            }
        }

        public bool NewModDetectMethodEnabled => UseNewModDetectMethod;

        [SettingsUIMultilineText] public string LanguagePacksInfos => string.Empty;

        internal string LanguagePacksState = "";

        public override void SetDefaults()
        {
            LoadLanguagePacks = true;
            Overwrite = true;
            Restrict = false;
            LogKey = false;
            UseNewModDetectMethod = true;
        }
    }

    // ReSharper disable once InconsistentNaming
    public class LocaleEN : IDictionarySource
    {
        private readonly Setting _mSetting;

        public LocaleEN(Setting setting)
        {
            _mSetting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                {_mSetting.GetSettingsLocaleID(), "I18N Everywhere"},
                {_mSetting.GetOptionGroupLocaleID(Setting.General), "General"},
                {_mSetting.GetOptionGroupLocaleID(Setting.Developer), "Developer"},

                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.Overwrite)), "Enable overwrite"},
                {
                    _mSetting.GetOptionDescLocaleID(nameof(Setting.Overwrite)),
                    "Use in-mod localization to override existing localization."
                },

                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.Restrict)), "Restrict Mode"},
                {
                    _mSetting.GetOptionDescLocaleID(nameof(Setting.Restrict)),
                    "Restrict Mode, will prevent mod overwrite another mod's localization."
                },

                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.LogKey)), "Log key when the func is executed"},
                {_mSetting.GetOptionDescLocaleID(nameof(Setting.LogKey)), "Log key when the func is executed"},

                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.ReloadLocalization)), "Reload"},
                {_mSetting.GetOptionDescLocaleID(nameof(Setting.ReloadLocalization)), "Reload localizations."},

                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.SelectedModDropDown)), "Selected mod"},
                {
                    _mSetting.GetOptionDescLocaleID(nameof(Setting.SelectedModDropDown)),
                    "Select what do you want to export."
                },

                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.HideLocaleAssets)), "Hide Locale Assets"},
                {
                    _mSetting.GetOptionDescLocaleID(nameof(Setting.HideLocaleAssets)),
                    "Restart required. Locale Assets will hide in export list."
                },

                {
                    _mSetting.GetOptionLabelLocaleID(nameof(Setting.ExportModLocalization)),
                    "Export selected localization"
                },
                {
                    _mSetting.GetOptionDescLocaleID(nameof(Setting.ExportModLocalization)),
                    "Export selected localization."
                },

                {
                    _mSetting.GetOptionWarningLocaleID(nameof(Setting.ExportModLocalization)),
                    "It will export to ModsData directory."
                },
                {
                    _mSetting.GetOptionLabelLocaleID(nameof(Setting.UseNewModDetectMethod)),
                    "Enable new mod detect method"
                },
                {
                    _mSetting.GetOptionDescLocaleID(nameof(Setting.UseNewModDetectMethod)),
                    "Enable new mod detect method, used by load language packs."
                },
                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.LoadLanguagePacks)), "Load language packs"},
                {_mSetting.GetOptionDescLocaleID(nameof(Setting.LoadLanguagePacks)), "Load language packs"},

                {"Menu.NOTIFICATION_TITLE[I18NEverywhere]", "I18N Everywhere"},
                {"Menu.NOTIFICATION_DESCRIPTION[I18NEverywhere.Detail]", "Localization loaded."},

                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.LanguagePacksInfos)), _mSetting.LanguagePacksState}
            };
        }

        public void Unload()
        {
        }
    }
}