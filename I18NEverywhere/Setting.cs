using Colossal;
using Colossal.IO.AssetDatabase;

using Game.Modding;
using Game.SceneFlow;
using Game.Settings;

using System.Collections.Generic;

namespace I18NEverywhere
{
    [SettingsUIShowGroupName(General,Developer)]
    [SettingsUIGroupOrder(General,Developer)]
    public class Setting : ModSetting
    {
        public const string General = "General";
        public const string Developer = "Developer";

        public Setting(IMod mod) : base(mod)
        {

        }

        [SettingsUISection(General)]
        public bool Overwrite { get; set; }

        [SettingsUISection(General)]
        public bool Restrict { get; set; }

        [SettingsUISection(Developer)]
        public bool LogKey { get; set; }

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

        public override void SetDefaults()
        {
            Overwrite = true;
            Restrict = false;
            LogKey = false;
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
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { _mSetting.GetSettingsLocaleID(), "I18N Everywhere" },
                { _mSetting.GetOptionGroupLocaleID(Setting.General), "General" },
                { _mSetting.GetOptionGroupLocaleID(Setting.Developer), "Developer" },

                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.Overwrite)), "Enable overwrite" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.Overwrite)), "Use in-mod localization to override existing localization." },

                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.Restrict)), "Restrict Mode"},
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.Restrict)), "Restrict Mode, will prevent mod overwrite another mod's localization."},

                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.LogKey)), "Log key when the func is executed" },
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.LogKey)), "Log key when the func is executed" },

                { _mSetting.GetOptionLabelLocaleID(nameof(Setting.ReloadLocalization)), "Reload"},
                { _mSetting.GetOptionDescLocaleID(nameof(Setting.ReloadLocalization)), "Reload localizations."},

                { "Menu.NOTIFICATION_TITLE[I18NEverywhere]", "I18N Everywhere" },
                { "Menu.NOTIFICATION_DESCRIPTION[I18NEverywhere.Detail]", "Localization loaded." }
            };
        }

        public void Unload()
        {

        }
    }
}
