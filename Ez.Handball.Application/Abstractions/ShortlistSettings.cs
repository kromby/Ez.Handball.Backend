namespace Ez.Handball.Application.Abstractions;

/// <summary>Configuration for the player shortlist. MaxSize defaults to 20 (bound from "Shortlist:MaxSize").</summary>
public sealed record ShortlistSettings(int MaxSize);
