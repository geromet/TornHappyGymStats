namespace HappyGymStats.Core.War;

public sealed class ChainEngine(double a = ChainEngine.DefaultA, double b = ChainEngine.DefaultB)
{
    public const double DefaultA = 0.25;
    public const double DefaultB = 0.75;
    public const double DefaultBase = 11.0;
    private const int ExactLimit = 200_000;

    public static readonly (int Milestone, int Bonus, string Timer)[] BonusTable =
    [
        (10,      10,     "60 seconds"),
        (25,      20,     "150 seconds"),
        (50,      40,     "5 minutes"),
        (100,     80,     "10 minutes"),
        (250,     160,    "25 minutes"),
        (500,     320,    "50 minutes"),
        (1_000,   640,    "1 hour 40 minutes"),
        (2_500,   1_280,  "4 hours 10 minutes"),
        (5_000,   2_560,  "8 hours 20 minutes"),
        (10_000,  5_120,  "16 hours 40 minutes"),
        (25_000,  10_240, "1 day 17 hours 40 minutes"),
        (50_000,  20_480, "3 days 11 hours 20 minutes"),
        (100_000, 40_960, "6 days 22 hours 40 minutes"),
    ];

    public static readonly int[] Milestones = BonusTable.Select(x => x.Milestone).ToArray();

    /// <summary>
    /// The one-off point bonus credited to whoever lands each milestone-crossing hit
    /// (10, 20, 40, 80, 160, 320, 640, ...). Used by opponent scouting to detect wars whose
    /// score was inflated by a chain-milestone lump rather than sustained hitting.
    /// </summary>
    public static readonly int[] MilestoneBonuses = BonusTable.Select(x => x.Bonus).ToArray();

    private static readonly int[] _bonusPrefix;

    static ChainEngine()
    {
        var running = 0;
        _bonusPrefix = BonusTable.Select(x => { running += x.Bonus; return running; }).ToArray();
    }

    private readonly List<double> _sigmaPrefix = [0.0];

    private void ExtendPrefix(int n)
    {
        var total = _sigmaPrefix[^1];
        for (var i = _sigmaPrefix.Count; i <= n; i++)
            _sigmaPrefix.Add(total += a * Math.Log10(i) + b);
    }

    public double SigmaMult(int length)
    {
        if (length == 0) return 0.0;
        if (length <= ExactLimit)
        {
            if (length >= _sigmaPrefix.Count) ExtendPrefix(length);
            return _sigmaPrefix[length];
        }
        return a * LGamma(length + 1) / Math.Log(10) + b * length;
    }

    private static double LGamma(double n)
    {
        if (n <= 1) return 0;
        return (n - 0.5) * Math.Log(n) - n + 0.5 * Math.Log(2 * Math.PI)
               + 1.0 / (12 * n) - 1.0 / (360 * n * n * n);
    }

    public int CumBonus(int length)
    {
        if (length < Milestones[0]) return 0;
        var idx = Array.BinarySearch(Milestones, length);
        if (idx < 0) idx = ~idx - 1;
        return _bonusPrefix[idx];
    }

    public double ChainRespect(int length, double baseResp)
    {
        if (length <= 0) return 0.0;
        return baseResp * SigmaMult(length) + CumBonus(length);
    }

    public double ComboRespect(IReadOnlyList<int> legs, double baseResp)
        => legs.Sum(leg => ChainRespect(leg, baseResp));

    public List<double> CumulativeCurve(IReadOnlyList<int> legs, double baseResp)
    {
        var result = new List<double>();
        var banked = 0.0;
        foreach (var leg in legs)
        {
            for (var n = 1; n <= leg; n++)
                result.Add(banked + ChainRespect(n, baseResp));
            banked += ChainRespect(leg, baseResp);
        }
        return result;
    }

    public static List<ChainSplit> EnumerateSplits(
        ChainEngine model, int budget, double baseResp,
        int maxLegs = 6, bool spendAll = true,
        int? maxLeg = null, int minLeg = 1)
    {
        if (budget < 1) return [];
        var cap = maxLeg.HasValue ? Math.Min(budget, maxLeg.Value) : budget;
        var stones = Milestones.Where(m => m >= minLeg && m <= cap).OrderByDescending(x => x).ToArray();
        var seen = new HashSet<string>();
        var results = new List<ChainSplit>();

        void Emit(List<int> legs, int remaining)
        {
            var combo = new List<int>(legs);
            if (spendAll && remaining >= Math.Max(1, minLeg) && combo.Count < maxLegs && remaining <= cap)
                combo.Add(remaining);
            if (combo.Count == 0) return;
            var sorted = combo.OrderByDescending(x => x).ToArray();
            var key = string.Join(",", sorted);
            if (!seen.Add(key)) return;
            results.Add(new ChainSplit { Legs = sorted, Respect = model.ComboRespect(sorted, baseResp) });
        }

        void Walk(int start, int remaining, List<int> legs)
        {
            Emit(legs, remaining);
            if (legs.Count >= maxLegs) return;
            for (var i = start; i < stones.Length; i++)
            {
                if (stones[i] > remaining) continue;
                legs.Add(stones[i]);
                Walk(i, remaining - stones[i], legs);
                legs.RemoveAt(legs.Count - 1);
            }
        }

        Walk(0, budget, []);
        results.Sort((x, y) => y.Respect.CompareTo(x.Respect));
        for (var i = 0; i < results.Count; i++) results[i].Rank = i + 1;
        return results;
    }
}

public sealed class ChainSplit
{
    public int Rank { get; set; }
    public required int[] Legs { get; init; }
    public required double Respect { get; init; }

    public int Hits => Legs.Sum();
    public double PerHit => Hits > 0 ? Respect / Hits : 0;

    public string Name
    {
        get
        {
            var counts = Legs.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            return string.Join(" + ", counts.OrderByDescending(kv => kv.Key)
                .Select(kv => kv.Value > 1 ? $"{kv.Key:N0} x{kv.Value}" : $"{kv.Key:N0}"));
        }
    }
}
