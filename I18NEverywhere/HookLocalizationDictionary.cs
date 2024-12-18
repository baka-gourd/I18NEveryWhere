using System;
using System.Diagnostics;
using Colossal.Localization;

#pragma warning disable CA1711
#pragma warning disable CA1707

namespace I18NEverywhere
{
    public class HookLocalizationDictionary
    {
        public static bool Prefix(string entryID, ref string value, ref bool __result,
            LocalizationDictionary __instance)
        {
            if (I18NEverywhere.Setting.LogKey)
            {
                I18NEverywhere.Logger.Info(entryID);
            }

            if (!I18NEverywhere.Setting.Overwrite)
            {
                if (__instance.ContainsID(entryID, ignoreFallbackEntries: true))
                {
                    return true;
                }
            }

            // ReSharper disable once InlineOutVariableDeclaration
            string result;
            // Based on in rare condition entryID will be null.
            if (entryID == null)
            {
                value = "ERR: WHY NULL";
                var ex = new ArgumentNullException(nameof(entryID),
                    "WHY NULL? If you see this, please ask for help from others as this is caused by other mods.");
                I18NEverywhere.Logger.Error(ex, ex.Message);
                __result = true;
                return false;
            }

            if (I18NEverywhere.CurrentLocaleDictionary.TryGetValue(entryID, out result))
            {
                value = result;
                __result = true;
                return false;
            }

            if (I18NEverywhere.FallbackLocaleDictionary.TryGetValue(entryID, out result))
            {
                value = result;
                __result = true;
                return false;
            }

            return true;
        }
    }
}