// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace ActorNet;

/// <summary>
/// Marks an interface as an actor's protocol, so the compiler can check calls to it.
/// </summary>
/// <remarks>
/// <para>
/// <c>AskAsync&lt;Balance&gt;(id, new GetBalance())</c> is three things that have to agree and
/// nothing that checks they do: the message, the response type, and the handler on the other side.
/// Get any of them wrong and the failure is an <see cref="AskTimeoutException"/> at runtime, on the
/// unlucky day the code path first runs.
/// </para>
/// <para>
/// An interface marked with this generates, at compile time:
/// </para>
/// <list type="bullet">
/// <item>a request record per method, and a reply record per method that returns a value;</item>
/// <item><c>&lt;Name&gt;Proxy.Of(system, key)</c>, which returns the interface;</item>
/// <item><c>&lt;Name&gt;ActorBase</c>, a <see cref="ReceiveActor"/> that implements the routing and
/// leaves the interface's methods abstract;</item>
/// <item><c>&lt;Name&gt;Protocol.Register(system)</c>, which registers every generated message.</item>
/// </list>
/// <para>
/// The generated records are ordinary messages on the same allow-list as hand-written ones, so a
/// proxy call across a node boundary is the same wire traffic it would have been by hand. What
/// changes is that a mismatch between caller and handler is now a compile error.
/// </para>
/// <example>
/// <code>
/// [ActorInterface]
/// public interface IBankAccount
/// {
///     Task&lt;decimal&gt; GetBalanceAsync();
///     Task DepositAsync(decimal amount, string reference);
/// }
///
/// public sealed class BankAccountActor : BankAccountActorBase
/// {
///     private decimal _balance;
///
///     public override Task&lt;decimal&gt; GetBalanceAsync() =&gt; Task.FromResult(_balance);
///     public override Task DepositAsync(decimal amount, string reference)
///     {
///         _balance += amount;
///         return Task.CompletedTask;
///     }
/// }
///
/// // and at the call site, checked by the compiler:
/// var account = BankAccountProxy.Of(system, "acct-1");
/// await account.DepositAsync(250m, "opening");
/// var balance = await account.GetBalanceAsync();
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class ActorInterfaceAttribute : Attribute
{
    /// <summary>
    /// Prefix for the generated message aliases. Defaults to the interface name without its
    /// leading <c>I</c>.
    /// </summary>
    /// <remarks>
    /// The alias is what travels on the wire and what a cross-language client addresses, so it is
    /// part of the protocol: renaming the interface after something is deployed would otherwise
    /// silently stop matching. Pin it here when that matters.
    /// </remarks>
    public string? Alias { get; set; }

    /// <summary>
    /// How long a generated ask waits, in seconds. Zero uses the system's default.
    /// </summary>
    public int TimeoutSeconds { get; set; }
}
