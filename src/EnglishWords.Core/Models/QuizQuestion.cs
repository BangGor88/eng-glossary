namespace EnglishWords.Core.Models;

public enum QuizDetail
{
    Meaning,
    Example
}

public sealed record QuizOption(string Text, bool IsCorrect, bool IsSelected = false);

public sealed record QuizQuestion(
    Guid WordId,
    string Word,
    QuizDetail Detail,
    IReadOnlyList<QuizOption> Options);