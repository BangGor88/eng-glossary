using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;

namespace EnglishWords.Infrastructure.Ollama;

public sealed class MixedLessonGenerator(
    ILessonGenerator englishGenerator,
    ILessonGenerator swedishGenerator) : ILessonGenerator
{
    public async Task<IReadOnlyList<GeneratedWord>> GenerateAsync(
        DateOnly date,
        int count,
        IReadOnlyCollection<string> avoidWords,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var englishCount = count / 2;
        var swedishCount = count - englishCount;
        var englishWords = englishCount == 0
            ? []
            : await englishGenerator.GenerateAsync(date, englishCount, avoidWords, cancellationToken, progress);
        var allAvoidWords = avoidWords.Concat(englishWords.Select(word => word.Word)).ToArray();
        var swedishWords = swedishCount == 0
            ? []
            : await swedishGenerator.GenerateAsync(date, swedishCount, allAvoidWords, cancellationToken, progress);

        var mixedWords = new List<GeneratedWord>(count);
        for (var index = 0; index < Math.Max(englishWords.Count, swedishWords.Count); index++)
        {
            if (index < englishWords.Count)
            {
                mixedWords.Add(englishWords[index] with { Language = VocabularyLanguage.English });
            }

            if (index < swedishWords.Count)
            {
                mixedWords.Add(swedishWords[index] with { Language = VocabularyLanguage.Swedish });
            }
        }

        return mixedWords;
    }
}
