using Colossal.PSI.Environment;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace I18NEverywhere
{
    internal static class Util
    {
        internal static string SanitizeFileName(string fileName)
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

            if (reservedNames.Any(reservedName =>
                    sanitizedFileName.Equals(reservedName, StringComparison.OrdinalIgnoreCase)))
            {
                sanitizedFileName = "_" + sanitizedFileName;
            }

            return sanitizedFileName;
        }

        /// <summary>
        /// Migrate setting to correct path.
        /// </summary>
        internal static void MigrateSetting()
        {
            var oldLocation = Path.Combine(EnvPath.kUserDataPath, "I18nEveryWhere.coc");
            var correctLocation = Path.Combine(EnvPath.kUserDataPath, "ModsSettings", "I18NEverywhere",
                "I18NEveryWhere.coc");

            I18NEverywhere.Logger.Info(oldLocation);
            MigrateMisMigratedSetting();

            if (File.Exists(oldLocation))
            {
                MigrateFile(oldLocation, correctLocation);
            }

            // Handle FallbackSettings.coc
            var fallbackLocation = Path.Combine(EnvPath.kUserDataPath, "FallbackSettings.coc");
            if (File.Exists(fallbackLocation))
            {
                MigrateFile(fallbackLocation, correctLocation);
            }
        }

        private static void MigrateMisMigratedSetting()
        {
            var oldLocation = Path.Combine(EnvPath.kUserDataPath, "ModSettings", "I18NEverywhere", "setting.coc");
            var oldLocation2 = Path.Combine(EnvPath.kUserDataPath, "ModsSettings", "I18NEverywhere", "setting.coc");
            var correctLocation = Path.Combine(EnvPath.kUserDataPath, "ModsSettings", "I18NEverywhere",
                "I18NEverywhere.coc");

            I18NEverywhere.Logger.Info(oldLocation);

            if (File.Exists(oldLocation))
            {
                MigrateFile(oldLocation, correctLocation);
            }

            if (File.Exists(oldLocation2))
            {
                MigrateFile(oldLocation2, correctLocation);
            }
        }

        private static void MigrateFile(string source, string destination)
        {
            var directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory!);

            if (File.Exists(destination))
            {
                File.Delete(source);
            }
            else
            {
                File.Move(source, destination);
            }

            I18NEverywhere.Logger.InfoFormat("File moved from {0} to {1}", source, destination);
        }
    }
}