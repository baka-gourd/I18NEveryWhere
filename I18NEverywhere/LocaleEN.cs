using System.Collections.Generic;

using Colossal;

using Game.Modding;

namespace I18NEverywhere
{
    // ReSharper disable once InconsistentNaming
    public class LocaleEN(Setting setting) : IDictionarySource
    {
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { setting.GetSettingsLocaleID(), "I18N Everywhere" },
                { setting.GetOptionGroupLocaleID(Setting.General), "General" },
                { setting.GetOptionGroupLocaleID(Setting.Developer), "Developer" },

                { setting.GetOptionLabelLocaleID(nameof(Setting.Overwrite)), "Enable overwrite" },
                {
                    setting.GetOptionDescLocaleID(nameof(Setting.Overwrite)),
                    "Use in-mod localization to override existing localization."
                },

                { setting.GetOptionLabelLocaleID(nameof(Setting.Restrict)), "Restrict Mode" },
                {
                    setting.GetOptionDescLocaleID(nameof(Setting.Restrict)),
                    "Restrict Mode, will prevent mod overwrite another mod's localization."
                },

                { setting.GetOptionLabelLocaleID(nameof(Setting.LogKey)), "Log key when the func is executed" },
                { setting.GetOptionDescLocaleID(nameof(Setting.LogKey)), "Log key when the func is executed" },

                { setting.GetOptionLabelLocaleID(nameof(Setting.ReloadLocalization)), "Reload" },
                { setting.GetOptionDescLocaleID(nameof(Setting.ReloadLocalization)), "Reload localizations." },

                { setting.GetOptionLabelLocaleID(nameof(Setting.SelectedModDropDown)), "Selected mod" },
                {
                    setting.GetOptionDescLocaleID(nameof(Setting.SelectedModDropDown)),
                    "Select what do you want to export."
                },

                { setting.GetOptionLabelLocaleID(nameof(Setting.LocaleType)), "Locale Type" },
                {
                    setting.GetOptionDescLocaleID(nameof(Setting.LocaleType)),
                    "Locale Assets Type."
                },

                {
                    setting.GetOptionLabelLocaleID(nameof(Setting.ExportModLocalization)),
                    "Export selected localization"
                },
                {
                    setting.GetOptionDescLocaleID(nameof(Setting.ExportModLocalization)),
                    "Export selected localization."
                },

                {
                    setting.GetOptionWarningLocaleID(nameof(Setting.ExportModLocalization)),
                    "It will export to ModsData directory."
                },
                {
                    setting.GetOptionLabelLocaleID(nameof(Setting.UseNewModDetectMethod)),
                    "Enable new mod detect method"
                },
                {
                    setting.GetOptionDescLocaleID(nameof(Setting.UseNewModDetectMethod)),
                    "Enable new mod detect method, used by load language packs."
                },
                { setting.GetOptionLabelLocaleID(nameof(Setting.LoadLanguagePacks)), "Load language packs" },
                { setting.GetOptionDescLocaleID(nameof(Setting.LoadLanguagePacks)), "Load language packs" },
                { setting.GetOptionLabelLocaleID(nameof(Setting.OpenDirectory)), "Open Directory" },
                { setting.GetOptionDescLocaleID(nameof(Setting.OpenDirectory)), "Open Directory" },
                {
                    setting.GetOptionLabelLocaleID(nameof(Setting.SuppressNullError)),
                    "Suppress NullReferenceException"
                },
                {
                    setting.GetOptionDescLocaleID(nameof(Setting.SuppressNullError)),
                    "<ONLY> suppress NullReference Exception, will <NOT> make game more stable. <MOST> of the time you don't need to turn it on."
                },

                { "Menu.NOTIFICATION_TITLE[I18NEverywhere]", "I18N Everywhere" },
                { "Menu.NOTIFICATION_DESCRIPTION[I18NEverywhere.Detail]", "Localization loaded." },

                { setting.GetOptionLabelLocaleID(nameof(Setting.LanguagePacksInfos)), setting.LanguagePacksState }
            };
        }

        public void Unload()
        {
        }
    }
}
