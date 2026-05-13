namespace Platform.Domain;

public static class ProjectPresentation
{
    private static readonly string[] HiddenSuffixes = ["production", "prod"];

    public static string ToDisplayName(string projectId)
    {
        var words = projectId
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !HiddenSuffixes.Contains(word, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (words.Length == 0)
        {
            words = [projectId];
        }

        return string.Join(" ", words.Select(ToTitleCase));
    }

    public static string ToEnvironment(string projectId)
    {
        return "Connected";
    }

    private static string ToTitleCase(string word)
    {
        return word.Length == 0
            ? word
            : string.Concat(word[..1].ToUpperInvariant(), word[1..].ToLowerInvariant());
    }
}
