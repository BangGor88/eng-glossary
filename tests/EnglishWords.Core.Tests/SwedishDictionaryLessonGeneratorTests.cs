using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Infrastructure.Dictionaries;
using EnglishWords.Infrastructure.Ollama;
using Xunit;

namespace EnglishWords.Core.Tests;

public sealed class SwedishDictionaryLessonGeneratorTests
{
    [Fact]
    public async Task Provider_ExtractsDefinitionExampleAndPronunciationFromSwedishEntry()
    {
        const string response = """
            {"query":{"pages":{"123":{"pageid":123,"title":"sommar","extract":"== Svenska ==\n\n=== Substantiv ===\n\nsommar\n\nuttal: sɔmar\n(årstider) den varmaste årstiden, vilken följer våren och kommer innan hösten\nPå sommaren badar vi i sjön.\nSynonymer: sumor"}}}}
            """;
        using var client = CreateClient(response);
        var provider = new SwedishWiktionaryDefinitionProvider(client);

        var result = await provider.GetDefinitionAsync("sommar");

        Assert.NotNull(result);
        Assert.Equal("substantiv", result.PartOfSpeech);
        Assert.Equal("(årstider) den varmaste årstiden, vilken följer våren och kommer innan hösten", result.Definition);
        Assert.Equal("På sommaren badar vi i sjön.", result.Example);
        Assert.Equal("sɔmar", result.Pronunciation);
        Assert.Equal("https://sv.wiktionary.org/wiki/sommar", result.SourceUrl);
    }

    [Fact]
    public async Task Provider_SkipsPronunciationMetadataAndUsesSwedishDefinition()
    {
        const string response = """
            {"query":{"pages":{"123":{"pageid":123,"title":"snart","extract":"== Svenska ==\n\n=== Adverb ===\n\nsnart\n\nuttal: sna:rtsnɑːʈ\navledning till adjektivet snar; inom nära framtid\nSynonymer: inom kort"}}}}
            """;
        using var client = CreateClient(response);
        var provider = new SwedishWiktionaryDefinitionProvider(client);

        var result = await provider.GetDefinitionAsync("snart");

        Assert.NotNull(result);
        Assert.Equal("adverb", result.PartOfSpeech);
        Assert.Equal("avledning till adjektivet snar; inom nära framtid", result.Definition);
        Assert.Equal(string.Empty, result.Example);
    }

    [Fact]
    public async Task Provider_DoesNotTreatEntryMetadataAsExample()
    {
        const string response = """
            {"query":{"pages":{"123":{"pageid":123,"title":"timjan","extract":"== Svenska ==\n\n=== Substantiv ===\n\ntimjan\n\nsläkte växter som används som krydda\nHyponymer: backtimjan, kryddtimjan"}}}}
            """;
        using var client = CreateClient(response);
        var provider = new SwedishWiktionaryDefinitionProvider(client);

        var result = await provider.GetDefinitionAsync("timjan");

        Assert.Equal(string.Empty, result!.Example);
    }

    [Fact]
    public async Task Provider_RejectsInflectedFormEntries()
    {
        const string response = """
            {"query":{"pages":{"123":{"pageid":123,"title":"lyckades","extract":"== Svenska ==\n\n=== Verb ===\n\nlyckades\n\nböjningsform av lyckas\nDet lyckades inte."}}}}
            """;
        using var client = CreateClient(response);
        var provider = new SwedishWiktionaryDefinitionProvider(client);

        Assert.Null(await provider.GetDefinitionAsync("lyckades"));
    }

    [Fact]
    public async Task Provider_RetriesAfterRateLimit()
    {
        const string response = """
            {"query":{"pages":{"123":{"pageid":123,"title":"sommar","extract":"== Svenska ==\n\n=== Substantiv ===\n\nsommar\n\nden varmaste årstiden"}}}}
            """;
        var handler = new CountingResponseHandler(response, rateLimitedRequests: 1);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://sv.wiktionary.org/") };
        var provider = new SwedishWiktionaryDefinitionProvider(client);

        var result = await provider.GetDefinitionAsync("sommar");

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("den varmaste årstiden", result!.Definition);
    }

    [Fact]
    public void TranslationProvider_ReadsSwedishTranslationFromEnglishSectionOnly()
    {
        const string wikitext = """
            ==Dutch==
            {{t|sv|holländskt}}
            ==English==
            ===Adjective===
            ====Translations====
            {{trans-top|short}}
            * Swedish: {{t+|sv|kort}}, {{t|sv|kortfattad}}
            ==French==
            {{t|sv|fransk}}
            """;

        Assert.Equal("kort", EnglishWiktionaryTranslationProvider.ParseSwedishTranslation(wikitext));
    }

    [Fact]
    public void TranslationProvider_ReturnsNullWhenNoSwedishTranslationExists()
    {
        Assert.Null(EnglishWiktionaryTranslationProvider.ParseSwedishTranslation("==English==\n{{t|de|sanft}}"));
    }

