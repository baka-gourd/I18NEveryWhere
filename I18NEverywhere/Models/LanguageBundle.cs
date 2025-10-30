using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace I18NEverywhere.Models;

public sealed class LanguageBundle : IDisposable
{
    public string[] IncludedLanguage { get; private set; }
    private FileStream FileStream { get; set; }
    public bool IsCentralized { get; private set; }

    private ZipFile _zipFile;

    private LanguageBundle(FileStream fs, ZipFile zipFile, bool isCentralized, string[] entries)
    {
        FileStream = fs;
        _zipFile = zipFile;
        IsCentralized = isCentralized;
        IncludedLanguage = entries;
    }

    public Dictionary<string, string> ReadContent(string id)
    {
        if (!IsCentralized)
        {
            var entryName = $"{id}.json";
            var entry = _zipFile.GetEntry(entryName)
                        ?? throw new FileNotFoundException($"Cannot find {entryName} in bundle.");

            using var entryStream = _zipFile.GetInputStream(entry);
            using var reader = new StreamReader(entryStream, Encoding.UTF8);
            var json = reader.ReadToEnd();
            var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? throw new InvalidDataException($"Failed to deserialize {entryName}.");

            return dict;
        }

        var result = new Dictionary<string, string>();
        var prefix = $"{id}/";
        foreach (ZipEntry entry in _zipFile)
        {
            if (entry.IsDirectory)
                continue;

            if (!entry.Name.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase) ||
                !entry.Name.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase)) continue;
            using var entryStream = _zipFile.GetInputStream(entry);
            using var reader = new StreamReader(entryStream, Encoding.UTF8);
            var json = reader.ReadToEnd();

            var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                       ?? throw new InvalidDataException($"Failed to deserialize {entry.Name}.");
            foreach (var kv in dict)
            {
                result[kv.Key] = kv.Value;
            }
        }

        return result;
    }

    public static LanguageBundle ReadBundle(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Bundle not found: {path}");

        var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var zipFile = new ZipFile(fs);
        var isCentralized = zipFile.Cast<ZipEntry>().Any(entry => !entry.IsDirectory && entry.Name.Equals(".centralized", StringComparison.OrdinalIgnoreCase));

        string[] entries;
        if (!isCentralized)
        {
            entries = (
                from ZipEntry entry in zipFile
                where !entry.IsDirectory
                where entry.Name.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase)
                select Path.GetFileNameWithoutExtension(entry.Name)
            ).Distinct(StringComparer.InvariantCultureIgnoreCase).ToArray();
        }
        else
        {
            var localeSet = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            var sp = new[] {'/'};
            foreach (ZipEntry entry in zipFile)
            {
                if (entry.IsDirectory) continue;
                var parts = entry.Name.Split(sp, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    localeSet.Add(parts[0]);
                }
            }

            entries = localeSet.ToArray();
        }

        return new LanguageBundle(fs, zipFile, isCentralized, entries);
    }

    public void Dispose()
    {
        _zipFile?.Close();
        _zipFile = null;
        FileStream?.Dispose();
        FileStream = null;
    }
}