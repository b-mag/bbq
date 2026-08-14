using System.Diagnostics;

namespace Carcosa.Server.P2P;

/// <summary>
/// Runtime capability metrics for a peer in the mesh.
/// Updated periodically and broadcast to other peers for future load balancing.
/// </summary>
public sealed class PeerMetrics
{
    public required string PeerId { get; init; }
    public long LatencyMs { get; set; }
    public float PacketLossRate { get; set; }
    public int CpuUsagePercent { get; set; }
    public int AvailableCpuPercent { get; set; }
    public long AvailableMemoryMb { get; set; }
    public float UploadBandwidthMbps { get; set; }
    public float DownloadBandwidthMbps { get; set; }
    public float CurrentUploadUtilization { get; set; }
    public float CurrentDownloadUtilization { get; set; }
    public TimeSpan Uptime { get; set; }
    public int DisconnectCount { get; set; }
    public DateTime LastUpdated { get; set; }
    public DateTime ConnectedSince { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Composite fitness score inputs for Phase 2 host election.
/// </summary>
public readonly struct PeerFitnessScore
{
    public long LatencyMs { get; init; }
    public float PacketLoss { get; init; }
    public int CpuUsage { get; init; }
    public float BandwidthUtilization { get; init; }
    public TimeSpan Uptime { get; init; }

    public float CalculateScore() => throw new NotImplementedException("Phase 2");
}

/// <summary>
/// Scoring helpers for peer fitness (Phase 2).
/// </summary>
public static class MetricsCalculator
{
    public static PeerFitnessScore FromMetrics(PeerMetrics metrics) => new()
    {
        LatencyMs = metrics.LatencyMs,
        PacketLoss = metrics.PacketLossRate,
        CpuUsage = metrics.CpuUsagePercent,
        BandwidthUtilization = Math.Max(metrics.CurrentUploadUtilization, metrics.CurrentDownloadUtilization),
        Uptime = metrics.Uptime,
    };
}

/// <summary>
/// Collects local peer metrics and stores metrics received from remote peers.
/// </summary>
public sealed class MetricsCollector
{
    private readonly PeerIdentity _localIdentity;
    private readonly PeerMesh _mesh;
    private readonly PeerMetrics _localMetrics;
    private readonly Dictionary<string, PeerMetrics> _remoteMetrics = new();
    private readonly object _remoteLock = new();
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSample = DateTime.UtcNow;
    private long _lastBytesSent;
    private long _lastBytesReceived;
    private DateTime _lastBandwidthSample = DateTime.UtcNow;

    public MetricsCollector(PeerIdentity localIdentity, PeerMesh mesh)
    {
        _localIdentity = localIdentity;
        _mesh = mesh;
        _localMetrics = new PeerMetrics
        {
            PeerId = localIdentity.PeerId,
            AvailableCpuPercent = 50,
            AvailableMemoryMb = GetAvailableMemoryMb(),
            UploadBandwidthMbps = 50,
            DownloadBandwidthMbps = 100,
        };
        _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
    }

    public PeerMetrics LocalMetrics => _localMetrics;

    public void UpdateLocalMetrics()
    {
        var now = DateTime.UtcNow;
        var process = Process.GetCurrentProcess();

        var cpuDelta = process.TotalProcessorTime - _lastCpuTime;
        var timeDelta = now - _lastCpuSample;
        if (timeDelta.TotalMilliseconds > 0)
        {
            var cpuPercent = (int)Math.Clamp(
                cpuDelta.TotalMilliseconds / (timeDelta.TotalMilliseconds * Environment.ProcessorCount) * 100,
                0, 100);
            _localMetrics.CpuUsagePercent = cpuPercent;
            _localMetrics.AvailableCpuPercent = Math.Max(0, 100 - cpuPercent);
        }

        _lastCpuTime = process.TotalProcessorTime;
        _lastCpuSample = now;

        long totalSent = 0;
        long totalReceived = 0;
        long totalLatency = 0;
        int latencyCount = 0;

        foreach (var conn in _mesh.Connections)
        {
            totalSent += conn.BytesSent;
            totalReceived += conn.BytesReceived;
            if (conn.LatencyMs > 0)
            {
                totalLatency += conn.LatencyMs;
                latencyCount++;
            }
        }

        var bandwidthDelta = now - _lastBandwidthSample;
        if (bandwidthDelta.TotalSeconds > 0)
        {
            var sentMbps = (float)((totalSent - _lastBytesSent) * 8 / bandwidthDelta.TotalSeconds / 1_000_000);
            var recvMbps = (float)((totalReceived - _lastBytesReceived) * 8 / bandwidthDelta.TotalSeconds / 1_000_000);

            _localMetrics.CurrentUploadUtilization = _localMetrics.UploadBandwidthMbps > 0
                ? sentMbps / _localMetrics.UploadBandwidthMbps
                : 0;
            _localMetrics.CurrentDownloadUtilization = _localMetrics.DownloadBandwidthMbps > 0
                ? recvMbps / _localMetrics.DownloadBandwidthMbps
                : 0;
        }

        _lastBytesSent = totalSent;
        _lastBytesReceived = totalReceived;
        _lastBandwidthSample = now;

        _localMetrics.LatencyMs = latencyCount > 0 ? totalLatency / latencyCount : 0;
        _localMetrics.AvailableMemoryMb = GetAvailableMemoryMb();
        _localMetrics.Uptime = now - _localMetrics.ConnectedSince;
        _localMetrics.LastUpdated = now;
    }

    public PeerMetricsUpdatePayload CreateUpdatePayload()
    {
        UpdateLocalMetrics();
        return new PeerMetricsUpdatePayload
        {
            PeerId = _localIdentity.PeerId,
            CurrentCpuUsagePercent = _localMetrics.CpuUsagePercent,
            CurrentUploadUtilization = _localMetrics.CurrentUploadUtilization,
            CurrentDownloadUtilization = _localMetrics.CurrentDownloadUtilization,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    public void StoreRemoteMetrics(PeerMetricsUpdatePayload payload)
    {
        lock (_remoteLock)
        {
            if (!_remoteMetrics.TryGetValue(payload.PeerId, out var metrics))
            {
                metrics = new PeerMetrics { PeerId = payload.PeerId };
                _remoteMetrics[payload.PeerId] = metrics;
            }

            metrics.CpuUsagePercent = payload.CurrentCpuUsagePercent;
            metrics.CurrentUploadUtilization = payload.CurrentUploadUtilization;
            metrics.CurrentDownloadUtilization = payload.CurrentDownloadUtilization;
            metrics.LastUpdated = DateTimeOffset.FromUnixTimeMilliseconds(payload.Timestamp).UtcDateTime;
        }
    }

    public IReadOnlyDictionary<string, PeerMetrics> GetRemoteMetrics()
    {
        lock (_remoteLock)
        {
            return new Dictionary<string, PeerMetrics>(_remoteMetrics);
        }
    }

    private static long GetAvailableMemoryMb()
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            return gcInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        }
        catch
        {
            return 0;
        }
    }
}
