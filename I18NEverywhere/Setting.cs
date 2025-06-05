using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.SceneFlow;
using Game.Settings;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Game.UI.Widgets;
using Newtonsoft.Json;
using Colossal.PSI.Environment;
using Game.UI.Localization;
// ReSharper disable ValueParameterNotUsed
#pragma warning disable CA1822
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
        public bool ReloadLocalization
        {
            // ReSharper disable once ValueParameterNotUsed
            set
            {
                var localeId = GameManager.instance.localizationManager.activeLocaleId;
                var fallbackLocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
                I18NEverywhere.LoadLocales(localeId, fallbackLocaleId, reloadFallback: true);
            }
        }

        [SettingsUISection(Developer)] public bool SuppressNullError { get; set; }

        [SettingsUISection(Developer)]
        [SettingsUIDropdown(typeof(Setting), nameof(GetTypes))]
        [SettingsUISetter(typeof(Setting), nameof(IncrementVersion))]
        public string LocaleType { get; set; } = "All";

        [SettingsUISection(Developer)]
        [SettingsUIDropdown(typeof(Setting), nameof(GetMods))]
        [SettingsUIValueVersion(typeof(I18NEverywhere), "GetVersion")]
        public string SelectedModDropDown { get; set; } = "None";

        public DropdownItem<string>[] GetMods()
        {
            var list = new List<DropdownItem<string>>
            {
                new() {value = "None", displayName = "None"},
                new() {value = "All", displayName = "All"}
            };
            I18NEverywhere.UpdateMods();
            list.AddRange(I18NEverywhere.ModsFallbackDictionary
                .Where(kv =>
                {
                    return LocaleType switch
                    {
                        "All" => true,
                        "Mod" => kv.Value is not LocaleAsset,
                        "LocaleAsset" => kv.Value is LocaleAsset,
                        _ => false
                    };
                })
                .Select(kv => kv.Key)
                .Select(key => new DropdownItem<string>
                    {value = key, displayName = key}));

            return list.ToArray();
        }

        public static DropdownItem<string>[] GetTypes()
        {
            var list = new DropdownItem<string>[]
            {
                new() {value = "All", displayName = LocalizedString.Value("All")},
                new() {value = "LocaleAsset", displayName = LocalizedString.Value("LocaleAsset")},
                new() {value = "Mod", displayName = LocalizedString.Value("Mod")}
            };
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
                        Path.Combine(dir.FullName, Util.SanitizeFileName(SelectedModDropDown + ".json")),
                        str);
                }
            }
        }

        public bool NewModDetectMethodEnabled => UseNewModDetectMethod;

        [SettingsUISection(Developer)]
        [SettingsUIButton]
        public bool OpenDirectory
        {
            set
            {
                var directory = Path.Combine(
                    EnvPath.kUserDataPath,
                    "ModsData",
                    "I18NEverywhere");
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
        }

        [SettingsUIMultilineText] public string LanguagePacksInfos => string.Empty;

        internal string LanguagePacksState = "";

        public override void SetDefaults()
        {
            LoadLanguagePacks = true;
            Overwrite = true;
            Restrict = false;
            LogKey = false;
            UseNewModDetectMethod = true;
            SuppressNullError = false;
        }

        public static void IncrementVersion(string _)
        {
             I18NEverywhere.IncrementVersion();
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

                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.LocaleType)), "Locale Type"},
                {
                    _mSetting.GetOptionDescLocaleID(nameof(Setting.LocaleType)),
                    "Locale Assets Type."
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
                {_mSetting.GetOptionLabelLocaleID(nameof(Setting.OpenDirectory)), "Open Directory"},
                {_mSetting.GetOptionDescLocaleID(nameof(Setting.OpenDirectory)), "Open Directory"},
                {
                    _mSetting.GetOptionLabelLocaleID(nameof(Setting.SuppressNullError)),
                    "Suppress NullReferenceException"
                },
                {
                    _mSetting.GetOptionDescLocaleID(nameof(Setting.SuppressNullError)),
                    "<ONLY> suppress NullReference Exception, will <NOT> make game more stable. <MOST> of the time you don't need to turn it on."
                },

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