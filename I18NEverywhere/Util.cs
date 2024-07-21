using Colossal.PSI.Environment;

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace I18NEverywhere
{
    public static class Util
    {
        public static string SanitizeFileName(string fileName)
        {
            var invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            var escapedInvalidChars = Regex.Escape(invalidChars);
            var invalidCharsPattern = $"[{escapedInvalidChars}]";
            var sanitizedFileName = Regex.Replace(fileName, invalidCharsPattern, "-");

            string[] reservedNames =
            {
                "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };

            if (reservedNames.Any(reservedName => sanitizedFileName.Equals(reservedName, StringComparison.OrdinalIgnoreCase)))
            {
                sanitizedFileName = "_" + sanitizedFileName;
            }

            return sanitizedFileName;
        }

        public static void MigrateSetting()
        {
            var oldLocation = Path.Combine(EnvPath.kUserDataPath, $"I18nEveryWhere.coc");
            I18NEverywhere.Logger.Info(oldLocation);
            MigrateMisMigratedSetting();
            if (!File.Exists(oldLocation)) return;

            var directory = Path.Combine(
                EnvPath.kUserDataPath,
                "ModsSettings",
                "I18NEverywhere");

            var correctLocation = Path.Combine(
                directory, "setting.coc");
            I18NEverywhere.Logger.Info(correctLocation);
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

        private static void MigrateMisMigratedSetting()
        {
            var oldLocation = Path.Combine(
                EnvPath.kUserDataPath,
                "ModSettings",
                "I18NEverywhere",
                "setting.coc");
            I18NEverywhere.Logger.Info(oldLocation);

            if (!File.Exists(oldLocation)) return;

            var directory = Path.Combine(
                EnvPath.kUserDataPath,
                "ModsSettings",
                "I18NEverywhere");

            var correctLocation = Path.Combine(
                directory, "setting.coc");
            I18NEverywhere.Logger.Info(correctLocation);
            Directory.CreateDirectory(directory);

            if (File.Exists(correctLocation))
            {
                File.Delete(oldLocation);
            }
            else
            {
                File.Move(oldLocation, correctLocation);
            }
            Directory.Delete(Path.Combine(
                EnvPath.kUserDataPath,
                "ModSettings",
                "I18NEverywhere"), true);
        }
    }
}