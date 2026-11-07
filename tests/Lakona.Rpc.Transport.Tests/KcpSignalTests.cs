using System.Buffers;
using System.Net.Sockets.Kcp;
using System.Runtime.ExceptionServices;
using Lakona.Rpc.Transport.Kcp;

namespace Lakona.Rpc.Transport.Tests;

public sealed class KcpSignalTests
{
    [Fact]
    public void Repeated_capacity_notifications_do_not_allocate_exceptions()
    {
        var buffers = new Buffers();
        using var kcp = new SimpleSegManager.Kcp(1, buffers, buffers);
        var sender = new KcpFrameSender(kcp, new object(), _ => { }, _ => { });
        sender.Signal();
        var thread = Environment.CurrentManagedThreadId;
        var exceptions = 0;
        void Observe(object? _, FirstChanceExceptionEventArgs e)
        {
            if (Environment.CurrentManagedThreadId == thread && e.Exception is SemaphoreFullException) exceptions++;
        }
        AppDomain.CurrentDomain.FirstChanceException += Observe;
        try
        {
            for (var i = 0; i < 10_000; i++) sender.Signal();
            Assert.Equal(0, exceptions);
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= Observe; }
    }

    private sealed class Buffers : IKcpCallback, IRentable
    {
        public IMemoryOwner<byte> RentBuffer(int size) => MemoryPool<byte>.Shared.Rent(size);
        public void Output(IMemoryOwner<byte> buffer, int length) => buffer.Dispose();
    }
}
