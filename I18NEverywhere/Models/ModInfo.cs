namespace I18NEverywhere.Models;

public class ModInfo
{
    public string Name { get; set; }
    public string Path { get; set; }
    public bool IsLanguagePack { get; set; }

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