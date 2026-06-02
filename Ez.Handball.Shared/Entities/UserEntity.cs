using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

public sealed class UserEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "user";
    public string RowKey { get; set; } = string.Empty;        // userId (GUID "N")
    public string Email { get; set; } = string.Empty;          // normalized: trimmed + lowercased
    public string DisplayName { get; set; } = string.Empty;
    public string Language { get; set; } = "is";               // "is" | "en"
    public string FavoriteClubId { get; set; } = string.Empty; // validated against Clubs
    public bool EmailVerified { get; set; }                    // starts false
    public string PasswordHash { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;     // reserved for managed-provider subject
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}
