using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

var results = new List<object>();
foreach (var length in new[] { 0, 32, 4096, 131072, 1048576 })
foreach (var known in new[] { true, false })
foreach (var chunk in new[] { int.MaxValue, 1024 })
{
    var bytes = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
    using var stream = new ChunkStream(bytes, chunk);
    var context = new DefaultHttpContext();
    context.Request.Body = stream;
    context.Request.ContentLength = known ? length : null;
    var iterations = length <= 4096 ? 10000 : 500;
    for (var round = -1; round < 5; round++)
    foreach (var candidate in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
    {
        Func<HttpContext, int, CancellationToken, Task<byte[]?>> reader = candidate
            ? static (c, m, t) => CandidateReader.ReadBodyAsync(c, m, t)
            : BaselineReader.ReadBodyAsync;
        stream.Position = 0;
        if (!reader(context, 2 * 1024 * 1024, CancellationToken.None).GetAwaiter().GetResult()!.AsSpan().SequenceEqual(bytes))
            throw new InvalidOperationException("Body mismatch.");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var gen0 = GC.CollectionCount(0);
        var start = Stopwatch.GetTimestamp();
        var count = round < 0 ? Math.Min(iterations, 1000) : iterations;
        long checksum = 0;
        for (var i = 0; i < count; i++)
        {
            stream.Position = 0;
            var task = reader(context, 2 * 1024 * 1024, CancellationToken.None);
            if (!task.IsCompletedSuccessfully) throw new InvalidOperationException("Expected synchronous memory input.");
            var body = task.Result!;
            checksum += body.Length;
            if (body.Length > 0) checksum += body[^1];
        }
        var elapsed = Stopwatch.GetElapsedTime(start);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (checksum != (long)count * (length + (length == 0 ? 0 : bytes[^1]))) throw new InvalidOperationException("Wrong bytes.");
        if (round < 0) continue;
        results.Add(new { Length = length, KnownLength = known, Chunk = chunk, Round = round,
            Candidate = candidate, Iterations = count, NsPerRead = elapsed.TotalNanoseconds / count,
            BytesPerRead = allocated / count, Gen0 = GC.CollectionCount(0) - gen0 });
    }
    Console.WriteLine($"Measured length={length}, known={known}, chunk={chunk}");
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new { Environment.Version, OS = Environment.OSVersion.ToString(),
    Environment.ProcessorCount, GCSettings.IsServerGC, Results = results }, new JsonSerializerOptions { WriteIndented = true }));

sealed class ChunkStream(byte[] bytes, int chunk) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
}
