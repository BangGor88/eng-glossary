using EnglishWords.Core.Abstractions;
using EnglishWords.Core.Models;

namespace EnglishWords.Core.Services;

public sealed class DailyLessonService(
    IVocabularyRepository repository,
    ILessonGenerator generator,
    TimeProvider timeProvider)
{
    public const int DailyWordCount = 6;
    private const int CarryoverWordCount = 2;

    public async Task<DailyLesson> GetTodayAsync(CancellationToken cancellationToken = default, IProgress<string>? generationProgress = null)
    {
        var now = timeProvider.GetLocalNow();
        var date = DateOnly.FromDateTime(now.DateTime);
        var existing = await repository.GetLatestLessonAsync(date, cancellationToken);
        if (existing is not null)
        {
            return await CompleteExistingLessonAsync(existing, cancellationToken, generationProgress);
        }

        var carryovers = await repository.GetReviewWordsAsync(date, CarryoverWordCount, cancellationToken);
        var generationCount = DailyWordCount - carryovers.Count;
        var generatedWords = await generator.GenerateAsync(
            date,
            generationCount,
            carryovers.Select(word => word.Word).ToArray(),
            cancellationToken,
            generationProgress);
        LessonValidator.Validate(generatedWords, generationCount);

        var words = carryovers.Select((word, index) => new VocabularyWord(
            Guid.NewGuid(),
            index + 1,
            word.Word,
            word.PartOfSpeech,
            word.Definition,
            word.Example,
            word.Pronunciation,
            true,
            false,
            word.CorrectStreak,
            word.IsMastered,
            0))
            .Concat(generatedWords.Select((word, index) => new VocabularyWord(
                Guid.NewGuid(),
                carryovers.Count + index + 1,
                word.Word.Trim(),
                word.PartOfSpeech.Trim(),
                word.Definition.Trim(),
                word.Example.Trim(),
                word.Pronunciation.Trim(),
                false,
                false,
                0,
                false,
                0)))
            .ToArray();

        var lesson = new DailyLesson(date, words, SetNumber: 1);
        LessonValidator.Validate(words.Select(ToGeneratedWord).ToArray(), DailyWordCount);
        _ = QuizFactory.Create(lesson);

        await repository.SaveLessonAsync(lesson, cancellationToken);
        return lesson;
    }

    public async Task<DailyLesson> GenerateNewSetAsync(
        DailyLesson completedLesson,
        CancellationToken cancellationToken = default,
        IProgress<string>? generationProgress = null)
    {
        if (!completedLesson.QuizPassed)
        {
            throw new InvalidOperationException("Finish the quiz with at least three correct answers before starting a new set.");
        }

        var previousWords = await repository.GetWordsForDateAsync(completedLesson.Date, cancellationToken);
        var generatedWords = await generator.GenerateAsync(
            completedLesson.Date,
            DailyWordCount,
            previousWords.Select(word => word.Word).ToArray(),
            cancellationToken,
            generationProgress);
        LessonValidator.Validate(generatedWords, DailyWordCount);

        var words = generatedWords.Select((word, index) => new VocabularyWord(
            Guid.NewGuid(),
            index + 1,
            word.Word.Trim(),
            word.PartOfSpeech.Trim(),
            word.Definition.Trim(),
            word.Example.Trim(),
            word.Pronunciation.Trim(),
            false,
            false,
            0,
            false)).ToArray();
        LessonValidator.Validate(words.Select(ToGeneratedWord).ToArray(), DailyWordCount);

        var lesson = new DailyLesson(completedLesson.Date, words, SetNumber: completedLesson.SetNumber + 1);
        _ = QuizFactory.Create(lesson);
        await repository.SaveLessonAsync(lesson, cancellationToken);
        return lesson;
    }

    private async Task<DailyLesson> CompleteExistingLessonAsync(
        DailyLesson lesson,
        CancellationToken cancellationToken,
        IProgress<string>? generationProgress)
    {
        var missingCount = DailyWordCount - lesson.Words.Count;
        if (missingCount <= 0)
        {
            return lesson;
        }

        var avoidWords = lesson.Words.Select(word => word.Word).ToArray();
        var generatedWords = await generator.GenerateAsync(lesson.Date, missingCount, avoidWords, cancellationToken, generationProgress);
        LessonValidator.Validate(generatedWords, missingCount);

        var additions = generatedWords.Select((word, index) => new VocabularyWord(
            Guid.NewGuid(),
            lesson.Words.Count + index + 1,
            word.Word.Trim(),
            word.PartOfSpeech.Trim(),
            word.Definition.Trim(),
            word.Example.Trim(),
            word.Pronunciation.Trim(),
            false,
            false,
            0,
            false,
            0)).ToArray();

        var completedWords = lesson.Words.Concat(additions).ToArray();
        LessonValidator.Validate(completedWords.Select(ToGeneratedWord).ToArray(), DailyWordCount);
        _ = QuizFactory.Create(lesson with { Words = completedWords });

        await repository.AddLessonWordsAsync(lesson.Date, lesson.SetNumber, additions, cancellationToken);
        return lesson with { Words = completedWords };
    }

    private static GeneratedWord ToGeneratedWord(VocabularyWord word) => new(
        word.Word,
        word.PartOfSpeech,
        word.Definition,
        word.Example,
        word.Pronunciation);
}