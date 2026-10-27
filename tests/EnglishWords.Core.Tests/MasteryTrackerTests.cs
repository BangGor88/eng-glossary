using EnglishWords.Core.Services;
using Xunit;

namespace EnglishWords.Core.Tests;

public sealed class MasteryTrackerTests
{
    [Fact]
    public void RecordAnswer_MastersWordOnFifthConsecutiveCorrectAnswer()
    {
        var mastery = new EnglishWords.Core.Models.WordMastery(0, false);

        for (var answer = 0; answer < MasteryTracker.RequiredCorrectStreak; answer++)
        {
            mastery = MasteryTracker.RecordAnswer(mastery.CorrectStreak, true);
        }

        Assert.Equal(5, mastery.CorrectStreak);
        Assert.True(mastery.IsMastered);
    }

    [Fact]
    public void RecordAnswer_WrongAnswerResetsConsecutiveStreakAndMastery()
    {
        var mastery = MasteryTracker.RecordAnswer(4, false);

        Assert.Equal(0, mastery.CorrectStreak);
        Assert.False(mastery.IsMastered);
    }
}