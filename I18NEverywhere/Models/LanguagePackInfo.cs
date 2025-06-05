using JetBrains.Annotations;

namespace I18NEverywhere.Models
{
    public class LanguagePackInfo
    {
        [CanBeNull] public string Name { get; set; }
        [CanBeNull] public string IncludedLanguage { get; set; }
        [CanBeNull] public string Author { get; set; }
        [CanBeNull] public string Description { get; set; }
    }
}