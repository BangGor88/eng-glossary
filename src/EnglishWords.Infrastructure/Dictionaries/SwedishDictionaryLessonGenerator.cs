using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Infrastructure.Ollama;

namespace EnglishWords.Infrastructure.Dictionaries;

// Picks English words with the model, then turns each into a Swedish card using Wiktionary only.
public sealed class SwedishDictionaryLessonGenerator(
    ILessonGenerator englishCandidateGenerator,
    ISwedishTranslationProvider translationProvider,
    ISwedishDefinitionProvider definitionProvider) : ILessonGenerator
{
    private const int MaximumGenerationAttempts = 6;

    public async Task<IReadOnlyList<GeneratedWord>> GenerateAsync(
        DateOnly date,
        int count,
        IReadOnlyCollection<string> avoidWords,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var results = new List<GeneratedWord>(count);
        var usedSwedishWords = new HashSet<string>(avoidWords, StringComparer.OrdinalIgnoreCase);
        var triedEnglishWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownExamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var attempt = 0; attempt < MaximumGenerationAttempts && results.Count < count; attempt++)
        {
            var remainingCount = count - results.Count;
            var candidates = await englishCandidateGenerator.GenerateAsync(
                date,
                Math.Max(15, remainingCount * 3),
                triedEnglishWords,
                cancellationToken,
                progress);

            foreach (var candidate in candidates)
            {
                var englishWord = candidate.Word.Trim();
                if (string.IsNullOrWhiteSpace(englishWord) || !triedEnglishWords.Add(englishWord))
                {
                    continue;
                }

                progress?.Report($"Translating '{englishWord}' to Swedish with Wiktionary...");
                var swedishWord = await translationProvider.GetSwedishTranslationAsync(englishWord, cancellationToken);
                if (string.IsNullOrWhiteSpace(swedishWord) || !usedSwedishWords.Add(swedishWord))
                {
                    continue;
                }

                var definition = await definitionProvider.GetDefinitionAsync(swedishWord, cancellationToken);
                if (definition is null ||
                    string.IsNullOrWhiteSpace(definition.Example) ||
                    !knownDefinitions.Add(definition.Definition.Trim()) ||
                    !knownExamples.Add(definition.Example.Trim()))
                {
                    continue;
                }

                results.Add(new GeneratedWord(
                    swedishWord,
                    definition.PartOfSpeech,
                    definition.Definition,
                    definition.Example,
                    string.IsNullOrWhiteSpace(definition.Pronunciation) ? swedishWord : definition.Pronunciation,
                    VocabularyLanguage.Swedish,
                    definition.SourceUrl));
                if (results.Count == count)
                {
                    break;
                }
            }
        }

        if (results.Count != count)
        {
            throw new OllamaGenerationException(
                $"Only {results.Count} of {count} words could be translated and found in Swedish Wiktionary after {MaximumGenerationAttempts} attempts. Retry generation.");
        }

        return results;
    }
}
