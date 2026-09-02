// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments.Domain;

/// <summary>
/// Decide how much stock to reorder each day under uncertain demand and a delivery lag.
/// </summary>
/// <remarks>
/// <para>
/// A single-echelon inventory problem: hold too little and sales are lost, hold too much and
/// capital sits on a shelf. What makes it a reinforcement-learning problem rather than an
/// arithmetic one is the lead time — an order placed today arrives several days later, so the
/// agent must act on demand it cannot yet see, and the consequence of a decision only becomes
/// visible long after it was made. That delayed credit assignment is exactly what temporal
/// difference learning is for.
/// </para>
/// <para>
/// Demand is seasonal with noise, so a fixed reorder quantity cannot be optimal; the agent has
/// to read the phase of the season out of recent demand. There is a known good baseline to
/// compare against — the base-stock policy, which orders up to a fixed level every day — and
/// <see cref="BaseStockAction"/> computes it, so a training curve can be read against a
/// meaningful line rather than against zero.
/// </para>
/// </remarks>
public sealed class SupplyChainEnvironment : DiscreteEnvironmentBase
{
    private const int LeadTime = 3;
    private const int MaxInventory = 200;
    private const int MaxOrder = 60;
    private const float HoldingCost = 0.10f;    // per unit per day
    private const float StockoutCost = 1.50f;   // per unit of unmet demand
    private const float UnitMargin = 1.00f;     // earned per unit sold
    private const float OrderingCost = 5.00f;   // fixed cost of placing any order

    private readonly int[] _pipeline = new int[LeadTime]; // orders in transit, [0] arrives next
    private readonly int[] _recentDemand = new int[5];

    private int _inventory;
    private int _day;
    private float _cumulativeProfit;
    private int _lastDemand;
    private int _lastSold;
    private int _lastLost;

    /// <summary>The seven order sizes the agent can choose between.</summary>
    private static readonly int[] OrderSizes = [0, 5, 10, 20, 30, 45, MaxOrder];

    public SupplyChainEnvironment()
        : base(
            new BoxSpace(
                [0f, 0f, 0f, 0f, 0f, 0f, -1f, -1f],
                [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f],
                [
                    "Inventory", "In transit (1 day)", "In transit (2 days)", "In transit (3 days)",
                    "Mean recent demand", "Demand volatility", "Season (sin)", "Season (cos)",
                ]),
            new DiscreteSpace(OrderSizes.Length, ["Order 0", "Order 5", "Order 10", "Order 20", "Order 30", "Order 45", "Order 60"]),
            maxEpisodeSteps: 180)
    {
        Reset();
    }

    public override string Name => "SupplyChain";

    /// <summary>Units on the shelf.</summary>
    public int Inventory => _inventory;

    /// <summary>Units ordered but not yet delivered, nearest arrival first.</summary>
    public ReadOnlySpan<int> Pipeline => _pipeline;

    /// <summary>Demand seen on the last step.</summary>
    public int LastDemand => _lastDemand;

    /// <summary>Units actually sold on the last step.</summary>
    public int LastSold => _lastSold;

    /// <summary>Demand that went unmet on the last step.</summary>
    public int LastLostSales => _lastLost;

    /// <summary>Profit accumulated this episode.</summary>
    public float CumulativeProfit => _cumulativeProfit;

    /// <summary>Day within the episode.</summary>
    public int Day => _day;

    /// <summary>Expected demand on a given day, before noise. Seasonal with a 60-day cycle.</summary>
    public static double ExpectedDemand(int day) => 20.0 + 12.0 * Math.Sin(2.0 * Math.PI * day / 60.0);

    /// <summary>
    /// The order a base-stock policy would place: top the inventory position up to a fixed
    /// level covering demand over the lead time plus a safety margin. A competent agent should
    /// match or beat this.
    /// </summary>
    public int BaseStockAction()
    {
        int inTransit = 0;
        for (int i = 0; i < LeadTime; i++) inTransit += _pipeline[i];

        double target = ExpectedDemand(_day) * (LeadTime + 1) * 1.2;
        int shortfall = (int)Math.Round(target - (_inventory + inTransit));

        // Snap to the nearest available order size, since the action space is discrete.
        int best = 0;
        for (int i = 1; i < OrderSizes.Length; i++)
            if (Math.Abs(OrderSizes[i] - shortfall) < Math.Abs(OrderSizes[best] - shortfall))
                best = i;
        return best;
    }

    protected override void OnReset()
    {
        _day = 0;
        _inventory = 40;
        Array.Clear(_pipeline);
        _cumulativeProfit = 0f;
        _lastDemand = _lastSold = _lastLost = 0;

        // Seed the demand history with plausible values so the first few observations are not a
        // block of zeros the agent has to learn to ignore.
        for (int i = 0; i < _recentDemand.Length; i++)
            _recentDemand[i] = (int)ExpectedDemand(0);
    }

    protected override void WriteObservation(Span<float> destination)
    {
        destination[0] = _inventory / (float)MaxInventory;
        for (int i = 0; i < LeadTime; i++)
            destination[1 + i] = _pipeline[i] / (float)MaxOrder;

        float mean = 0f;
        for (int i = 0; i < _recentDemand.Length; i++) mean += _recentDemand[i];
        mean /= _recentDemand.Length;

        float variance = 0f;
        for (int i = 0; i < _recentDemand.Length; i++)
        {
            float d = _recentDemand[i] - mean;
            variance += d * d;
        }

        destination[4] = mean / 50f;
        destination[5] = MathF.Sqrt(variance / _recentDemand.Length) / 20f;

        // The phase of the season goes in as a sine-cosine pair rather than a day counter, for
        // the same reason angles do elsewhere: it is periodic, and day 59 is adjacent to day 0.
        double phase = 2.0 * Math.PI * _day / 60.0;
        destination[6] = (float)Math.Sin(phase);
        destination[7] = (float)Math.Cos(phase);
    }

    protected override StepResult OnStep(int action)
    {
        int order = OrderSizes[action];

        // Today's delivery arrives before today's demand is served.
        _inventory = Math.Min(MaxInventory, _inventory + _pipeline[0]);
        for (int i = 0; i < LeadTime - 1; i++) _pipeline[i] = _pipeline[i + 1];
        _pipeline[LeadTime - 1] = order;

        // Poisson-like demand: seasonal mean with noise, floored at zero.
        double expected = ExpectedDemand(_day);
        int demand = Math.Max(0, (int)Math.Round(expected + Random.NextGaussian() * 4.0));

        int sold = Math.Min(demand, _inventory);
        int lost = demand - sold;
        _inventory -= sold;

        float profit =
            sold * UnitMargin
            - _inventory * HoldingCost
            - lost * StockoutCost
            - (order > 0 ? OrderingCost : 0f);

        _cumulativeProfit += profit;
        _lastDemand = demand;
        _lastSold = sold;
        _lastLost = lost;

        Array.Copy(_recentDemand, 1, _recentDemand, 0, _recentDemand.Length - 1);
        _recentDemand[^1] = demand;

        _day++;

        // Scaled to roughly unit magnitude. The agents' default learning rates assume rewards in
        // this range; leaving profit in currency units would need a different rate per currency.
        return Advance(profit / 20f, terminated: false);
    }
}
