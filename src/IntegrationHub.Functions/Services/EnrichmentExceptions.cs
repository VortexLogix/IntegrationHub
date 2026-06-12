namespace IntegrationHub.Functions.Services;

public sealed class DuplicateEventException : Exception
{
    public DuplicateEventException(string eventId)
        : base($"Duplicate event detected for eventId '{eventId}'.")
    {
        EventId = eventId;
    }

    public string EventId { get; }
}
