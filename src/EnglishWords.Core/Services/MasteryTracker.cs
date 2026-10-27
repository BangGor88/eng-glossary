using EnglishWords.Core.Models;

namespace EnglishWords.Core.Services;

public static class MasteryTracker
{
    public const int RequiredCorrectStreak = 5;

    public static WordMastery RecordAnswer(int currentStreak, bool isCorrect)
    {
        var streak = isCorrect ? currentStreak + 1 : 0;
        return new WordMastery(streak, streak >= RequiredCorrectStreak);
    }
}