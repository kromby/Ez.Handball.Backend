namespace Ez.Handball.Domain;

// A transient notification event handed to channels. No Id/timestamps — there is no
// store in the skeleton. Title/Body are plain strings; templating is out of scope.
public sealed record Notification(
    string UserId,
    NotificationType Type,
    string Title,
    string Body);
