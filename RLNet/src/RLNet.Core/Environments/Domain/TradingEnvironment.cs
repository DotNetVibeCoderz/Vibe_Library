// RLNet - Reinforcement Learning for .NET
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using RLNet.Spaces;

namespace RLNet.Environments.Domain;

/// <summary>
/// Trade a single instrument over a synthetic price series, choosing when to buy, hold or sell.
/// </summary>
/// <remarks>
/// <para>
/// The price follows a geometric random walk with a mild mean-reverting component, so there is
/// a genuinely learnable edge — an agent that buys after a dip and sells after a run beats
/// buy-and-hold. A pure random walk would be unlearnable by construction, which makes for a
/// demonstration that always fails; the mean reversion is what turns this into a task.
/// </para>
/// <para>
/// <b>This is a teaching environment, not a trading system.</b> There is no slippage, no market
/// impact, no bid-ask spread beyond a flat commission, and the price process is nothing like a
/// real instrument. An agent that profits here says nothing whatsoever about live markets.
/// </para>
/// <para>
/// The observation is deliberately free of absolute levels — it carries returns over several
/// horizons, position and cash ratio, all scale-free. Feeding the raw price would let the agent
/// memorise the series it trained on and learn nothing transferable.
/// </para>
/// </remarks>
public sealed class TradingEnvironment : DiscreteEnvironmentBase
{
    private const float InitialCash = 10_000f;
    private const float Commission = 0.001f;   // 10 basis points per trade
    private const double Volatility = 0.015;
    private const double MeanReversion = 0.04;
    private const double BasePrice = 100.0;

    private readonly double[] _prices;
    private int _step;
    private float _cash;
    private int _shares;
    private float _netWorth;
    private float _previousNetWorth;

    /// <param name="seriesLength">Number of price points generated per episode.</param>
    public TradingEnvironment(int seriesLength = 512)
        : base(
            new BoxSpace(
                [-1f, -1f, -1f, -1f, 0f, 0f],
                [1f, 1f, 1f, 1f, 1f, 1f],
                ["Return (1)", "Return (5)", "Return (20)", "Deviation from mean", "Position", "Cash ratio"]),
            new DiscreteSpace(3, ["Hold", "Buy", "Sell"]),
            maxEpisodeSteps: seriesLength - 1)
    {
        _prices = new double[seriesLength];
        Reset();
    }

    public override string Name => "Trading";

    /// <summary>The generated price series for the current episode, for rendering.</summary>
    public ReadOnlySpan<double> Prices => _prices;

    /// <summary>Index into <see cref="Prices"/> of the current bar.</summary>
    public int CurrentStep => _step;

    /// <summary>Price of the current bar.</summary>
    public double CurrentPrice => _prices[_step];

    /// <summary>Uninvested cash.</summary>
    public float Cash => _cash;

    /// <summary>Shares held.</summary>
    public int Shares => _shares;

    /// <summary>Cash plus the marked-to-market value of the position.</summary>
    public float NetWorth => _netWorth;

    /// <summary>What holding from the first bar to now would have been worth, the benchmark to beat.</summary>
    public float BuyAndHoldValue => (float)(InitialCash * (_prices[_step] / _prices[0]));

    protected override void OnReset()
    {
        GeneratePrices();
        _step = 0;
        _cash = InitialCash;
        _shares = 0;
        _netWorth = InitialCash;
        _previousNetWorth = InitialCash;
    }

    private void GeneratePrices()
    {
        double price = BasePrice;
        double logMean = Math.Log(BasePrice);

        for (int i = 0; i < _prices.Length; i++)
        {
            _prices[i] = price;

            // Ornstein-Uhlenbeck in log space: the drift pulls the log price back toward its
            // long-run mean in proportion to how far it has strayed. That is the exploitable
            // structure, and its strength is what sets the difficulty of the task.
            double logPrice = Math.Log(price);
            double drift = MeanReversion * (logMean - logPrice);
            double shock = Random.NextGaussian() * Volatility;

            price = Math.Exp(logPrice + drift + shock);
        }
    }

    protected override void WriteObservation(Span<float> destination)
    {
        destination[0] = (float)ReturnOver(1);
        destination[1] = (float)ReturnOver(5);
        destination[2] = (float)ReturnOver(20);

        // How far the price sits from its rolling mean — the signal the mean-reverting process
        // actually rewards trading on.
        destination[3] = (float)Math.Clamp(DeviationFromMean(20), -1.0, 1.0);

        float positionValue = _shares * (float)_prices[_step];
        destination[4] = _netWorth > 0f ? positionValue / _netWorth : 0f;
        destination[5] = _netWorth > 0f ? _cash / _netWorth : 0f;
    }

    private double ReturnOver(int horizon)
    {
        int from = Math.Max(0, _step - horizon);
        if (from == _step) return 0.0;

        // Scaled by 10 so a typical few-percent move lands in a range a network can work with
        // without a normalisation layer, then clamped so an outlier cannot dominate the input.
        return Math.Clamp((_prices[_step] / _prices[from] - 1.0) * 10.0, -1.0, 1.0);
    }

    private double DeviationFromMean(int window)
    {
        int from = Math.Max(0, _step - window + 1);
        double sum = 0.0;
        for (int i = from; i <= _step; i++) sum += _prices[i];
        double mean = sum / (_step - from + 1);
        return (_prices[_step] / mean - 1.0) * 10.0;
    }

    protected override StepResult OnStep(int action)
    {
        double price = _prices[_step];

        switch (action)
        {
            case 1 when _cash >= price * (1 + Commission):
            {
                // Commit a fixed fraction of equity per trade rather than one share: one share
                // is a position size that shrinks to irrelevance as the account grows, so the
                // agent's decisions would stop mattering partway through the episode.
                int quantity = Math.Max(1, (int)(_netWorth * 0.25 / price));
                float cost = (float)(quantity * price * (1 + Commission));
                if (cost <= _cash)
                {
                    _cash -= cost;
                    _shares += quantity;
                }
                break;
            }

            case 2 when _shares > 0:
            {
                int quantity = Math.Max(1, _shares / 2);
                _cash += (float)(quantity * price * (1 - Commission));
                _shares -= quantity;
                break;
            }
        }

        _step++;
        double newPrice = _prices[_step];
        _netWorth = _cash + _shares * (float)newPrice;

        // Reward is the log return of equity, not its change. Log returns add over time, so the
        // discounted sum the agent maximises is the compound growth rate — which is the thing a
        // trader actually cares about. Raw profit would make a gain from 10,000 to 10,100 look
        // identical to one from 100,000 to 100,100.
        float reward = _previousNetWorth > 0f && _netWorth > 0f
            ? (float)Math.Log(_netWorth / _previousNetWorth) * 100f
            : -1f;

        _previousNetWorth = _netWorth;

        // Wiped out: no position, no cash worth trading. Ends the episode rather than letting it
        // run out with nothing to decide.
        bool terminated = _netWorth < InitialCash * 0.2f;
        if (terminated) reward -= 10f;

        return Advance(reward, terminated);
    }
}
