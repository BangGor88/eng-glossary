using EnglishWords.Core.Models;

namespace EnglishWords.Core.Abstractions;

public interface IVocabularyRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<DailyLesson?> GetLessonAsync(DateOnly date, int setNumber, CancellationToken cancellationToken = default);
    Task<DailyLesson?> GetLatestLessonAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task SaveLessonAsync(DailyLesson lesson, CancellationToken cancellationToken = default);
    Task AddLessonWordsAsync(DateOnly date, int setNumber, IReadOnlyList<VocabularyWord> words, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VocabularyWord>> GetWordsForDateAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VocabularyWord>> GetReviewWordsAsync(DateOnly beforeDate, int count, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScorecardWord>> GetScorecardAsync(CancellationToken cancellationToken = default);
    Task MarkWordReadAsync(DateOnly date, int setNumber, Guid wordId, CancellationToken cancellationToken = default);
    Task StartQuizAttemptAsync(DateOnly date, int setNumber, CancellationToken cancellationToken = default);
    Task SaveQuizAnswerAsync(
        DateOnly date,
        int setNumber,
        Guid wordId,
        int correctStreak,
        bool isMastered,
        int timesCorrect,
        int score,
        bool isComplete,
        bool isPassed,
        CancellationToken cancellationToken = default);
}