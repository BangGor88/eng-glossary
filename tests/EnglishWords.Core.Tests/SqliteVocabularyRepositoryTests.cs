using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using EnglishWords.Core.Services;
using EnglishWords.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EnglishWords.Core.Tests;

public sealed class SqliteVocabularyRepositoryTests
{
    [Fact]
    public async Task NewDatabase_PersistsCardsReadQuizScoreAndMastery()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var repository = new SqliteVocabularyRepository(path);
            await repository.InitializeAsync();
            var lesson = BuildLesson(new DateOnly(2026, 10, 27));
            await repository.SaveLessonAsync(lesson);
            await repository.StartQuizAttemptAsync(lesson.Date, 1);
            await repository.MarkWordReadAsync(lesson.Date, 1, lesson.Words[0].Id);
            await repository.SaveQuizAnswerAsync(lesson.Date, 1, lesson.Words[0].Id, 1, false, 1, 3, true, true);

            var saved = await repository.GetLessonAsync(lesson.Date, 1);

            Assert.NotNull(saved);
            Assert.Equal(6, saved.Words.Count);
            Assert.True(saved.Words[0].IsRead);
            Assert.Equal(1, saved.Words[0].CorrectStreak);
            Assert.Equal(1, saved.Words[0].TimesCorrect);
            Assert.Equal(3, saved.QuizScore);
            Assert.Equal(3, saved.BestScore);
            Assert.Equal(1, saved.QuizAttempts);
            Assert.True(saved.QuizComplete);
            Assert.True(saved.QuizPassed);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task Scorecard_AggregatesRepeatedWordsAcrossSameDaySets()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var repository = new SqliteVocabularyRepository(path);
            await repository.InitializeAsync();
            var date = new DateOnly(2026, 10, 27);
            var first = BuildLesson(date) with
            {
                Words = BuildLesson(date).Words.Select((word, index) => index == 0 ? word with { TimesCorrect = 2 } : word).ToArray()
            };
            await repository.SaveLessonAsync(first);
            var repeated = first.Words[0] with { Id = Guid.NewGuid(), Order = 1, IsReviewWord = true, TimesCorrect = 0 };
            var secondWords = new[] { repeated }.Concat(first.Words.Skip(1).Select((word, index) => word with { Id = Guid.NewGuid(), Order = index + 2 })).ToArray();
            await repository.SaveLessonAsync(new DailyLesson(date, secondWords, SetNumber: 2));

            var scorecard = await repository.GetScorecardAsync();
            var adapt = Assert.Single(scorecard, word => word.Word == "adapt");

