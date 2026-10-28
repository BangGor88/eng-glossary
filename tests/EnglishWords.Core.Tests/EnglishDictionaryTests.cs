using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Infrastructure.Dictionaries;
using EnglishWords.Infrastructure.Ollama;
using Xunit;

namespace EnglishWords.Core.Tests;

public sealed class EnglishDictionaryTests
{
    private const string BriefWikitext = """
        ==English==

        ===Pronunciation===
        * {{IPA|en|/bɹiːf/}}

        ===Adjective===
        {{en-adj|brief|er}}

        # {{lb|en|obsolete}} Rife; common.
        # Of short [[duration]]; happening quickly. {{defdate|from 15th c.}}
        #: {{syn|en|short}}
        #: {{ux|en|Her reign was '''brief''' but spectacular.}}
        # [[concise|Concise]]; taking few words.
        #: {{ux|en|His speech was '''brief''' but moving.}}

        ===Noun===
        # A summary.

        ==Scots==
        # Something else entirely.
        """;

    [Fact]
    public void Parser_CleansDefinitionAndPicksFirstSenseWithExample()
    {
        var result = EnglishWiktionaryDefinitionProvider.ParseEnglishDefinition(BriefWikitext);

        Assert.NotNull(result);
        Assert.Equal("adjective", result.PartOfSpeech);
        Assert.Equal("Of short duration; happening quickly.", result.Definition);
        Assert.Equal("Her reign was brief but spectacular.", result.Example);
        Assert.Equal("/bɹiːf/", result.Pronunciation);
    }

    [Fact]
    public void Parser_RejectsInflectedFormsAndEntriesWithoutExamples()
    {
        const string wikitext = """
            ==English==
            ===Verb===
            # {{past tense of|en|gather}}
            #: {{ux|en|She gathered the shawl about her.}}
            # To bring things together.
            """;

        Assert.Null(EnglishWiktionaryDefinitionProvider.ParseEnglishDefinition(wikitext));
    }

    [Fact]
    public async Task Generator_UsesDictionaryDataAndSkipsWordsWithoutEntries()
    {
        var generator = new EnglishDictionaryLessonGenerator(
            new FakeCandidates("brief", "zzzz", "gather"),
            new FakeDefinitions());

        var words = await generator.GenerateAsync(new DateOnly(2026, 1, 1), 2, []);

        Assert.Equal(["brief", "gather"], words.Select(word => word.Word));
        Assert.All(words, word => Assert.Equal(VocabularyLanguage.English, word.Language));
        Assert.Equal("def-brief", words[0].Definition);
        Assert.Equal("https://en.wiktionary.org/wiki/brief#English", words[0].DefinitionSource);
    }

    [Fact]
    public async Task Generator_FailsWhenTooFewWordsAreFound()
    {
        var generator = new EnglishDictionaryLessonGenerator(
            new FakeCandidates("zzzz", "yyyy"),
            new FakeDefinitions());

        await Assert.ThrowsAsync<OllamaGenerationException>(
            () => generator.GenerateAsync(new DateOnly(2026, 1, 1), 1, []));
    }

    private sealed class FakeCandidates(params string[] words) : ILessonGenerator
    {
        public Task<IReadOnlyList<GeneratedWord>> GenerateAsync(
            DateOnly date,
            int count,
            IReadOnlyCollection<string> avoidWords,
            CancellationToken cancellationToken = default,
            IProgress<string>? progress = null) =>
            Task.FromResult<IReadOnlyList<GeneratedWord>>(words
                .Where(word => !avoidWords.Contains(word, StringComparer.OrdinalIgnoreCase))
                .Select(word => new GeneratedWord(word, "noun", "model", "model example", word))
                .ToArray());
    }

    private sealed class FakeDefinitions : IEnglishDefinitionProvider
    {
        public Task<DictionaryDefinition?> GetDefinitionAsync(string word, CancellationToken cancellationToken = default) =>
            Task.FromResult<DictionaryDefinition?>(word.StartsWith('z') || word.StartsWith('y')
                ? null
                : new DictionaryDefinition(
                    "verb",
                    $"def-{word}",
                    $"https://en.wiktionary.org/wiki/{word}#English",
                    $"example-{word} sentence",
                    string.Empty));
    }
}
