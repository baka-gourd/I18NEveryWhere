using System.Text.Encodings.Web;
using System.Text.Json;

using Colossal;

namespace I18NEverywhere.LocaleGen
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var setting = new Setting(new I18NEverywhere());
            var locale = new LocaleEN(setting);
            var e = new Dictionary<string, string>(
                locale.ReadEntries([], []));
            var str = JsonSerializer.Serialize(e, new JsonSerializerOptions()
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            Console.WriteLine(str);
        }
    }
}
