// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet.Tests;

public sealed class ActorIdTests
{
    [Fact]
    public void RoundTripsThroughItsStringForm()
    {
        var id = new ActorId("BankAccountActor", "user-1");
        Assert.Equal("BankAccountActor/user-1", id.ToString());
        Assert.Equal(id, ActorId.Parse(id.ToString()));
    }

    [Fact]
    public void SplitsOnTheFirstSeparatorSoKeysMayContainMore()
    {
        // Hierarchical keys are the reason this matters: "Device/plant-3/line-2" is one device,
        // not a type called Device with a key that has been truncated.
        var id = ActorId.Parse("DeviceActor/plant-3/line-2");
        Assert.Equal("DeviceActor", id.Type);
        Assert.Equal("plant-3/line-2", id.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NoSeparator")]
    [InlineData("/missing-type")]
    [InlineData("MissingKey/")]
    public void RejectsAddressesThatCannotBeRouted(string value)
    {
        Assert.False(ActorId.TryParse(value, out _));
        Assert.Throws<FormatException>(() => ActorId.Parse(value));
    }

    [Fact]
    public void DefaultInstanceIsTheEmptyAddress()
    {
        Assert.True(ActorId.None.IsEmpty);
        Assert.False(new ActorId("A", "b").IsEmpty);
        Assert.Equal("<none>", ActorId.None.ToString());
    }

    [Fact]
    public void ForUsesTheActorTypeName()
    {
        Assert.Equal(new ActorId("CounterActor", "k"), ActorId.For<CounterActor>("k"));
    }
}
