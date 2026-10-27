namespace EnglishWords.Core.Models;

public sealed record ScorecardWord(
    string Word,
    string PartOfSpeech,
    string Definition,
    int TimesCorrect,
    int CorrectStreak,
    bool IsMastered,
    DateOnly LastSeen)
{
    public string MasteryLabel => IsMastered ? "LEARNED" : $"{CorrectStreak} / 5 streak";
}