using EnglishWords.Core.Models;

namespace EnglishWords.Core.Abstractions;

public interface ILessonGenerator
{
    Task<IReadOnlyList<GeneratedWord>> GenerateAsync(
        DateOnly date,
        int count,
        IReadOnlyCollection<string> avoidWords,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null);
}