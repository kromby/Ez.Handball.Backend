namespace Ez.Handball.Ingestion.Models;

public sealed record PlayerStatLine(
    string Side,
    int? Jersey,
    string Name,
    bool IsGoalkeeper,
    int Goals,
    int YellowCards,
    int TwoMinuteSuspensions,
    int RedCards,
    int? Assists = null,
    int? Turnovers = null,
    int? Steals = null,
    double? PlusMinus = null,
    int? Shots = null,
    int? Blocks = null,
    double? Stops = null,
    double? ExpectedGoals = null,
    int? PenaltiesEarned = null,
    int? PenaltyGoals = null,
    int? Saves = null,
    double? SaveRate = null,
    double? ExpectedSaves = null,
    double? GoalsAgainst = null,
    int? PenaltySaves = null
);
