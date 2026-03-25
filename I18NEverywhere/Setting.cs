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
namespace I18NEverywhere;

[FileLocation(@"ModsSettings\I18NEverywhere\I18NEverywhere")]
[SettingsUIShowGroupName(General, Developer)]
[SettingsUIGroupOrder(General, Developer)]
public class Setting(IMod mod) : ModSetting(mod)
{
    public const string General = "General";
    public const string Developer = "Developer";

    [SettingsUISection(General)] public bool Overwrite { get; set; } = true;

    [SettingsUISection(General)] public bool Restrict { get; set; }

    [SettingsUISection(General)]
    [SettingsUIDisableByCondition(typeof(Setting), nameof(UseNewModDetectMethod), true)]
    public bool LoadLanguagePacks { get; set; } = true;

    public bool CanLoadLanguagePacks => LoadLanguagePacks && UseNewModDetectMethod;

    [SettingsUISection(Developer)] public bool LogKey { get; set; }

    [SettingsUISection(Developer)] public bool UseNewModDetectMethod { get; set; } = true;

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
            { value = key, displayName = key }));

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
                            if (pairD.Value is not IDictionarySource dictionarySource) continue;
                            switch (LocaleType)
                            {
                                case "LocaleAsset" when pairD.Value is not LocaleAsset:
                                case "Mod" when pairD.Value is LocaleAsset:
                                    continue;
                            }

                            var dict = dictionarySource
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
            if (obj is not IDictionarySource selectedSource) return;
            {
                var dict = selectedSource.ReadEntries(new List<IDictionaryEntryError>(), new Dictionary<string, int>())
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                var str = JsonConvert.SerializeObject(dict, Formatting.Indented);
                File.WriteAllText(
                    Path.Combine(dir.FullName, Util.SanitizeFileName(SelectedModDropDown + ".json")),
                    str);
            }
        }
    }

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
