using Azure;
using Azure.Data.Tables;

namespace Ez.Handball.Shared.Entities;

// Generic key/value config store. PartitionKey = a config group (e.g. a rule set
// name like "fantasy-v1"), RowKey = a setting key, Value = the setting value.
public class ConfigEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Value { get; set; } = string.Empty;
}
