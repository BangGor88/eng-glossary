using EnglishWords.Core.Models;
using EnglishWords.Core.Services;
using Xunit;

namespace EnglishWords.Core.Tests;

public sealed class LessonValidatorTests
{
    [Fact]
    public void Validate_AcceptsFiveCompleteDistinctWords()
    {
        LessonValidator.Validate(ValidWords(), 5);
    }

    [Fact]
    public void Validate_RejectsWrongWordCount()
    {
        var exception = Assert.Throws<InvalidDataException>(() => LessonValidator.Validate(ValidWords().Take(4).ToArray(), 5));

        Assert.Contains("exactly 5", exception.Message);
    }

    [Fact]
    public void Validate_RejectsDuplicateWordsIgnoringCase()
    {
        var words = ValidWords().ToArray();
        words[4] = words[4] with { Word = words[0].Word.ToUpperInvariant() };

        Assert.Throws<InvalidDataException>(() => LessonValidator.Validate(words, 5));
    }

    [Fact]
    public void Validate_RejectsMissingExample()
    {
        var words = ValidWords().ToArray();
        words[2] = words[2] with { Example = " " };

        Assert.Throws<InvalidDataException>(() => LessonValidator.Validate(words, 5));
    }

    [Fact]
    public void Validate_RejectsDuplicateQuizDetails()
    {
        var words = ValidWords().ToArray();
        words[4] = words[4] with { Definition = words[0].Definition };

        var exception = Assert.Throws<InvalidDataException>(() => LessonValidator.Validate(words, 5));

        Assert.Contains("distinct meanings", exception.Message);
    }

    private static IReadOnlyList<GeneratedWord> ValidWords() =>
    [
        new("adapt", "verb", "to change to fit a new situation", "She adapted quickly to the new team.", "uh-DAPT"),
        new("brief", "adjective", "short in time or length", "We had a brief meeting.", "BREEF"),
        new("curious", "adjective", "eager to learn or know", "He was curious about the result.", "KYOOR-ee-us"),
        new("gather", "verb", "to bring things together", "Please gather the papers.", "GATH-er"),
        new("steady", "adjective", "firm and not changing suddenly", "Keep a steady pace.", "STED-ee")
    ];
}