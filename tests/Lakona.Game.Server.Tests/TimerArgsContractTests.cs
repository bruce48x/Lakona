using Lakona.Game.Server.Hotfix.Timers;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class TimerArgsContractTests
{
    [Fact]
    public void Root_polymorphism_reports_path_and_both_types()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new LakonaTimerArgsSerializer().Serialize<BaseArgs>(new DerivedArgs()));
        AssertPolymorphism(error, "$", typeof(BaseArgs), typeof(DerivedArgs));
    }

    [Theory]
    [InlineData("$.Child")]
    [InlineData("$.Items[0]")]
    [InlineData("$.List[0]")]
    [InlineData("$.Items")]
    [InlineData("$.List")]
    public void Nested_polymorphism_reports_the_offending_value(string path)
    {
        var args = new Container();
        switch (path)
        {
            case "$.Child": args.Child = new DerivedArgs(); break;
            case "$.Items[0]": args.Items = new BaseArgs[] { new DerivedArgs() }; break;
            case "$.List[0]": args.List = [new DerivedArgs()]; break;
            case "$.Items": args.Items = new DerivedArgs[0]; break;
            case "$.List": args.List = new DerivedList(); break;
        }

        var error = Assert.Throws<InvalidOperationException>(() => new LakonaTimerArgsSerializer().Serialize(args));
        AssertPolymorphism(error, path,
            path == "$.Items" ? typeof(BaseArgs[]) : path == "$.List" ? typeof(List<BaseArgs>) : typeof(BaseArgs),
            path == "$.Items" ? typeof(DerivedArgs[]) : path == "$.List" ? typeof(DerivedList) : typeof(DerivedArgs));
    }

    [Fact]
    public void Snapshot_reads_original_properties_once()
    {
        var args = new CountingArgs { Value = 7 };
        var serializer = new LakonaTimerArgsSerializer();
        var snapshot = serializer.Serialize(args);
        Assert.Equal(1, args.ReadCount());
        args.Value = 99;
        var copy = Assert.IsType<CountingArgs>(serializer.Deserialize(snapshot.SerializerId, snapshot.JsonPayload, typeof(CountingArgs)));
        Assert.Equal(7, copy.Value);
    }

    [Fact]
    public void Exact_derived_type_and_nullable_values_remain_supported()
    {
        var serializer = new LakonaTimerArgsSerializer();
        var snapshot = serializer.Serialize(new DerivedArgs { Value = 7, Extra = 99 });
        var copy = Assert.IsType<DerivedArgs>(serializer.Deserialize(snapshot.SerializerId, snapshot.JsonPayload, typeof(DerivedArgs)));
        Assert.Equal(7, copy.Value);
        Assert.Equal(99, copy.Extra);
        serializer.Serialize<int?>(7);
        serializer.Serialize<int?>(null);
        serializer.Serialize(new Container());
    }

    [Fact]
    public void Constructor_parameter_shapes_outside_the_property_graph_are_still_checked()
    {
        var serializer = new LakonaTimerArgsSerializer();
        var error = Assert.Throws<InvalidOperationException>(() =>
            serializer.Serialize(new UnsupportedConstructorArgs("value")));
        Assert.Contains("System.Object", error.ToString());
        Assert.Throws<InvalidOperationException>(() => serializer.Deserialize(
            LakonaTimerArgsSerializer.SystemTextJsonSerializerId,
            "{\"Value\":\"value\"}"u8.ToArray(), typeof(UnsupportedConstructorArgs)));
    }

    private static void AssertPolymorphism(Exception error, string path, Type declared, Type actual)
    {
        Assert.Contains(path, error.Message);
        Assert.Contains(declared.FullName!, error.Message);
        Assert.Contains(actual.FullName!, error.Message);
    }

    public class BaseArgs { public int Value { get; set; } }
    public sealed class DerivedArgs : BaseArgs { public int Extra { get; set; } }
    public sealed class DerivedList : List<BaseArgs>;
    public sealed class UnsupportedConstructorArgs(object value)
    {
        public string Value { get; } = (string)value;
    }
    public sealed class Container
    {
        public BaseArgs? Child { get; set; }
        public BaseArgs[] Items { get; set; } = [];
        public List<BaseArgs> List { get; set; } = [];
    }

    public sealed class CountingArgs
    {
        private int reads;
        private int value;
        public int Value { get { reads++; return value; } set { this.value = value; } }
        public int ReadCount() => reads;
    }
}
