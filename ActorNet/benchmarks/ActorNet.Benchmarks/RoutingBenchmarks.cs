// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using ActorNet.Cluster;
using ActorNet.Serialization;
using BenchmarkDotNet.Attributes;

namespace ActorNet.Benchmarks;

/// <summary>
/// The two computations every message pays for before it reaches an actor: deciding who owns the
/// key, and - on a remote hop only - serializing the payload.
/// </summary>
/// <remarks>
/// Worth measuring separately because they answer different questions. Placement runs on every
/// send in a cluster, so it has to be cheap enough to disappear next to the channel write.
/// Serialization runs only on the wire, which is the justification for the local path carrying the
/// materialized object instead.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class RoutingBenchmarks
{
    private HashRing _ring = null!;
    private string[] _keys = null!;
    private JsonMessageSerializer _serializer = null!;
    private readonly Reading _message = new("sensor-042", 21.5, 1_700_000_000);

    /// <summary>Cluster size the ring is built for.</summary>
    [Params(3, 12)]
    public int Members { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _ring = new HashRing(Enumerable.Range(1, Members).Select(i => $"node-{i}"), virtualNodes: 128);
        _keys = Enumerable.Range(0, 1024).Select(i => $"DeviceActor/sensor-{i:D5}").ToArray();

        _serializer = new JsonMessageSerializer();
        _serializer.Types.Register<Reading>("bench.reading");
    }

    [Benchmark(Description = "Hash one key")]
    public ulong Hash() => HashRing.Hash(_keys[0]);

    [Benchmark(Description = "Place 1024 keys on the ring")]
    public int Placement()
    {
        var checksum = 0;
        foreach (var key in _keys) checksum += _ring.OwnerOf(key).Length;
        return checksum;
    }

    [Benchmark(Description = "Serialize one message")]
    public (string, System.Text.Json.JsonElement) Serialize() => _serializer.Serialize(_message);

    [Benchmark(Description = "Round-trip one message")]
    public object RoundTrip()
    {
        var (alias, payload) = _serializer.Serialize(_message);
        return _serializer.Deserialize(alias, payload);
    }
}

/// <summary>A message the size a real telemetry payload tends to be.</summary>
public sealed record Reading(string DeviceId, double Celsius, long UnixSeconds);
