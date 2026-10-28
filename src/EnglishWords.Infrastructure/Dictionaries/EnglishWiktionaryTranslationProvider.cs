using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishWords.Core.Abstractions;

namespace EnglishWords.Infrastructure.Dictionaries;

public sealed partial class EnglishWiktionaryTranslationProvider(HttpClient httpClient) : ISwedishTranslationProvider
{
    private const int MaximumRetries = 6;
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(20);
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public async Task<string?> GetSwedishTranslationAsync(
        string englishWord,
        CancellationToken cancellationToken = default)
    {
        var uri = $"w/api.php?action=parse&page={Uri.EscapeDataString(englishWord)}&prop=wikitext&redirects=1&format=json";
        using var response = await SendWithRetryAsync(uri, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("parse", out var parse))
        {
            return null;
        }

        return ParseSwedishTranslation(parse.GetProperty("wikitext").GetProperty("*").GetString() ?? string.Empty);
    }

    public static string? ParseSwedishTranslation(string wikitext)
    {
        var englishStart = wikitext.IndexOf("==English==", StringComparison.Ordinal);
        if (englishStart < 0)
        {
            return null;
        }

        var englishEnd = EnglishSectionEndRegex().Match(wikitext, englishStart + "==English==".Length);
        var section = englishEnd.Success
            ? wikitext[englishStart..englishEnd.Index]
            : wikitext[englishStart..];

        return SwedishTranslationRegex()
            .Matches(section)
            .Select(match => match.Groups[1].Value.Trim())
            .FirstOrDefault(translation => translation.Length > 0 && !translation.Contains(' '));
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
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                response.EnsureSuccessStatusCode();
                return response;
            }

            if (attempt >= MaximumRetries)
            {
                response.Dispose();
                throw new HttpRequestException(
                    "English Wiktionary is rate-limiting requests (429). Wait a minute and retry.",
                    null,
                    HttpStatusCode.TooManyRequests);
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

    [GeneratedRegex(@"\{\{t\+?\|sv\|([^}|]+)")]
    private static partial Regex SwedishTranslationRegex();

    [GeneratedRegex(@"^==[^=]", RegexOptions.Multiline)]
    private static partial Regex EnglishSectionEndRegex();
}
