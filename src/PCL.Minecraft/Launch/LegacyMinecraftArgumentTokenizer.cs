using System.Text;

namespace PCL3.Minecraft.Launch;

public static class LegacyMinecraftArgumentTokenizer
{
    public static IReadOnlyList<string> Tokenize(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var escaping = false;
        var tokenStarted = false;

        foreach (var character in arguments)
        {
            if (escaping)
            {
                current.Append(character);
                tokenStarted = true;
                escaping = false;
                continue;
            }

            if (character == '\\' && inQuotes)
            {
                escaping = true;
                tokenStarted = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                tokenStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (tokenStarted)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    tokenStarted = false;
                }

                continue;
            }

            current.Append(character);
            tokenStarted = true;
        }

        if (escaping)
        {
            current.Append('\\');
        }

        if (inQuotes)
        {
            throw new FormatException("Legacy Minecraft argument string contains an unterminated quote.");
        }

        if (tokenStarted)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
