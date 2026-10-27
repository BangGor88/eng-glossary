using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;
using Microsoft.Data.Sqlite;

namespace EnglishWords.Infrastructure.Persistence;

public sealed class SqliteVocabularyRepository(string databasePath) : IVocabularyRepository
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS Lessons (
                Date TEXT PRIMARY KEY,
                LessonDate TEXT NOT NULL DEFAULT '',
                SetNumber INTEGER NOT NULL DEFAULT 1,
                QuizScore INTEGER NOT NULL DEFAULT 0,
                BestScore INTEGER NOT NULL DEFAULT 0,
                QuizAttempts INTEGER NOT NULL DEFAULT 0,
                QuizComplete INTEGER NOT NULL DEFAULT 0,
                QuizPassed INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS Words (
                Id TEXT PRIMARY KEY,
                LessonDate TEXT NOT NULL REFERENCES Lessons(Date) ON DELETE CASCADE,
                Ordinal INTEGER NOT NULL,
                Word TEXT NOT NULL,
                PartOfSpeech TEXT NOT NULL,
                Definition TEXT NOT NULL,
                Example TEXT NOT NULL,
                Pronunciation TEXT NOT NULL,
                Repetitions INTEGER NOT NULL DEFAULT 0,
                EaseFactor REAL NOT NULL DEFAULT 2.5,
                IntervalDays INTEGER NOT NULL DEFAULT 0,
                DueAt TEXT NOT NULL DEFAULT '',
                IsReviewWord INTEGER NOT NULL DEFAULT 0,
                IsRead INTEGER NOT NULL DEFAULT 0,
                CorrectStreak INTEGER NOT NULL DEFAULT 0,
                IsMastered INTEGER NOT NULL DEFAULT 0,
                TimesCorrect INTEGER NOT NULL DEFAULT 0,
                UNIQUE(LessonDate, Ordinal)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureColumnAsync(connection, "Lessons", "QuizScore", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Lessons", "BestScore", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Lessons", "QuizAttempts", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Lessons", "QuizComplete", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Lessons", "QuizPassed", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Lessons", "LessonDate", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync(connection, "Lessons", "SetNumber", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync(connection, "Words", "IsReviewWord", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Words", "IsRead", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Words", "CorrectStreak", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Words", "IsMastered", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "Words", "TimesCorrect", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await using var backfillCommand = connection.CreateCommand();
        backfillCommand.CommandText = "UPDATE Lessons SET LessonDate = substr(Date, 1, 10) WHERE LessonDate = '';";
        await backfillCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DailyLesson?> GetLessonAsync(DateOnly date, int setNumber, CancellationToken cancellationToken = default)
    {
        var lessonKey = GetLessonKey(date, setNumber);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT QuizScore, BestScore, QuizAttempts, QuizComplete, QuizPassed
            FROM Lessons
            WHERE Date = $date;
            """;
        command.Parameters.AddWithValue("$date", lessonKey);

        int quizScore;
        int bestScore;
        int quizAttempts;
        bool quizComplete;
        bool quizPassed;
        await using (var lessonReader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await lessonReader.ReadAsync(cancellationToken))
            {
                return null;
            }

            quizScore = lessonReader.GetInt32(0);
            bestScore = lessonReader.GetInt32(1);
            quizAttempts = lessonReader.GetInt32(2);
            quizComplete = lessonReader.GetInt32(3) != 0;
            quizPassed = lessonReader.GetInt32(4) != 0;
        }

        command.Parameters.Clear();
        command.CommandText = """
                 SELECT Id, Ordinal, Word, PartOfSpeech, Definition, Example, Pronunciation,
                     IsReviewWord, IsRead, CorrectStreak, IsMastered, TimesCorrect
            FROM Words
            WHERE LessonDate = $date
            ORDER BY Ordinal;
            """;
        command.Parameters.AddWithValue("$date", lessonKey);

        var words = new List<VocabularyWord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            words.Add(ReadWord(reader));
        }

        return new DailyLesson(date, words, quizScore, bestScore, quizAttempts, quizComplete, quizPassed, setNumber);
    }

    public async Task<DailyLesson?> GetLatestLessonAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SetNumber FROM Lessons WHERE LessonDate = $date ORDER BY SetNumber DESC LIMIT 1;";
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull
            ? null
            : await GetLessonAsync(date, Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
    }

    public async Task SaveLessonAsync(DailyLesson lesson, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lessonCommand = connection.CreateCommand())
        {
            lessonCommand.Transaction = (SqliteTransaction)transaction;
            lessonCommand.CommandText = """
                INSERT INTO Lessons (Date, LessonDate, SetNumber, QuizScore, BestScore, QuizAttempts, QuizComplete, QuizPassed)
                VALUES ($date, $lessonDate, $setNumber, $score, $bestScore, $attempts, $complete, $passed);
                """;
            lessonCommand.Parameters.AddWithValue("$date", GetLessonKey(lesson.Date, lesson.SetNumber));
            lessonCommand.Parameters.AddWithValue("$lessonDate", lesson.Date.ToString("yyyy-MM-dd"));
            lessonCommand.Parameters.AddWithValue("$setNumber", lesson.SetNumber);
            lessonCommand.Parameters.AddWithValue("$score", lesson.QuizScore);
            lessonCommand.Parameters.AddWithValue("$bestScore", lesson.BestScore);
            lessonCommand.Parameters.AddWithValue("$attempts", lesson.QuizAttempts);
            lessonCommand.Parameters.AddWithValue("$complete", lesson.QuizComplete ? 1 : 0);
            lessonCommand.Parameters.AddWithValue("$passed", lesson.QuizPassed ? 1 : 0);
            await lessonCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertWordsAsync(connection, (SqliteTransaction)transaction, lesson.Date, lesson.SetNumber, lesson.Words, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddLessonWordsAsync(DateOnly date, int setNumber, IReadOnlyList<VocabularyWord> words, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertWordsAsync(connection, (SqliteTransaction)transaction, date, setNumber, words, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VocabularyWord>> GetWordsForDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.Id, w.Ordinal, w.Word, w.PartOfSpeech, w.Definition, w.Example, w.Pronunciation,
                   w.IsReviewWord, w.IsRead, w.CorrectStreak, w.IsMastered, w.TimesCorrect
            FROM Words w
            INNER JOIN Lessons l ON l.Date = w.LessonDate
            WHERE l.LessonDate = $date
            ORDER BY l.SetNumber, w.Ordinal;
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        var words = new List<VocabularyWord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) words.Add(ReadWord(reader));
        return words;
    }

    public async Task<IReadOnlyList<VocabularyWord>> GetReviewWordsAsync(DateOnly beforeDate, int count, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Ordinal, Word, PartOfSpeech, Definition, Example, Pronunciation,
                   IsReviewWord, IsRead, CorrectStreak, IsMastered
            FROM Words
            WHERE LessonDate = (
                SELECT Date FROM Lessons WHERE LessonDate < $date
                ORDER BY LessonDate DESC, SetNumber DESC LIMIT 1
            )
            ORDER BY IsMastered, CorrectStreak, Ordinal
            LIMIT $count;
            """;
        command.Parameters.AddWithValue("$date", beforeDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$count", count);

        var words = new List<VocabularyWord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            words.Add(ReadWord(reader));
        }

        return words;
    }

    public async Task<IReadOnlyList<ScorecardWord>> GetScorecardAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.Word, w.PartOfSpeech, w.Definition, w.TimesCorrect, w.CorrectStreak, w.IsMastered, l.LessonDate
            FROM Words w
            INNER JOIN Lessons l ON l.Date = w.LessonDate
            ORDER BY l.LessonDate, l.SetNumber, w.Ordinal;
            """;
        var attempts = new List<(string Word, string PartOfSpeech, string Definition, int TimesCorrect, int CorrectStreak, bool IsMastered, DateOnly Date)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                attempts.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5) != 0,
                    DateOnly.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        return attempts
            .GroupBy(item => item.Word, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var latest = group.Last();
                return new ScorecardWord(
                    latest.Word,
                    latest.PartOfSpeech,
                    latest.Definition,
                    group.Sum(item => item.TimesCorrect),
                    group.Max(item => item.CorrectStreak),
                    group.Any(item => item.IsMastered),
                    latest.Date);
            })
            .OrderByDescending(item => item.IsMastered)
            .ThenByDescending(item => item.TimesCorrect)
            .ThenBy(item => item.Word)
            .ToArray();
    }

    public async Task MarkWordReadAsync(DateOnly date, int setNumber, Guid wordId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Words SET IsRead = 1 WHERE Id = $id AND LessonDate = $date;";
        command.Parameters.AddWithValue("$id", wordId.ToString("D"));
        command.Parameters.AddWithValue("$date", GetLessonKey(date, setNumber));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task StartQuizAttemptAsync(DateOnly date, int setNumber, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Lessons
            SET QuizScore = 0, QuizAttempts = QuizAttempts + 1, QuizComplete = 0, QuizPassed = 0
            WHERE Date = $date;
            """;
        command.Parameters.AddWithValue("$date", GetLessonKey(date, setNumber));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveQuizAnswerAsync(
        DateOnly date,
        int setNumber,
        Guid wordId,
        int correctStreak,
        bool isMastered,
        int timesCorrect,
        int score,
        bool isComplete,
        bool isPassed,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var wordCommand = connection.CreateCommand())
        {
            wordCommand.Transaction = (SqliteTransaction)transaction;
            wordCommand.CommandText = """
                UPDATE Words SET CorrectStreak = $streak, IsMastered = $mastered, TimesCorrect = $timesCorrect
                WHERE Id = $id AND LessonDate = $date;
                """;
            wordCommand.Parameters.AddWithValue("$streak", correctStreak);
            wordCommand.Parameters.AddWithValue("$mastered", isMastered ? 1 : 0);
            wordCommand.Parameters.AddWithValue("$timesCorrect", timesCorrect);
            wordCommand.Parameters.AddWithValue("$id", wordId.ToString("D"));
            wordCommand.Parameters.AddWithValue("$date", GetLessonKey(date, setNumber));
            await wordCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var lessonCommand = connection.CreateCommand())
        {
            lessonCommand.Transaction = (SqliteTransaction)transaction;
            lessonCommand.CommandText = """
                UPDATE Lessons
                SET QuizScore = $score, BestScore = MAX(BestScore, $score),
                    QuizComplete = $complete, QuizPassed = $passed
                WHERE Date = $date;
                """;
            lessonCommand.Parameters.AddWithValue("$score", score);
            lessonCommand.Parameters.AddWithValue("$complete", isComplete ? 1 : 0);
            lessonCommand.Parameters.AddWithValue("$passed", isPassed ? 1 : 0);
            lessonCommand.Parameters.AddWithValue("$date", GetLessonKey(date, setNumber));
            await lessonCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static VocabularyWord ReadWord(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetInt32(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetInt32(7) != 0,
        reader.GetInt32(8) != 0,
        reader.GetInt32(9),
        reader.GetInt32(10) != 0,
        reader.GetInt32(11));

    private static async Task InsertWordsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly date,
        int setNumber,
        IReadOnlyList<VocabularyWord> words,
        CancellationToken cancellationToken)
    {
        foreach (var word in words)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Words (
                    Id, LessonDate, Ordinal, Word, PartOfSpeech, Definition, Example, Pronunciation,
                    Repetitions, EaseFactor, IntervalDays, DueAt, IsReviewWord, IsRead, CorrectStreak, IsMastered, TimesCorrect)
                VALUES (
                    $id, $date, $ordinal, $word, $partOfSpeech, $definition, $example, $pronunciation,
                    0, 2.5, 0, '', $isReviewWord, $isRead, $correctStreak, $isMastered, $timesCorrect);
                """;
            command.Parameters.AddWithValue("$id", word.Id.ToString("D"));
            command.Parameters.AddWithValue("$date", GetLessonKey(date, setNumber));
            command.Parameters.AddWithValue("$ordinal", word.Order);
            command.Parameters.AddWithValue("$word", word.Word);
            command.Parameters.AddWithValue("$partOfSpeech", word.PartOfSpeech);
            command.Parameters.AddWithValue("$definition", word.Definition);
            command.Parameters.AddWithValue("$example", word.Example);
            command.Parameters.AddWithValue("$pronunciation", word.Pronunciation);
            command.Parameters.AddWithValue("$isReviewWord", word.IsReviewWord ? 1 : 0);
            command.Parameters.AddWithValue("$isRead", word.IsRead ? 1 : 0);
            command.Parameters.AddWithValue("$correctStreak", word.CorrectStreak);
            command.Parameters.AddWithValue("$isMastered", word.IsMastered ? 1 : 0);
            command.Parameters.AddWithValue("$timesCorrect", word.TimesCorrect);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string GetLessonKey(DateOnly date, int setNumber) =>
        setNumber == 1 ? date.ToString("yyyy-MM-dd") : $"{date:yyyy-MM-dd}#{setNumber}";

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var infoCommand = connection.CreateCommand();
        infoCommand.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await infoCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();
        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}