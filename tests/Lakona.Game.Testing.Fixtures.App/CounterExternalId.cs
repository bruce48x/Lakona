using MemoryPack;

namespace Lakona.Game.Testing.Fixtures.App;

// An external-style type without a MemoryPack marker, supported as a DTO member.
public sealed record CounterExternalId(string Value);

public sealed class CounterExternalIdFormatterAttribute : MemoryPackCustomFormatterAttribute<CounterExternalId>
{
    public override IMemoryPackFormatter<CounterExternalId> GetFormatter() => new Formatter();

    private sealed class Formatter : MemoryPackFormatter<CounterExternalId>
    {
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref CounterExternalId? value) =>
            writer.WriteString(value?.Value);

        public override void Deserialize(ref MemoryPackReader reader, scoped ref CounterExternalId? value)
        {
            var text = reader.ReadString();
            value = text is null ? null : new CounterExternalId(text);
        }
    }
}
