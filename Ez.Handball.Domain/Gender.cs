namespace Ez.Handball.Domain;

public sealed record Gender(string Value, string Label);

public static class Genders
{
    // The single source of truth for the gender enum. Labels are static Icelandic;
    // locale-aware labels are deferred to #19.
    public static readonly IReadOnlyList<Gender> All =
    [
        new Gender("karlar", "Karlar"),
        new Gender("kvenna", "Kvenna"),
    ];
}
