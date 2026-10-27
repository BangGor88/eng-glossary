using EnglishWords.Core.Models;

namespace EnglishWords.Core.Services;

public static class LessonValidator
{
    public static void Validate(IReadOnlyList<GeneratedWord> words, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(words);

        if (expectedCount < 1 || words.Count != expectedCount)
        {
            throw new InvalidDataException($"The lesson must contain exactly {expectedCount} words; received {words.Count}.");
        }

        if (words.Any(word => word is null ||
                              IsBlank(word.Word) ||
                              IsBlank(word.PartOfSpeech) ||
                              IsBlank(word.Definition) ||
                              IsBlank(word.Example) ||
                              IsBlank(word.Pronunciation)))
        {
            throw new InvalidDataException("Every word needs a word, part of speech, definition, example, and pronunciation hint.");
        }

        ValidateDistinct(words.Select(word => word.Word), "words");
        ValidateDistinct(words.Select(word => word.Definition), "meanings");
        ValidateDistinct(words.Select(word => word.Example), "examples");
        ValidateDistinct(words.Select(word => word.Pronunciation), "pronunciation hints");
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static void ValidateDistinct(IEnumerable<string> values, string detailName)
    {
        if (values.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count())
        {
            throw new InvalidDataException($"Every lesson needs distinct {detailName} so the quiz has three clear choices.");
        }
    }
}