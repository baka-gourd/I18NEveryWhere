using System;

namespace I18NEverywhere.Models
{
    public class ModInfo : IEquatable<ModInfo>
    {
        public string Name { get; init; }
        public string Path { get; init; }
        public bool IsLanguagePack { get; init; }

        public bool Equals(ModInfo other)
        {
            if (other is null)
            {
                return false;
            }

            return other.Path == Path;
        }

        public override bool Equals(object obj)
        {
            return obj is ModInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Path != null ? Path.GetHashCode() : 0;
        }
    }
}
