using Colossal;
using Colossal.IO.AssetDatabase;

using Game.Modding;
using Game.Settings;

using System.Collections.Generic;

namespace I18NEverywhere
{
    public class Setting : ModSetting
    {
        public const string KSection = "General";

        public Setting(IMod mod) : base(mod)
        {

        }

        [SettingsUISection(KSection)]
        public bool Overwrite { get; set; }
        [SettingsUISection(KSection)]
        public bool ScanLocalModDirectory { get; set; }
        [SettingsUISection(KSection)]
        public bool ScanPModDirectory { get; set; }
        [SettingsUISection(KSection)]
        public bool LogKey { get; set; }

        public override void SetDefaults()
        {
            Overwrite = true;
            ScanLocalModDirectory = false;
            ScanPModDirectory = false;
            LogKey = false;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting _mSetting;
        public LocaleEN(Setting setting)
        {
            _mSetting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { _mSetting.GetSettingsLocaleID(), "I18N Everywhere" },
                { _mSetting.GetOptionTabLocaleID(Setting.KSection), "General" },

                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.Overwrite)), "Enable overwrite" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.Overwrite)), "Use in-mod localization to override existing localization." },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.ScanLocalModDirectory)), "Force scan Local Mods" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.ScanLocalModDirectory)), "Force scan <CSII_USERDATAPATH>/Mods directory, usually use when develop a local mod and I18n Everywhere load as paradox mod." },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.ScanPModDirectory)), "Force scan Paradox Mods" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.ScanPModDirectory)), "Force scan <CSII_USERDATAPATH>/.cache/Mods/mods_subscribed directory, usually use when I18n Everywhere load as local mod." },
                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.LogKey)), "Log key when the func is executed" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.LogKey)), "Log key when the func is executed" },

                { "Menu.NOTIFICATION_TITLE[I18NEverywhere]", "I18N Everywhere" },
                { "Menu.NOTIFICATION_DESCRIPTION[I18NEverywhere.Detail]", "Localization loaded." }
            };
        }

        public void Unload()
        {

        }
    }
}
