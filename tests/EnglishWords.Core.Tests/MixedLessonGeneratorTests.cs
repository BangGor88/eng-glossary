using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Infrastructure.Ollama;
using Xunit;

namespace EnglishWords.Core.Tests;

public sealed class MixedLessonGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_InterleavesEnglishAndSwedishWords()
    {
        var english = new FakeLanguageGenerator(VocabularyLanguage.English);
        var swedish = new FakeLanguageGenerator(VocabularyLanguage.Swedish);
        var generator = new MixedLessonGenerator(english, swedish);

        var words = await generator.GenerateAsync(new DateOnly(2026, 10, 28), 6, []);

        Assert.Equal(
            [
                VocabularyLanguage.English,
                VocabularyLanguage.Swedish,
                VocabularyLanguage.English,
                VocabularyLanguage.Swedish,
                VocabularyLanguage.English,
                VocabularyLanguage.Swedish
            ],
            words.Select(word => word.Language));
        Assert.Equal("English-1", words[0].Word);
        Assert.Equal("Swedish-1", words[1].Word);
        Assert.Contains("English-1", swedish.LastAvoidWords);
    }

    private sealed class FakeLanguageGenerator(VocabularyLanguage language) : ILessonGenerator
    {
        public IReadOnlyCollection<string> LastAvoidWords { get; private set; } = [];

        public Task<IReadOnlyList<GeneratedWord>> GenerateAsync(
            DateOnly date,
            int count,
            IReadOnlyCollection<string> avoidWords,
            CancellationToken cancellationToken = default,
            IProgress<string>? progress = null)
        {
            LastAvoidWords = avoidWords;
            IReadOnlyList<GeneratedWord> words = Enumerable.Range(1, count)
                .Select(index => new GeneratedWord(
                    $"{language}-{index}",
                    language.ToString(),
                    $"{language} meaning {index}",
                    $"{language} example {index}",
                    $"{language} pronunciation {index}",
                    language))
                .ToArray();
            return Task.FromResult(words);
        }
    }
}
