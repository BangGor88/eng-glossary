using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Infrastructure.Ollama;

namespace EnglishWords.Infrastructure.Dictionaries;

// The model only proposes words; definitions, examples and pronunciation come from English Wiktionary.
public sealed class EnglishDictionaryLessonGenerator(
    ILessonGenerator candidateGenerator,
    IEnglishDefinitionProvider definitionProvider) : ILessonGenerator
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
        var triedWords = new HashSet<string>(avoidWords, StringComparer.OrdinalIgnoreCase);
        var knownDefinitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownExamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var attempt = 0; attempt < MaximumGenerationAttempts && results.Count < count; attempt++)
        {
            var candidates = await candidateGenerator.GenerateAsync(
                date,
                Math.Max(15, (count - results.Count) * 3),
                triedWords,
                cancellationToken,
                progress);

            foreach (var candidate in candidates)
            {
                var word = candidate.Word.Trim();
                if (string.IsNullOrWhiteSpace(word) || word.Contains(' ') || !triedWords.Add(word))
                {
                    continue;
                }

                progress?.Report($"Looking up '{word}' in Wiktionary...");
                var definition = await definitionProvider.GetDefinitionAsync(word, cancellationToken);
                if (definition is null ||
                    !knownDefinitions.Add(definition.Definition) ||
                    !knownExamples.Add(definition.Example))
                {
                    continue;
                }

                results.Add(new GeneratedWord(
                    word,
                    definition.PartOfSpeech,
                    definition.Definition,
                    definition.Example,
                    string.IsNullOrWhiteSpace(definition.Pronunciation) ? word : definition.Pronunciation,
                    VocabularyLanguage.English,
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
                $"Only {results.Count} of {count} words were found in English Wiktionary after {MaximumGenerationAttempts} attempts. Retry generation.");
        }

        return results;
    }
}