    [Fact]
    public async Task Generator_TranslatesEnglishCandidatesAndUsesSwedishDictionaryData()
    {
        var generator = new SwedishDictionaryLessonGenerator(
            new EnglishCandidates(["summer", "gentle", "cat"]),
            new FakeTranslations(),
            new FakeDefinitionProvider());

        var result = await generator.GenerateAsync(new DateOnly(2026, 10, 28), 2, []);

        Assert.Equal(["sommar", "katt"], result.Select(word => word.Word));
        Assert.All(result, word =>
        {
            Assert.Equal(VocabularyLanguage.Swedish, word.Language);
            Assert.Equal("substantiv", word.PartOfSpeech);
            Assert.StartsWith("svensk definition för", word.Definition);
            Assert.StartsWith("Exempel med", word.Example);
            Assert.StartsWith("https://sv.wiktionary.org/wiki/", word.DefinitionSource);
        });
    }

    [Fact]
    public async Task Generator_RequestsMoreEnglishCandidatesWhenTranslationsAreMissing()
    {
        var candidates = new EnglishCandidates(["gentle", "noword"], ["summer", "cat", "dog"]);
        var generator = new SwedishDictionaryLessonGenerator(
            candidates,
            new FakeTranslations(),
            new FakeDefinitionProvider());

        var result = await generator.GenerateAsync(new DateOnly(2026, 10, 28), 3, []);

        Assert.Equal(["sommar", "katt", "hund"], result.Select(word => word.Word));
        Assert.Equal(2, candidates.CallCount);
        Assert.Contains("gentle", candidates.LastAvoidedWords);
    }

    [Fact]
    public async Task Generator_SkipsEntriesWithoutSwedishExample()
    {
        var generator = new SwedishDictionaryLessonGenerator(
            new EnglishCandidates(["soon", "summer"]),
            new FakeTranslations(),
            new FakeDefinitionProvider());

        var result = await generator.GenerateAsync(new DateOnly(2026, 10, 28), 1, []);

        Assert.Equal("sommar", Assert.Single(result).Word);
    }

    [Fact]
    public async Task Generator_FailsWithClearMessageWhenTooFewWordsAreFound()
    {
        var generator = new SwedishDictionaryLessonGenerator(
            new EnglishCandidates(["gentle"]),
            new FakeTranslations(),
            new FakeDefinitionProvider());

        var exception = await Assert.ThrowsAsync<OllamaGenerationException>(() =>
            generator.GenerateAsync(new DateOnly(2026, 10, 28), 1, []));

        Assert.Contains("Only 0 of 1", exception.Message);
    }

    private static HttpClient CreateClient(string response) =>
        new(new CountingResponseHandler(response))
        {
            BaseAddress = new Uri("https://sv.wiktionary.org/")
        };

    private sealed class CountingResponseHandler(string response, int rateLimitedRequests = 0) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount <= rateLimitedRequests)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(limited);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeTranslations : ISwedishTranslationProvider
    {
        private static readonly Dictionary<string, string> Words = new()
        {
            ["summer"] = "sommar",
            ["cat"] = "katt",
            ["dog"] = "hund",
            ["soon"] = "snart"
        };

        public Task<string?> GetSwedishTranslationAsync(string englishWord, CancellationToken cancellationToken = default) =>
            Task.FromResult(Words.GetValueOrDefault(englishWord));
    }

    private sealed class FakeDefinitionProvider : ISwedishDefinitionProvider
    {
        public Task<IReadOnlyDictionary<string, DictionaryDefinition>> GetDefinitionsAsync(
            IReadOnlyCollection<string> words,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DictionaryDefinition>>(
                words.ToDictionary(word => word, Create, StringComparer.OrdinalIgnoreCase));

        public Task<DictionaryDefinition?> GetDefinitionAsync(string word, CancellationToken cancellationToken = default) =>
            Task.FromResult<DictionaryDefinition?>(Create(word));

        private static DictionaryDefinition Create(string word) => new(
            "substantiv",
            $"svensk definition för {word}",
            $"https://sv.wiktionary.org/wiki/{Uri.EscapeDataString(word)}",
            word == "snart" ? string.Empty : $"Exempel med {word}.",
            $"uttal {word}");
    }

    private sealed class EnglishCandidates(params string[][] batches) : ILessonGenerator
    {
        public int CallCount { get; private set; }

        public IReadOnlyCollection<string> LastAvoidedWords { get; private set; } = [];

        public Task<IReadOnlyList<GeneratedWord>> GenerateAsync(
            DateOnly date,
            int count,
            IReadOnlyCollection<string> avoidWords,
            CancellationToken cancellationToken = default,
            IProgress<string>? progress = null)
        {
            LastAvoidedWords = avoidWords.ToArray();
            var batch = CallCount < batches.Length ? batches[CallCount] : [];
            CallCount++;
            return Task.FromResult<IReadOnlyList<GeneratedWord>>(
                batch.Select(word => new GeneratedWord(word, "noun", "english definition", "English example.", "pron")).ToArray());
        }
    }
}
