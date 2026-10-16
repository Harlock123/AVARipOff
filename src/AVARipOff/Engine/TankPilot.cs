namespace AVARipOff.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It picks the raider that matters most: one towing a
/// canister away, the sooner it'll be off the screen the more; a hunter coming for the tank; then whichever is
/// nearest the fuel. It turns to lead it and fires once it's lined up, driving closer only when it's out of range
/// (and back towards the depot when there's nothing to shoot). When a shot or a raider is about to hit it, it tries
/// a few ways of driving and takes the one that gets it furthest out of the way.
/// </summary>
public sealed class TankPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>It thinks again about its target only every so often.</summary>
    public const int LookEvery = 6;

    private int _tick, _dodging;
    private Raider? _target;
    private Controls _dodge;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var k = g.Tank;
        if (g.Phase is not (Phase.Play or Phase.Start) || !k.Alive) { Doing = "waiting"; _target = null; return default; }
        if (_dodging > 0) _dodging--;
        else if (_tick % 5 == 0 && Threat(g, k, default) is { } miss && miss < Tank.Radius + 8) { _dodge = Dodge(g); _dodging = 10; }
        if (_tick % LookEvery == 0 || _target is not { Dead: false } || !g.Raiders.Contains(_target)) _target = Choose(g);
        if (_dodging > 0)
        {
            Doing = "dodging";
            return _dodge with { Fire = Lined(g, k) };
        }
        if (_target == null)
        {
            // Nothing to shoot: back to the depot, and face out.
            double home = Field.Distance(k.X, k.Y, Field.CX, Field.CY);
            if (home > 90)
            {
                double want = Math.Atan2(Field.CY - k.Y, Field.CX - k.X), off = Math.IEEERemainder(want - k.Angle, Math.PI * 2);
                Doing = "going home";
                return new Controls(Math.Abs(off) < 0.08 ? 0 : Math.Sign(off), Math.Abs(off) < 0.6 ? 1 : 0);
            }
            Doing = "guarding";
            return default;
        }
        var (ax, ay) = Lead(g, k, _target);
        double aim = Math.Atan2(ay - k.Y, ax - k.X), diff = Math.IEEERemainder(aim - k.Angle, Math.PI * 2);
        int turn = Math.Abs(diff) < 0.03 ? 0 : Math.Sign(diff);
        double dist = Field.Distance(k.X, k.Y, ax, ay), reach = GameEngine.ShellSpeed * GameEngine.ShellLife;
        int drive = 0;
        if (dist > reach * 0.8 && Math.Abs(diff) < 0.6) drive = 1;
        else if (dist < 80) drive = -1;
        bool fire = Lined(g, k);
        Doing = fire ? "firing" : drive > 0 ? "closing in" : "aiming";
        return new Controls(turn, drive, fire);
    }

    /// <summary>Whether a shot now would hit the target, near enough.</summary>
    private bool Lined(GameEngine g, Tank k)
    {
        if (_target == null || k.FireIn > 0) return false;
        var (ax, ay) = Lead(g, k, _target);
        double dist = Field.Distance(k.X, k.Y, ax, ay), off = Math.IEEERemainder(Math.Atan2(ay - k.Y, ax - k.X) - k.Angle, Math.PI * 2);
        return Math.Abs(off) < Math.Atan2(Raider.Radius * 0.8, Math.Max(dist, 1)) + 0.015 && dist < GameEngine.ShellSpeed * GameEngine.ShellLife * 0.95;
    }

    /// <summary>Where a raider will be when a shot gets there.</summary>
    private static (double X, double Y) Lead(GameEngine g, Tank k, Raider r)
    {
        double v = g.SpeedOf(r), vx = Math.Cos(r.Angle) * v, vy = Math.Sin(r.Angle) * v;
        double time = Field.Distance(k.X, k.Y, r.X, r.Y) / GameEngine.ShellSpeed;
        for (int i = 0; i < 3; i++) time = Field.Distance(k.X, k.Y, r.X + vx * time, r.Y + vy * time) / GameEngine.ShellSpeed;
        return (r.X + vx * time, r.Y + vy * time);
    }

    /// <summary>The raider that most needs shooting, allowing for how far it has to turn to face it.</summary>
    private static Raider? Choose(GameEngine g)
    {
        var k = g.Tank;
        Raider? best = null;
        double cost = double.MaxValue;
        foreach (var r in g.Raiders)
        {
            if (r.Delay > 0 || !Field.Inside(r.X, r.Y, -10)) continue;
            double d = Field.Distance(k.X, k.Y, r.X, r.Y), v = g.SpeedOf(r);
            double turn = Math.Abs(Math.IEEERemainder(Math.Atan2(r.Y - k.Y, r.X - k.X) - k.Angle, Math.PI * 2)) / GameEngine.TurnRate;
            double urgency;
            if (r.Towing) urgency = Field.ToEdge(r.X, r.Y) / v * 0.5;
            else if (r.Kind == RaiderKind.Hunter) urgency = d / v * 0.6;
            else urgency = r.Target is { } t ? Field.Distance(r.X, r.Y, t.X, t.Y) / v : 5;
            double c = urgency + turn + d / GameEngine.ShellSpeed;
            if (c < cost) { cost = c; best = r; }
        }
        return best;
    }

    /// <summary>How close the nearest shot or raider will come, driving like this for the next while (null if none is about).</summary>
    private static double? Threat(GameEngine g, Tank k, Controls c, bool drive = false)
    {
        var shells = g.Shells.Where(s => s.Hostile).Select(s => (s.X, s.Y, s.Vx, s.Vy, R: 3.0)).ToList();
        foreach (var r in g.Raiders.Where(r => r.Delay <= 0))
        {
            double v = g.SpeedOf(r);
            shells.Add((r.X, r.Y, Math.Cos(r.Angle) * v, Math.Sin(r.Angle) * v, Raider.Radius));
        }
        if (shells.Count == 0) return null;
        var me = new Tank { X = k.X, Y = k.Y, Angle = k.Angle, Speed = k.Speed };
        double? miss = null;
        for (int t = 1; t <= 40; t++)
        {
            if (drive) GameEngine.Move(me, c, Field.Step);
            foreach (var (x, y, vx, vy, r) in shells)
            {
                double d = Field.Distance(x + vx * t * Field.Step, y + vy * t * Field.Step, me.X, me.Y) - r + 3;
                if (miss is not { } m || d < m) miss = d;
            }
        }
        return miss;
    }

    /// <summary>The way of driving, of a handful, that takes it furthest from the trouble.</summary>
    private static Controls Dodge(GameEngine g)
    {
        Controls best = default;
        double most = double.MinValue;
        foreach (int drive in new[] { 1, -1 })
        foreach (int turn in new[] { 0, -1, 1 })
        {
            var c = new Controls(turn, drive);
            double miss = Threat(g, g.Tank, c, drive: true) ?? 999;
            if (miss > most + 0.5) { most = miss; best = c; }
        }
        return best;
    }
}