            Assert.Equal(2, adapt.TimesCorrect);
            Assert.Equal(2, (await repository.GetLatestLessonAsync(date))?.SetNumber);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task GetReviewWords_ReturnsCorrectCountsForCarryoverWords()
    {
        var path = TemporaryDatabasePath();
        try
        {
            var repository = new SqliteVocabularyRepository(path);
            await repository.InitializeAsync();
            var previousDate = new DateOnly(2026, 10, 26);
            var lesson = BuildLesson(previousDate) with
            {
                Words = BuildLesson(previousDate).Words
                    .Select((word, index) => index == 0 ? word with { TimesCorrect = 4 } : word)
                    .ToArray()
            };
            await repository.SaveLessonAsync(lesson);

            var carryovers = await repository.GetReviewWordsAsync(previousDate.AddDays(1), 2);

            Assert.Equal(2, carryovers.Count);
            Assert.Equal(4, carryovers[0].TimesCorrect);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task Initialize_UpgradesLegacyFiveWordLessonAndAddsSixthWord()
    {
        var path = TemporaryDatabasePath();
        var date = new DateOnly(2026, 10, 27);
        try
        {
            await CreateLegacyDatabaseAsync(path, date);
            var repository = new SqliteVocabularyRepository(path);
            await repository.InitializeAsync();
            var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
            var service = new DailyLessonService(repository, new OneWordGenerator(), clock);

            var lesson = await service.GetTodayAsync();

            Assert.Equal(6, lesson.Words.Count);
            Assert.Equal("newword", lesson.Words[^1].Word);
            Assert.Equal(5, lesson.Words.Count(word => word.Word != "newword"));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static async Task CreateLegacyDatabaseAsync(string path, DateOnly date)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE Lessons (Date TEXT PRIMARY KEY);
                CREATE TABLE Words (
                    Id TEXT PRIMARY KEY,
                    LessonDate TEXT NOT NULL REFERENCES Lessons(Date) ON DELETE CASCADE,
                    Ordinal INTEGER NOT NULL,
                    Word TEXT NOT NULL,
                    PartOfSpeech TEXT NOT NULL,
                    Definition TEXT NOT NULL,
                    Example TEXT NOT NULL,
                    Pronunciation TEXT NOT NULL,
                    Repetitions INTEGER NOT NULL,
                    EaseFactor REAL NOT NULL,
                    IntervalDays INTEGER NOT NULL,
                    DueAt TEXT NOT NULL,
                    UNIQUE(LessonDate, Ordinal)
                );
                INSERT INTO Lessons (Date) VALUES ($date);
                """;
            command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            await command.ExecuteNonQueryAsync();
        }

        var lesson = BuildLesson(date);
        foreach (var word in lesson.Words.Take(5))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Words (
                    Id, LessonDate, Ordinal, Word, PartOfSpeech, Definition, Example, Pronunciation,
                    Repetitions, EaseFactor, IntervalDays, DueAt)
                VALUES ($id, $date, $ordinal, $word, $partOfSpeech, $definition, $example, $pronunciation, 0, 2.5, 0, '');
                """;
            command.Parameters.AddWithValue("$id", word.Id.ToString("D"));
            command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$ordinal", word.Order);
            command.Parameters.AddWithValue("$word", word.Word);
            command.Parameters.AddWithValue("$partOfSpeech", word.PartOfSpeech);
            command.Parameters.AddWithValue("$definition", word.Definition);
            command.Parameters.AddWithValue("$example", word.Example);
            command.Parameters.AddWithValue("$pronunciation", word.Pronunciation);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static DailyLesson BuildLesson(DateOnly date)
    {
        var entries = new[]
        {
            new GeneratedWord("adapt", "verb", "change to fit a new situation", "She adapted to the new team.", "uh-DAPT"),
            new GeneratedWord("brief", "adjective", "short in time or length", "We had a brief meeting.", "BREEF"),
            new GeneratedWord("curious", "adjective", "eager to learn or know", "He was curious about it.", "KYOOR-ee-us"),
            new GeneratedWord("gather", "verb", "bring things together", "Gather the papers, please.", "GATH-er"),
            new GeneratedWord("steady", "adjective", "firm and not changing suddenly", "Keep a steady pace.", "STED-ee"),
            new GeneratedWord("notice", "verb", "see or become aware of something", "I noticed a small bird.", "NOH-tiss")
        };
        var words = entries.Select((entry, index) => new VocabularyWord(
            Guid.NewGuid(), index + 1, entry.Word, entry.PartOfSpeech, entry.Definition, entry.Example, entry.Pronunciation,
            false, false, 0, false)).ToArray();
        return new DailyLesson(date, words);
    }

    private static string TemporaryDatabasePath() => Path.Combine(Path.GetTempPath(), $"english-words-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class OneWordGenerator : ILessonGenerator
    {
        public Task<IReadOnlyList<GeneratedWord>> GenerateAsync(DateOnly date, int count, IReadOnlyCollection<string> avoidWords, CancellationToken cancellationToken = default, IProgress<string>? progress = null) =>
            Task.FromResult<IReadOnlyList<GeneratedWord>>(
            [new GeneratedWord("newword", "noun", "a newly introduced word", "The newword was fun to learn.", "NOO-word")]);
    }
}