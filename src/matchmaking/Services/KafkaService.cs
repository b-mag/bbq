// =============================================================================
// KafkaService.cs — Kafka Producer/Consumer for Session Discovery
// =============================================================================
//
// WHY KAFKA:
// Kafka provides durable, real-time event streaming for session discovery.
// Game servers publish heartbeats to a topic; this service consumes them.
// This decouples game servers from the matchmaking service — they don't need
// to know each other's addresses, just the Kafka broker.
//
// TOPICS:
//   - sessions.active: Game servers publish heartbeats every 10 seconds.
//     Messages are JSON-serialized SessionHeartbeat objects.
//     Consumer reads these to populate the SessionRegistry.
//
// GRACEFUL DEGRADATION:
// If Kafka is unavailable, the service still works via the REST heartbeat
// endpoint (/api/sessions/heartbeat). Game servers can fall back to REST
// if Kafka connectivity fails.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;

namespace Carcosa.Matchmaking.Services;

/// <summary>
/// Handles Kafka producer/consumer operations for session discovery.
/// Runs a background consumer loop that populates the SessionRegistry.
/// </summary>
public sealed class KafkaService
{
    private const string SessionTopic = "sessions.active";
    private readonly string _bootstrapServers;
    private readonly ProducerConfig _producerConfig;
    private readonly ConsumerConfig _consumerConfig;

    public KafkaService(string bootstrapServers)
    {
        _bootstrapServers = bootstrapServers;
        _producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.Leader, // Fast ack, session data is ephemeral
        };
        _consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "carcosa-matchmaking",
            AutoOffsetReset = AutoOffsetReset.Latest, // Only care about current sessions
            EnableAutoCommit = true,
        };
    }

    /// <summary>
    /// Publish a session heartbeat to Kafka.
    /// Called by game servers (via REST proxy or directly).
    /// </summary>
    public async Task PublishHeartbeat(SessionHeartbeat heartbeat)
    {
        try
        {
            using var producer = new ProducerBuilder<string, string>(_producerConfig).Build();
            var json = JsonSerializer.Serialize(heartbeat, KafkaJsonContext.Default.SessionHeartbeat);
            await producer.ProduceAsync(SessionTopic, new Message<string, string>
            {
                Key = heartbeat.SessionId,
                Value = json
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Kafka] Failed to publish heartbeat: {ex.Message}");
        }
    }

    /// <summary>
    /// Background consumer loop that reads session heartbeats from Kafka
    /// and updates the SessionRegistry. Runs until the application shuts down.
    /// 
    /// WHY BACKGROUND LOOP: Kafka consumers are long-lived — they maintain a
    /// connection to the broker and receive messages as they arrive. This loop
    /// runs on a dedicated thread started in Program.cs.
    /// </summary>
    public void ConsumeSessionHeartbeats(SessionRegistry registry)
    {
        Console.WriteLine($"[Kafka] Starting consumer for topic '{SessionTopic}'...");

        try
        {
            using var consumer = new ConsumerBuilder<string, string>(_consumerConfig).Build();
            consumer.Subscribe(SessionTopic);

            while (true)
            {
                try
                {
                    var result = consumer.Consume(TimeSpan.FromSeconds(1));
                    if (result == null) continue;

                    var heartbeat = JsonSerializer.Deserialize(result.Message.Value,
                        KafkaJsonContext.Default.SessionHeartbeat);

                    if (heartbeat != null)
                    {
                        registry.UpdateSession(heartbeat);
                    }
                }
                catch (ConsumeException ex)
                {
                    Console.WriteLine($"[Kafka] Consume error: {ex.Error.Reason}");
                }
            }
        }
        catch (Exception ex)
        {
            // Kafka not available — fall back to REST heartbeats
            Console.WriteLine($"[Kafka] Consumer failed to start: {ex.Message}");
            Console.WriteLine("[Kafka] Sessions will be tracked via REST heartbeats only.");
        }
    }
}

/// <summary>
/// AOT-compatible JSON context for Kafka message serialization.
/// </summary>
[JsonSerializable(typeof(SessionHeartbeat))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class KafkaJsonContext : JsonSerializerContext { }
