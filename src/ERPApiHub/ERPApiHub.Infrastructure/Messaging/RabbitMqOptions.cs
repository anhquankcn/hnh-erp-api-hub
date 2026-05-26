namespace ERPApiHub.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "1stopshop_event_bus";
    public string IngestionQueue { get; set; } = "erphub.ingestion";
    public string IngestionBindingKey { get; set; } = "erphub.ingestion.#";
    public string IngestionDeadLetterQueue { get; set; } = "erphub.dlq.ingestion";
}
