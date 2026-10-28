using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishWords.Core.Abstractions;

namespace EnglishWords.Infrastructure.Dictionaries;

public sealed partial class EnglishWiktionaryDefinitionProvider(HttpClient httpClient) : IEnglishDefinitionProvider
{
    private static readonly HashSet<string> PartsOfSpeech = new(StringComparer.OrdinalIgnoreCase)
    {
        "Noun", "Verb", "Adjective", "Adverb", "Pronoun", "Preposition", "Conjunction", "Interjection"
    };

    private static readonly string[] SkippedSenseMarkers =
    [
        "{{plural of", "{{past tense of", "{{past participle of", "{{present participle of", "{{inflection of",
        "{{en-past of", "{{en-third-person", "{{alternative", "{{alt form", "{{misspelling", "{{obsolete spelling",
        "{{synonym of", "{{abbreviation of", "{{initialism of", "{{non-gloss", "{{comparative of", "{{superlative of",
        "{{lb|en|obsolete", "{{lb|en|rare", "{{lb|en|archaic", "{{lb|en|dated", "{{lb|en|slang", "{{lb|en|vulgar",
        "{{lb|en|offensive", "{{lb|en|derogatory", "{{lb|en|nonstandard", "{{lb|en|dialectal", "{{lb|en|historical"
    ];

    private readonly WiktionaryApiClient _client = new(httpClient, "English Wiktionary");

    public async Task<DictionaryDefinition?> GetDefinitionAsync(
        string word,
        CancellationToken cancellationToken = default)
    {
        var uri = $"w/api.php?action=parse&page={Uri.EscapeDataString(word)}&prop=wikitext&redirects=1&format=json";
        using var response = await _client.GetAsync(uri, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("parse", out var parse))
        {
            return null;
        }

        var definition = ParseEnglishDefinition(parse.GetProperty("wikitext").GetProperty("*").GetString() ?? string.Empty);
        return definition is null
            ? null
            : definition with { SourceUrl = $"https://en.wiktionary.org/wiki/{Uri.EscapeDataString(word)}#English" };
    }

    public static DictionaryDefinition? ParseEnglishDefinition(string wikitext)
    {
        var start = wikitext.IndexOf("==English==", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var lines = wikitext[(start + "==English==".Length)..].Split('\n');
        var pronunciation = string.Empty;
        string? partOfSpeech = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("----", StringComparison.Ordinal) ||
                (line.StartsWith("==", StringComparison.Ordinal) && !line.StartsWith("===", StringComparison.Ordinal)))
            {
                break;
            }

            var heading = HeadingRegex().Match(line);
            if (heading.Success)
            {
                var name = heading.Groups[1].Value.Trim();
                partOfSpeech = PartsOfSpeech.Contains(name) ? name.ToLowerInvariant() : null;
                continue;
            }

            if (pronunciation.Length == 0)
            {
                var ipa = IpaRegex().Match(line);
                if (ipa.Success)
                {
                    pronunciation = ipa.Groups[1].Value;
                }
            }

            if (partOfSpeech is null || !line.StartsWith("# ", StringComparison.Ordinal))
            {
                continue;
            }

            if (SkippedSenseMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var definition = Clean(line[2..]);
            if (definition.Length < 8 || definition.EndsWith(':'))
            {
                continue;
            }

            var example = FindExample(lines, index + 1);
            if (example.Length == 0)
            {
                continue;
            }

            return new DictionaryDefinition(partOfSpeech, definition, string.Empty, example, pronunciation);
        }

        return null;
    }

    private static string FindExample(string[] lines, int startIndex)
    {
        for (var index = startIndex; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith("#:", StringComparison.Ordinal))
            {
                if (line.StartsWith('#'))
                {
                    break;
                }

                continue;
            }

            var match = ExampleRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var text = Clean(LinkRegex().Replace(match.Groups[1].Value, "$1").Split('|')[0]);
            if (text.Length >= 15 && !text.Contains("{{") && !text.Contains('\n'))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string Clean(string wikitext)
    {
        var text = LinkRegex().Replace(wikitext, "$1");
        string previous;
        do
        {
            previous = text;
            text = TemplateRegex().Replace(text, string.Empty);
        }
        while (text != previous);

        text = text.Replace("'''", string.Empty).Replace("''", string.Empty);
        text = HtmlRegex().Replace(text, string.Empty);
        return WhitespaceRegex().Replace(text, " ").Trim().TrimEnd(';', ',').Trim();
    }

    [GeneratedRegex(@"^=+\s*([^=]+?)\s*=+$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"\{\{IPA\|en\|(/[^/|}]+/)")]
    private static partial Regex IpaRegex();

    [GeneratedRegex(@"^#:\s*\{\{ux\|en\|(.+?)\}\}\s*$")]
    private static partial Regex ExampleRegex();

    [GeneratedRegex(@"\[\[(?:[^\]|]*\|)?([^\]|]*)\]\]")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"\{\{[^{}]*\}\}")]
    private static partial Regex TemplateRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
