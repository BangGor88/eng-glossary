using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Core.Services;
using Xunit;

namespace EnglishWords.Core.Tests;

public sealed class DailyLessonServiceTests
{
    [Fact]
    public async Task GetTodayAsync_GeneratesSixWordsAndReusesSavedLessonThatDay()
    {
        var repository = new MemoryRepository();
        var generator = new FakeGenerator(ValidWords());
        var service = new DailyLessonService(repository, generator, new FixedTimeProvider(Utc(2026, 10, 27)));

        var first = await service.GetTodayAsync();
        var second = await service.GetTodayAsync();

        Assert.Same(first, second);
        Assert.Equal(new DateOnly(2026, 10, 27), first.Date);
        Assert.Equal(6, first.Words.Count);
        Assert.Equal(1, generator.CallCount);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task GetTodayAsync_GeneratesAgainOnNextLocalDate()
    {
        var repository = new MemoryRepository();
        var generator = new FakeGenerator(ValidWords());
        var clock = new FixedTimeProvider(Utc(2026, 10, 27));
        var service = new DailyLessonService(repository, generator, clock);

        var first = await service.GetTodayAsync();
        clock.SetUtcNow(Utc(2026, 10, 28));
        var second = await service.GetTodayAsync();

        Assert.NotEqual(first.Date, second.Date);
        Assert.Equal(6, second.Words.Count);
        Assert.Equal(2, second.Words.Count(word => word.IsReviewWord));
        Assert.Equal(4, generator.RequestedCounts[1]);
        Assert.Equal(2, generator.CallCount);
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task GetTodayAsync_DoesNotSaveInvalidGeneration()
    {
        var repository = new MemoryRepository();
        var generator = new FakeGenerator(ValidWords().Take(5).ToArray());
        var service = new DailyLessonService(repository, generator, new FixedTimeProvider(Utc(2026, 10, 27)));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetTodayAsync());

        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task GenerateNewSetAsync_AfterPassedQuiz_SavesNextNumberedSetWithoutReusingTodayWords()
    {
        var repository = new MemoryRepository();
        var generator = new FakeGenerator(ValidWords(), NewSetWords());
        var service = new DailyLessonService(repository, generator, new FixedTimeProvider(Utc(2026, 10, 27)));
        var first = await service.GetTodayAsync();

        var second = await service.GenerateNewSetAsync(first with { QuizPassed = true });

        Assert.Equal(2, second.SetNumber);
        Assert.Equal(6, second.Words.Count);
        Assert.Equal("bright", second.Words[0].Word);
        Assert.Equal(2, repository.LessonCount);
        Assert.Equal(6, generator.RequestedCounts[1]);
        Assert.Equal(6, generator.AvoidedWords[1].Count);
    }

    private static IReadOnlyList<GeneratedWord> ValidWords() =>
    [
        new("adapt", "verb", "to change to fit a new situation", "She adapted quickly to the new team.", "uh-DAPT"),
        new("brief", "adjective", "short in time or length", "We had a brief meeting.", "BREEF"),
        new("curious", "adjective", "eager to learn or know", "He was curious about the result.", "KYOOR-ee-us"),
        new("gather", "verb", "to bring things together", "Please gather the papers.", "GATH-er"),
        new("steady", "adjective", "firm and not changing suddenly", "Keep a steady pace.", "STED-ee"),
        new("notice", "verb", "to see or become aware of something", "I noticed a small bird.", "NOH-tiss")
    ];

    private static IReadOnlyList<GeneratedWord> NewSetWords() =>
    [
        new("bright", "adjective", "full of light", "The bright lamp lit the room.", "BRYTE"),
        new("calm", "adjective", "peaceful and not worried", "She felt calm before the show.", "KAHM"),
        new("discover", "verb", "find something for the first time", "They discovered a new trail.", "dis-KUV-er"),
        new("gentle", "adjective", "kind and careful", "Use a gentle touch.", "JEN-tul"),
        new("invite", "verb", "ask someone to come", "We invited our neighbors.", "in-VYTE"),
        new("journey", "noun", "a trip from one place to another", "The journey took an hour.", "JUR-nee")
    ];

    private static DateTimeOffset Utc(int year, int month, int day) =>
        new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class FakeGenerator(IReadOnlyList<GeneratedWord> words, IReadOnlyList<GeneratedWord>? newSetWords = null) : ILessonGenerator
    {
        public int CallCount { get; private set; }
        public List<int> RequestedCounts { get; } = [];
        public List<IReadOnlyCollection<string>> AvoidedWords { get; } = [];

        public Task<IReadOnlyList<GeneratedWord>> GenerateAsync(
            DateOnly date,
            int count,
            IReadOnlyCollection<string> avoidWords,
            CancellationToken cancellationToken = default,
            IProgress<string>? progress = null)
        {
            CallCount++;
            RequestedCounts.Add(count);
            AvoidedWords.Add(avoidWords);
            var source = avoidWords.Count >= DailyLessonService.DailyWordCount ? newSetWords ?? words : words;
            var generated = source
                .Where(word => !avoidWords.Contains(word.Word, StringComparer.OrdinalIgnoreCase))
                .Take(count)
                .ToArray();
            return Task.FromResult<IReadOnlyList<GeneratedWord>>(generated);
        }
    }

    private sealed class MemoryRepository : IVocabularyRepository
    {
        private readonly Dictionary<(DateOnly Date, int SetNumber), DailyLesson> _lessons = [];

        public int SaveCount { get; private set; }
        public int LessonCount => _lessons.Count;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DailyLesson?> GetLessonAsync(DateOnly date, int setNumber, CancellationToken cancellationToken = default)
        {
            _lessons.TryGetValue((date, setNumber), out var lesson);
            return Task.FromResult(lesson);
        }

        public Task<DailyLesson?> GetLatestLessonAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            var lesson = _lessons.Values.Where(item => item.Date == date).OrderByDescending(item => item.SetNumber).FirstOrDefault();
            return Task.FromResult(lesson);
        }

        public Task SaveLessonAsync(DailyLesson lesson, CancellationToken cancellationToken = default)
        {
            _lessons.Add((lesson.Date, lesson.SetNumber), lesson);
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task AddLessonWordsAsync(DateOnly date, int setNumber, IReadOnlyList<VocabularyWord> words, CancellationToken cancellationToken = default)
        {
            var lesson = _lessons[(date, setNumber)];
            _lessons[(date, setNumber)] = lesson with { Words = lesson.Words.Concat(words).ToArray() };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VocabularyWord>> GetWordsForDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VocabularyWord>>(_lessons.Values.Where(item => item.Date == date).SelectMany(item => item.Words).ToArray());

        public Task<IReadOnlyList<VocabularyWord>> GetReviewWordsAsync(DateOnly beforeDate, int count, CancellationToken cancellationToken = default)
        {
            var previous = _lessons.Values.Where(lesson => lesson.Date < beforeDate)
                .OrderByDescending(lesson => lesson.Date).ThenByDescending(lesson => lesson.SetNumber).FirstOrDefault();
            var words = previous?.Words
                .OrderBy(word => word.IsMastered)
                .ThenBy(word => word.CorrectStreak)
                .Take(count)
                .ToArray() ?? [];
            return Task.FromResult<IReadOnlyList<VocabularyWord>>(words);
        }

        public Task<IReadOnlyList<ScorecardWord>> GetScorecardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScorecardWord>>(Array.Empty<ScorecardWord>());

        public Task MarkWordReadAsync(DateOnly date, int setNumber, Guid wordId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StartQuizAttemptAsync(DateOnly date, int setNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveQuizAnswerAsync(DateOnly date, int setNumber, Guid wordId, int correctStreak, bool isMastered, int timesCorrect, int score, bool isComplete, bool isPassed, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}