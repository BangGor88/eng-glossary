using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text.Json;
using EnglishWords.Core.Abstractions;

namespace EnglishWords.Infrastructure.Dictionaries;

public sealed partial class SwedishWiktionaryDefinitionProvider(HttpClient httpClient) : ISwedishDefinitionProvider
{
    private const int MaximumRetries = 6;
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(20);
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    // Full-text extracts are limited to one page per request, so words are looked up individually.
    public async Task<IReadOnlyDictionary<string, DictionaryDefinition>> GetDefinitionsAsync(
        IReadOnlyCollection<string> words,
        CancellationToken cancellationToken = default)
    {
        var definitions = new Dictionary<string, DictionaryDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in words.Where(word => !string.IsNullOrWhiteSpace(word)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var definition = await GetDefinitionAsync(word, cancellationToken);
            if (definition is not null)
            {
                definitions[word] = definition;
            }
        }

        return definitions;
    }

    public async Task<DictionaryDefinition?> GetDefinitionAsync(
        string word,
        CancellationToken cancellationToken = default)
    {
        var uri = $"w/api.php?action=query&titles={Uri.EscapeDataString(word)}&prop=extracts&explaintext=1&redirects=1&format=json";
        using var response = await SendWithRetryAsync(uri, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var pages = document.RootElement.GetProperty("query").GetProperty("pages");
        var page = pages.EnumerateObject().Select(item => item.Value).FirstOrDefault();
        if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("extract", out var extractElement))
        {
            return null;
        }

        var definition = ParseSwedishDefinition(word, extractElement.GetString() ?? string.Empty);
        return definition is null
            ? null
            : definition with { SourceUrl = $"https://sv.wiktionary.org/wiki/{Uri.EscapeDataString(word)}" };
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await WaitForTurnAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EnglishWordsApp", "1.0"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("(https://github.com/BangGor88/eng-glossary; vocabulary practice app)"));
            var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
            {
                response.EnsureSuccessStatusCode();
                return response;
            }

            if (attempt >= MaximumRetries)
            {
                response.Dispose();
                throw new HttpRequestException(
                    "Swedish Wiktionary is rate-limiting requests (429). Wait a minute and retry.",
                    null,
                    System.Net.HttpStatusCode.TooManyRequests);
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            response.Dispose();
            await Task.Delay(delay > MaximumRetryDelay ? MaximumRetryDelay : delay, cancellationToken);
        }
    }

    private async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastRequestAt + MinimumRequestInterval - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _throttle.Release();
        }
    }

    internal static DictionaryDefinition? ParseSwedishDefinition(string word, string extract)
    {
        var lines = extract.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var swedishStart = Array.FindIndex(lines, line => line.Equals("== Svenska ==", StringComparison.OrdinalIgnoreCase));
        if (swedishStart < 0)
        {
            return null;
        }

        for (var index = swedishStart + 1; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("== ", StringComparison.Ordinal))
            {
                break;
            }

            if (!lines[index].StartsWith("=== ", StringComparison.Ordinal))
            {
                continue;
            }

            var partOfSpeech = lines[index].Trim('=', ' ').ToLowerInvariant();
            if (partOfSpeech is not ("substantiv" or "verb" or "adjektiv" or "adverb" or "pronomen" or "preposition" or "konjunktion" or "interjektion" or "räkneord"))
            {
                continue;
            }

            var foundHeadword = false;
            for (var definitionIndex = index + 1; definitionIndex < lines.Length; definitionIndex++)
            {
                var line = lines[definitionIndex];
                if (line.StartsWith("=== ", StringComparison.Ordinal) || line.StartsWith("== ", StringComparison.Ordinal))
                {
                    index = definitionIndex - 1;
                    break;
                }

                if (!foundHeadword)
                {
                    foundHeadword = true;
                    continue;
                }

                if (!IsDefinition(line, word))
                {
                    continue;
                }

                // An inflected-form entry only points at another headword, so it makes a poor card.
                if (line.Contains("böjningsform av", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var example = definitionIndex + 1 < lines.Length ? lines[definitionIndex + 1] : string.Empty;
                var pronunciation = lines
                    .Skip(index + 1)
                    .TakeWhile(candidate => !candidate.StartsWith("=", StringComparison.Ordinal))
                    .FirstOrDefault(candidate => candidate.StartsWith("uttal:", StringComparison.OrdinalIgnoreCase));
                return new DictionaryDefinition(
                    partOfSpeech,
                    NormalizeDefinition(line),
                    string.Empty,
                    IsExample(example, word) ? example : string.Empty,
                    pronunciation is null ? string.Empty : pronunciation["uttal:".Length..].Trim());
            }
        }

        return null;
    }

    private static bool IsDefinition(string line, string word) =>
        !line.Equals(word, StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Synonymer:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Antonymer:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("uttal:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Etymologi:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Översättningar:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Se även:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Jämför:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Besläktade ord:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Grammatik:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Fraser:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Varianter:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("Sammansättningar:", StringComparison.OrdinalIgnoreCase);

    // Examples are full sentences; a following lowercase or "(" line is another definition.
    private static bool IsExample(string line, string word) =>
        IsDefinition(line, word) &&
        line.Length > 0 &&
        char.IsUpper(line[0]) &&
        !LabelRegex().IsMatch(line);

    private static string NormalizeDefinition(string line)
    {
        var definition = line.TrimStart('*', '#', ' ', '\t');
        var listMarker = definition.IndexOf(". ", StringComparison.Ordinal);
        return listMarker >= 0 && listMarker <= 3 && definition[..listMarker].All(char.IsDigit)
            ? definition[(listMarker + 2)..].Trim()
            : definition;
    }

    // Entry metadata such as "Hyponymer: ..." or "Se även tesaurus: ..." is not an example sentence.
    [GeneratedRegex(@"^\p{L}+(\s\p{L}+){0,2}:")]
    private static partial Regex LabelRegex();
}