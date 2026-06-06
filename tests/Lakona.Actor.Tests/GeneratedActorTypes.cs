namespace Lakona.Actor.Tests;

public abstract record GeneratedCounterMessage;

public sealed record GeneratedAdd(int Value) : GeneratedCounterMessage;

public sealed record GeneratedGetCounter : GeneratedCounterMessage;

public sealed class GeneratedCounterActor : IActor<GeneratedCounterMessage>
{
    private int value;

    public ValueTask OnMessage(ActorContext<GeneratedCounterMessage> ctx, GeneratedCounterMessage message)
    {
        switch (message)
        {
            case GeneratedAdd add:
                value += add.Value;
                break;
            case GeneratedGetCounter:
                ctx.Respond(value);
                break;
        }

        return ValueTask.CompletedTask;
    }
}

[ActorClient]
public interface IGeneratedCounterClient
{
    ValueTask Add(int value);

    ValueTask<int> GetCounter();
}

internal sealed class GeneratedCounterClientActor : IActor<GeneratedCounterClientMessage>
{
    private int value;

    public ValueTask OnMessage(ActorContext<GeneratedCounterClientMessage> ctx, GeneratedCounterClientMessage message)
    {
        switch (message)
        {
            case GeneratedCounterClientAddRequest add:
                value += add.Value;
                break;
            case GeneratedCounterClientGetCounterRequest:
                ctx.Respond(value);
                break;
        }

        return ValueTask.CompletedTask;
    }
}
