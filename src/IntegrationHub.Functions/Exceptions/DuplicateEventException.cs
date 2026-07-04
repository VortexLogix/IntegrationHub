namespace IntegrationHub.Functions.Exceptions;

public sealed class DuplicateEventException : Exception
{
    public DuplicateEventException(string eventId)
        : base($"Duplicate event detected for eventId '{eventId}'.")
    {
        EventId = eventId;
    }

    public string EventId { get; }
}
