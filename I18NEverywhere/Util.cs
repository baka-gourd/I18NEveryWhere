using System;
using System.IO;
using System.Text.RegularExpressions;

namespace I18NEverywhere
{
    public class Util
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

            foreach (var reservedName in reservedNames)
            {
                if (sanitizedFileName.Equals(reservedName, StringComparison.OrdinalIgnoreCase))
                {
                    sanitizedFileName = "_" + sanitizedFileName;
                    break;
                }
            }

            return sanitizedFileName;
        }
    }
}