using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Core.Services;
using EnglishWords.Infrastructure.Dictionaries;
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
    public async Task GetTodayAsync_RefreshesLegacySwedishDefinitionsFromDictionary()
    {
        var date = new DateOnly(2026, 10, 27);
        var repository = new MemoryRepository();
        var words = ValidWords().Select((word, index) => new VocabularyWord(
            Guid.NewGuid(),
            index + 1,
            word.Word,
            word.PartOfSpeech,
            "English model definition",
            word.Example,
            word.Pronunciation,
            false,
            false,
            0,
            false,
            0,
            VocabularyLanguage.Swedish)).ToArray();
        await repository.SaveLessonAsync(new DailyLesson(date, words));
        var service = new DailyLessonService(
            repository,
            new FakeGenerator(ValidWords()),
            new FixedTimeProvider(Utc(2026, 10, 27)),
            new FakeSwedishDefinitionProvider());

        var lesson = await service.GetTodayAsync();

        Assert.All(lesson.Words, word =>
        {
            Assert.Equal("substantiv", word.PartOfSpeech);
            Assert.StartsWith("svensk ordförklaring för ", word.Definition);
            Assert.StartsWith("https://sv.wiktionary.org/wiki/", word.DefinitionSource);
        });
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

    [Fact]
    public async Task GenerateNewSetAsync_UsesCurrentSwedishGeneratorAndDictionaryDefinitions()
    {
        var repository = new MemoryRepository();
        var modelGenerator = new FakeGenerator(ValidWords(), NewSetWords());
        var swedishGenerator = new SwedishDictionaryLessonGenerator(
            modelGenerator,
            new IdentityTranslations(),
            new FakeSwedishDefinitionProvider());
        var service = new DailyLessonService(
            repository,
            swedishGenerator,
            new FixedTimeProvider(Utc(2026, 10, 27)));
        var first = await service.GetTodayAsync();

        var second = await service.GenerateNewSetAsync(first with { QuizPassed = true });

        Assert.All(second.Words, word =>
        {
            Assert.Equal(VocabularyLanguage.Swedish, word.Language);
            Assert.StartsWith("svensk ordförklaring för ", word.Definition);
            Assert.StartsWith("https://sv.wiktionary.org/wiki/", word.DefinitionSource);
        });
        Assert.Equal(2, second.SetNumber);
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

    private sealed class IdentityTranslations : ISwedishTranslationProvider
    {
        public Task<string?> GetSwedishTranslationAsync(string englishWord, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("sv" + englishWord);
    }

    private sealed class FakeSwedishDefinitionProvider : ISwedishDefinitionProvider
    {
        public Task<IReadOnlyDictionary<string, DictionaryDefinition>> GetDefinitionsAsync(
            IReadOnlyCollection<string> words,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DictionaryDefinition>>(
                words.ToDictionary(
                    word => word,
                    word => new DictionaryDefinition(
                        "substantiv",
                        $"svensk ordförklaring för {word}",
                        $"https://sv.wiktionary.org/wiki/{Uri.EscapeDataString(word)}"),
                    StringComparer.OrdinalIgnoreCase));

        public Task<DictionaryDefinition?> GetDefinitionAsync(string word, CancellationToken cancellationToken = default) =>
            Task.FromResult<DictionaryDefinition?>(new DictionaryDefinition(
                "substantiv",
                $"svensk ordförklaring för {word}",
                $"https://sv.wiktionary.org/wiki/{Uri.EscapeDataString(word)}",
                $"Exempel med {word}.",
                $"uttal {word}"));
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

        public Task UpdateWordDefinitionAsync(
            DateOnly date,
            int setNumber,
            Guid wordId,
            string partOfSpeech,
            string definition,
            string definitionSource,
            CancellationToken cancellationToken = default)
        {
            var lesson = _lessons[(date, setNumber)];
            var words = lesson.Words.Select(word => word.Id == wordId
                ? word with { PartOfSpeech = partOfSpeech, Definition = definition, DefinitionSource = definitionSource }
                : word).ToArray();
            _lessons[(date, setNumber)] = lesson with { Words = words };
            return Task.CompletedTask;
        }

        public Task StartQuizAttemptAsync(DateOnly date, int setNumber, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveQuizAnswerAsync(DateOnly date, int setNumber, Guid wordId, int correctStreak, bool isMastered, int timesCorrect, int score, bool isComplete, bool isPassed, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}