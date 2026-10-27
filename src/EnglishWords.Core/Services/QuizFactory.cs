using EnglishWords.Core.Models;

namespace EnglishWords.Core.Services;

public static class QuizFactory
{
    public static IReadOnlyList<QuizQuestion> Create(DailyLesson lesson)
    {
        if (lesson.Words.Count != DailyLessonService.DailyWordCount)
        {
            throw new InvalidDataException("A quiz requires all six words in today's lesson.");
        }

        var questions = lesson.Words.Select((word, index) =>
        {
            var detail = (QuizDetail)(index % 2);
            var answer = GetDetail(word, detail);
            var distractors = lesson.Words
                .Where(candidate => candidate.Id != word.Id)
                .Select(candidate => GetDetail(candidate, detail))
                .Where(value => !string.Equals(value, answer, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(_ => Random.Shared.Next())
                .Take(2)
                .ToArray();

            if (distractors.Length != 2)
            {
                throw new InvalidDataException($"There aren't enough distinct {detail.ToString().ToLowerInvariant()} choices for this quiz. Retry lesson generation.");
            }

            var options = distractors
                .Select(value => new QuizOption(value, false))
                .Append(new QuizOption(answer, true))
                .OrderBy(_ => Random.Shared.Next())
                .ToArray();

            return new QuizQuestion(word.Id, word.Word, detail, options);
        }).OrderBy(_ => Random.Shared.Next()).ToArray();

        return questions;
    }

    private static string GetDetail(VocabularyWord word, QuizDetail detail) => detail switch
    {
        QuizDetail.Meaning => word.Definition,
        QuizDetail.Example => word.Example,
        _ => throw new ArgumentOutOfRangeException(nameof(detail))
    };
}