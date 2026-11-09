using System.Buffers;
using Lakona.Game.Server.Management;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace Lakona.Game.Server.Tests.Management;

public sealed class LakonaHttpBodyReaderTests
{
    [Theory]
    [InlineData(0, true, 1)]
    [InlineData(0, false, 1)]
    [InlineData(32, true, 32)]
    [InlineData(32, false, 1)]
    [InlineData(4096, true, 127)]
    [InlineData(20000, false, 3000)]
    [InlineData(20000, true, 20000)]
    public async Task Reads_exact_detached_bytes_and_returns_scratch_once(int length, bool knownLength, int chunkSize)
    {
        var bytes = Payload(length);
        using var stream = new ControlledStream(bytes, chunkSize);
        var context = Context(stream, knownLength ? length : null);
        var pool = new TrackingPool();

        var result = await LakonaHttpHosting.ReadBodyAsync(context, 30000, TestContext.Current.CancellationToken, pool);

        Assert.Equal(bytes, result);
        if (length == 0) Assert.Same(Array.Empty<byte>(), result);
        Assert.Equal(200, context.Response.StatusCode);
        AssertReturned(pool);
        Assert.False(stream.Disposed); // ASP.NET owns the request stream.
        Assert.InRange(stream.LargestReadBuffer, 1, 8192);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Actual_bytes_are_checked_even_when_content_length_understates_body(int declaredLength)
    {
        using var stream = new ControlledStream(Payload(33), 7);
        var context = Context(stream, declaredLength);
        var pool = new TrackingPool();
        Assert.Null(await LakonaHttpHosting.ReadBodyAsync(context, 32, TestContext.Current.CancellationToken, pool));
        Assert.Equal(413, context.Response.StatusCode);
        AssertReturned(pool);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(8192, 8191)]
    [InlineData(8192, 8192)]
    [InlineData(8192, 8193)]
    public async Task Unknown_length_enforces_actual_size_boundary(int limit, int length)
    {
        var bytes = Payload(length);
        using var stream = new ControlledStream(bytes, 8192);
        var context = Context(stream, null);
        var pool = new TrackingPool();
        var result = await LakonaHttpHosting.ReadBodyAsync(context, limit, TestContext.Current.CancellationToken, pool);
        if (length <= limit)
        {
            Assert.Equal(bytes, result);
            Assert.Equal(200, context.Response.StatusCode);
        }
        else
        {
            Assert.Null(result);
            Assert.Equal(413, context.Response.StatusCode);
        }
        AssertReturned(pool);
        Assert.InRange(stream.LargestReadBuffer, 1, Math.Min(limit, 8192));
    }

    [Fact]
    public async Task Declared_oversize_rejects_without_reading_or_renting()
    {
        using var stream = new ControlledStream(Payload(33), 33);
        var context = Context(stream, 33);
        var pool = new TrackingPool();
        Assert.Null(await LakonaHttpHosting.ReadBodyAsync(context, 32, TestContext.Current.CancellationToken, pool));
        Assert.Equal(413, context.Response.StatusCode);
        Assert.Equal(0, stream.ReadCount);
        Assert.Equal(0, pool.Rents);
        Assert.Equal(0, pool.Returns);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preserves_server_limit_feature(bool readOnly)
    {
        var context = Context(Stream.Null, null);
        var feature = new SizeFeature { IsReadOnly = readOnly, MaxRequestBodySize = 100 };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        await LakonaHttpHosting.ReadBodyAsync(context, 32, TestContext.Current.CancellationToken);
        Assert.Equal(readOnly ? 100 : 32, feature.MaxRequestBodySize);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task Pending_read_cancellation_or_failure_returns_scratch_once(int completedChunks, bool failure)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var stream = new ControlledStream(Payload(32), 8) { PauseAtRead = completedChunks + 1 };
        var pool = new TrackingPool();
        var read = LakonaHttpHosting.ReadBodyAsync(Context(stream, null), 64, cancellation.Token, pool);
        await stream.Paused.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, pool.Returns);
        try
        {
            if (failure)
            {
                var expected = new IOException("read failed");
                stream.Resume.TrySetException(expected);
                Assert.Same(expected, await Assert.ThrowsAsync<IOException>(() => read));
            }
            else
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            }
            AssertReturned(pool);
            Assert.False(stream.Disposed);
        }
        finally { stream.Resume.TrySetResult(); }
    }

