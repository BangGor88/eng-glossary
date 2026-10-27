namespace EnglishWords.Core.Models;

public sealed record DailyLesson(
	DateOnly Date,
	IReadOnlyList<VocabularyWord> Words,
	int QuizScore = 0,
	int BestScore = 0,
	int QuizAttempts = 0,
	bool QuizComplete = false,
	bool QuizPassed = false,
	int SetNumber = 1);