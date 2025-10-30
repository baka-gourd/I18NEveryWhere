namespace I18NEverywhere.Models
{
    public class ModInfo
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

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode() : 0) * 397) ^ (Path != null ? Path.GetHashCode() : 0);
            }
        }
    }
}