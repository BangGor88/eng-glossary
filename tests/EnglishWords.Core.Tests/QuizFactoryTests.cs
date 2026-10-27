using EnglishWords.Core.Models;
using EnglishWords.Core.Services;
using Xunit;

namespace EnglishWords.Core.Tests;

public sealed class QuizFactoryTests
{
    [Fact]
    public void Create_BuildsSixQuestionsWithThreeOptionsAndTwoQuestionsPerDetail()
    {
        var questions = QuizFactory.Create(Lesson());

        Assert.Equal(6, questions.Count);
        Assert.All(questions, question =>
        {
            Assert.Equal(3, question.Options.Count);
            Assert.Single(question.Options, option => option.IsCorrect);
            Assert.Equal(3, question.Options.Select(option => option.Text).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });
        Assert.All(Enum.GetValues<QuizDetail>(), detail => Assert.Equal(3, questions.Count(question => question.Detail == detail)));
    }

    [Fact]
    public void Create_UsesTheMatchingWordDetailAsCorrectAnswer()
    {
        var lesson = Lesson();

        foreach (var question in QuizFactory.Create(lesson))
        {
            var word = lesson.Words.Single(item => item.Id == question.WordId);
            var expected = question.Detail switch
            {
                QuizDetail.Meaning => word.Definition,
                QuizDetail.Example => word.Example,
                _ => throw new ArgumentOutOfRangeException()
            };
            Assert.Equal(expected, question.Options.Single(option => option.IsCorrect).Text);
        }
    }

    [Fact]
    public void Create_RejectsIncompleteLesson()
    {
        var lesson = Lesson();

        Assert.Throws<InvalidDataException>(() => QuizFactory.Create(lesson with { Words = lesson.Words.Take(5).ToArray() }));
    }

    private static DailyLesson Lesson()
    {
        var words = new[]
        {
            new GeneratedWord("adapt", "verb", "change to fit a new situation", "She adapted to the new team.", "uh-DAPT"),
            new GeneratedWord("brief", "adjective", "short in time or length", "We had a brief meeting.", "BREEF"),
            new GeneratedWord("curious", "adjective", "eager to learn or know", "He was curious about it.", "KYOOR-ee-us"),
            new GeneratedWord("gather", "verb", "bring things together", "Gather the papers, please.", "GATH-er"),
            new GeneratedWord("steady", "adjective", "firm and not changing suddenly", "Keep a steady pace.", "STED-ee"),
            new GeneratedWord("notice", "verb", "see or become aware of something", "I noticed a small bird.", "NOH-tiss")
        };

        return new DailyLesson(
            new DateOnly(2026, 10, 27),
            words.Select((word, index) => new VocabularyWord(
                Guid.NewGuid(), index + 1, word.Word, word.PartOfSpeech, word.Definition, word.Example, word.Pronunciation,
                false, false, 0, false)).ToArray());
    }
}