    [Fact]
    public async Task Pending_read_success_keeps_buffer_until_copy_finishes()
    {
        var bytes = Payload(20000);
        using var stream = new ControlledStream(bytes, 127) { PauseAtRead = 2 };
        var pool = new TrackingPool();
        var read = LakonaHttpHosting.ReadBodyAsync(Context(stream, null), 30000, TestContext.Current.CancellationToken, pool);
        try
        {
            await stream.Paused.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, pool.Returns);
        }
        finally { stream.Resume.TrySetResult(); }
        Assert.Equal(bytes, await read);
        AssertReturned(pool);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_before_read_or_between_read_and_copy_returns_scratch_once(bool cancelAfterRead)
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new ControlledStream(Payload(32), 8)
        {
            AfterRead = cancelAfterRead ? cancellation.Cancel : null
        };
        if (!cancelAfterRead) cancellation.Cancel();
        var pool = new TrackingPool();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LakonaHttpHosting.ReadBodyAsync(Context(stream, null), 64, cancellation.Token, pool));
        AssertReturned(pool);
    }

    [Theory]
    [InlineData(0, 1024)]
    [InlineData(32, 1024)]
    [InlineData(4096, 10000)]
    public async Task Warm_small_reads_do_not_allocate_a_large_scratch_buffer(int length, int maximumAllocatedBytes)
    {
        using var stream = new MemoryStream(Payload(length));
        var context = Context(stream, length);
        for (var i = 0; i < 100; i++)
        {
            stream.Position = 0;
            await LakonaHttpHosting.ReadBodyAsync(context, 1024 * 1024, CancellationToken.None);
        }
        long total = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            stream.Position = 0;
            var pending = LakonaHttpHosting.ReadBodyAsync(context, 1024 * 1024, CancellationToken.None);
            if (!pending.IsCompletedSuccessfully) throw new InvalidOperationException("Expected synchronous memory input.");
            total += (await pending)!.Length;
        }
        var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
        Assert.Equal(length * 1000L, total);
        Assert.InRange(allocated, 0, maximumAllocatedBytes);
    }

    private static DefaultHttpContext Context(Stream stream, long? length)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = stream;
        context.Request.ContentLength = length;
        return context;
    }

    private static byte[] Payload(int length) => Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();

    private static void AssertReturned(TrackingPool pool)
    {
        Assert.Equal(1, pool.Rents);
        Assert.Equal(1, pool.Returns);
        Assert.All(pool.Buffer!, value => Assert.Equal((byte)0xCC, value));
    }

    private sealed class TrackingPool : ArrayPool<byte>
    {
        public int Rents { get; private set; }
        public int Returns { get; private set; }
        public byte[]? Buffer { get; private set; }
        public override byte[] Rent(int minimumLength)
        {
            Rents++;
            // Real pools can return a larger bucket; the reader must cap its read slice.
            return Buffer = new byte[minimumLength + 64];
        }
        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.Same(Buffer, array);
            Returns++;
            array.AsSpan().Fill(0xCC);
        }
    }

    private sealed class SizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; init; }
        public long? MaxRequestBodySize { get; set; }
    }

    private sealed class ControlledStream(byte[] bytes, int chunkSize) : MemoryStream(bytes)
    {
        public int ReadCount { get; private set; }
        public int LargestReadBuffer { get; private set; }
        public int PauseAtRead { get; init; } = -1;
        public Action? AfterRead { get; init; }
        public bool Disposed { get; private set; }
        public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            LargestReadBuffer = Math.Max(LargestReadBuffer, buffer.Length);
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadCount == PauseAtRead)
            {
                Paused.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            var count = await base.ReadAsync(buffer[..Math.Min(chunkSize, buffer.Length)], cancellationToken);
            AfterRead?.Invoke();
            return count;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
