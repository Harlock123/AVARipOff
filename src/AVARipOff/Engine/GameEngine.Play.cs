namespace AVARipOff.Engine;

public sealed partial class GameEngine
{
    public const double TurnRate = 2.6, Accel = 480, Brake = 900, Coast = 420, MaxSpeed = 210, ReverseSpeed = 110;
    public const double ShellSpeed = 560, ShellLife = 0.75, FireGap = 0.25;
    public const int MaxShells = 2, CanisterCount = 8;
    public const double DepotRadius = 40, TowSpeed = 0.55;
    public const double StartSeconds = 2, RespawnSeconds = 2.5, SafeSeconds = 2, ClearSeconds = 2.6, LostSeconds = 3;

    /// <summary>The wave: 1 for the first.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public Tank Tank { get; } = new();
    public List<Shell> Shells { get; } = [];
    public List<Canister> Canisters { get; } = [];
    public List<Raider> Raiders { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public TankPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>For tests: the raiders hold still and hold their fire, and none come in.</summary>
    internal bool Calm { get; set; }

    public int RaidersPerWave => Math.Min(10, 3 + Wave / 2);
    public double RaiderSpeed => Math.Min(215, 130 + 8 * (Wave - 1));
    public double RaiderTurn => Math.Min(3.4, 2.6 + 0.05 * (Wave - 1));
    public double RaiderFireGap => Math.Max(1.1, 2.6 - 0.1 * (Wave - 1));
    public double RaiderShellSpeed => Math.Min(420, 280 + 10 * (Wave - 1));

    /// <summary>What a raider is worth: more for the dangerous kinds, and more every wave.</summary>
    public int PointsFor(RaiderKind kind) => (kind switch { RaiderKind.Runner => 50, RaiderKind.Gunner => 75, _ => 100 }) * Wave;

    public static double SpeedOf(RaiderKind kind) => kind switch { RaiderKind.Runner => 1.15, RaiderKind.Hunter => 1.1, _ => 1 };

    /// <summary>How fast a raider is going: slower when it has a canister in tow.</summary>
    public double SpeedOf(Raider r) => RaiderSpeed * SpeedOf(r.Kind) * (r.Towing ? TowSpeed : 1);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Wave = 1;
        Wreckage.Clear();
        Raiders.Clear();
        Shells.Clear();
        Canisters.Clear();
        for (int i = 0; i < CanisterCount; i++)
        {
            double a = i * Math.PI * 2 / CanisterCount;
            Canisters.Add(new Canister { X = Field.CX + Math.Cos(a) * DepotRadius, Y = Field.CY + Math.Sin(a) * DepotRadius });
        }
        PlaceTank();
        SetPhase(Phase.Start);
    }

    private void PlaceTank()
    {
        var k = Tank;
        k.X = Field.CX;
        k.Y = Field.CY;
        k.Angle = -Math.PI / 2;
        k.Speed = 0;
        k.Alive = true;
        k.Safe = SafeSeconds;
        k.FireIn = 0;
    }

    /// <summary>A wave: its raiders wait off the edges and come in one after another.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Raiders.Clear();
        Shells.Clear();
        int n = RaidersPerWave;
        int gunners = wave >= 3 ? Math.Min(n - 1, (wave - 1) / 2) : 0;
        int hunters = wave >= 5 ? Math.Min(Math.Min(2, (wave - 3) / 2), n - 1 - gunners) : 0;
        var kinds = Enumerable.Repeat(RaiderKind.Runner, n - gunners - hunters)
            .Concat(Enumerable.Repeat(RaiderKind.Gunner, gunners)).Concat(Enumerable.Repeat(RaiderKind.Hunter, hunters))
            .OrderBy(_ => _rng.Next()).ToList();
        // They come in together, from every side.
        int side = _rng.Next(4);
        for (int i = 0; i < kinds.Count; i++)
        {
            var (x, y) = EdgePoint((side + i) % 4);
            Raiders.Add(new Raider
            {
                Kind = kinds[i], X = x, Y = y, Angle = Math.Atan2(Field.CY - y, Field.CX - x),
                Delay = StartSeconds + 0.3 * i, Weave = _rng.Next(2) == 0 ? 1 : -1, Phase = _rng.NextDouble() * Math.PI * 2,
                FireIn = RaiderFireGap * (0.5 + _rng.NextDouble()),
            });
        }
        if (!Tank.Alive) { PlaceTank(); Tank.Back = 0; }
        SetPhase(Phase.Start);
        Say($"WAVE {wave}", Canisters.Count < CanisterCount ? $"{Canisters.Count} CANISTERS LEFT" : null, StartSeconds + 0.3);
        Play(Sfx.WaveStart);
    }

    /// <summary>A point just off one of the edges (0 left, 1 right, 2 top, 3 bottom), anywhere along it.</summary>
    private (double X, double Y) EdgePoint(int side) => side switch
    {
        0 => (-24, Field.Top + 30 + _rng.NextDouble() * (Field.Bottom - Field.Top - 60)),
        1 => (Field.Width + 24, Field.Top + 30 + _rng.NextDouble() * (Field.Bottom - Field.Top - 60)),
        2 => (30 + _rng.NextDouble() * (Field.Width - 60), Field.Top - 24),
        _ => (30 + _rng.NextDouble() * (Field.Width - 60), Field.Bottom + 24),
    };

    private void Simulate(double dt, Controls c)
    {
        MoveDebris(dt);
        switch (Phase)
        {
            case Phase.Start:
                Drive(dt, c);
                MoveRaiders(dt);
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Drive(dt, c);
                Fire(c);
                MoveRaiders(dt);
                MoveShells(dt);
                Crashes();
                Music(Raiders.Any(r => r.Towing));
                if (Canisters.Count == 0) Lost();
                else if (Raiders.Count == 0) Clear();
                break;
            case Phase.Clear:
                Drive(dt, c);
                MoveShells(dt);
                if (PhaseTime >= 1 && PhaseTime - dt < 1 && Bonus > 0) Score += Bonus;
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
            case Phase.Lost:
                MoveRaiders(dt);
                MoveShells(dt);
                if (PhaseTime >= LostSeconds) { GameOver(); return; }
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the tank ─────────────────────────────

    /// <summary>One step of a tank's driving: turning on the spot, speeding up and slowing down, kept on the screen.</summary>
    public static void Move(Tank k, Controls c, double dt)
    {
        k.Angle = Math.IEEERemainder(k.Angle + c.Turn * TurnRate * dt, Math.PI * 2);
        if (c.Drive > 0) k.Speed = Math.Min(MaxSpeed, k.Speed + (k.Speed < 0 ? Brake : Accel) * dt);
        else if (c.Drive < 0) k.Speed = Math.Max(-ReverseSpeed, k.Speed - (k.Speed > 0 ? Brake : Accel) * dt);
        else k.Speed = k.Speed > 0 ? Math.Max(0, k.Speed - Coast * dt) : Math.Min(0, k.Speed + Coast * dt);
        double x = k.X + Math.Cos(k.Angle) * k.Speed * dt, y = k.Y + Math.Sin(k.Angle) * k.Speed * dt;
        double cx = Math.Clamp(x, Tank.Radius, Field.Width - Tank.Radius), cy = Math.Clamp(y, Field.Top + Tank.Radius, Field.Bottom - Tank.Radius);
        if (cx != x || cy != y) k.Speed *= 0.9;
        k.X = cx;
        k.Y = cy;
    }

    private void Drive(double dt, Controls c)
    {
        var k = Tank;
        if (!k.Alive)
        {
            // Back at the depot once the way is clear.
            if ((k.Back -= dt) <= 0 && Phase == Phase.Play && !Raiders.Any(r => Field.Distance(r.X, r.Y, Field.CX, Field.CY) < 70))
            {
                PlaceTank();
                Play(Sfx.Respawn);
            }
            return;
        }
        k.Safe = Math.Max(0, k.Safe - dt);
        k.FireIn -= dt;
        Move(k, c, dt);
    }

    private void Fire(Controls c)
    {
        var k = Tank;
        if (!c.Fire || k.FireIn > 0 || !k.Alive) return;
        if (Shells.Count(s => !s.Hostile) >= MaxShells) return;
        k.FireIn = FireGap;
        double fx = Math.Cos(k.Angle), fy = Math.Sin(k.Angle);
        Shells.Add(new Shell { X = k.X + fx * 18, Y = k.Y + fy * 18, Vx = fx * ShellSpeed, Vy = fy * ShellSpeed, Life = ShellLife });
        Play(Sfx.Fire);
    }

    // ───────────────────────────── the raiders ─────────────────────────────

    private void MoveRaiders(double dt)
    {
        foreach (var r in Raiders.ToList())
        {
            if (r.Delay > 0) { r.Delay -= dt; continue; }
            if (Calm) continue;
            r.T += dt;
            double wx, wy;
            bool leaving = false;
            bool charge = r.Kind == RaiderKind.Gunner && Tank.Alive && !r.Towing && Field.Distance(r.X, r.Y, Tank.X, Tank.Y) < 300
                && !(r.Target is { } near && Field.Distance(r.X, r.Y, near.X, near.Y) < 120);
            if ((r.Kind == RaiderKind.Hunter || charge) && Tank.Alive && !r.Towing)
            {
                // Straight for the tank, weaving a little.
                double d = Field.Distance(r.X, r.Y, Tank.X, Tank.Y);
                (wx, wy) = Weave(r, Tank.X, Tank.Y, Math.Min(60, d * 0.2));
            }
            else if (r.Towing)
            {
                // Away from the depot, the shortest way off the screen.
                var (ex, ey) = Exit(r.X, r.Y);
                (wx, wy) = Weave(r, ex, ey, 40);
            }
            else
            {
                if (r.Target is not { Carrier: null } t || !Canisters.Contains(t)) r.Target = Choose(r);
                if (r.Target is { } target)
                {
                    double d = Field.Distance(r.X, r.Y, target.X, target.Y);
                    (wx, wy) = Weave(r, target.X, target.Y, Math.Min(130, d * 0.35));
                    if (d < Raider.Radius + Canister.Radius + 4)
                    {
                        target.Carrier = r;
                        Play(Sfx.Hook);
                    }
                }
                else
                {
                    // Nothing left to take: off it goes.
                    (wx, wy) = Exit(r.X, r.Y);
                    leaving = true;
                }
            }
            double want = Math.Atan2(wy - r.Y, wx - r.X), diff = Math.IEEERemainder(want - r.Angle, Math.PI * 2);
            r.Angle += Math.Clamp(diff, -RaiderTurn * dt, RaiderTurn * dt);
            double v = SpeedOf(r);
            r.X += Math.Cos(r.Angle) * v * dt;
            r.Y += Math.Sin(r.Angle) * v * dt;
            if (r.Towing && r.Target is { } towed)
            {
                // The canister on the end of its line, dragged along behind.
                double dx = towed.X - r.X, dy = towed.Y - r.Y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 1) { dx = -Math.Cos(r.Angle); dy = -Math.Sin(r.Angle); d = 1; }
                towed.X = r.X + dx / d * Raider.Tow;
                towed.Y = r.Y + dy / d * Raider.Tow;
            }
            if ((r.Towing || leaving) && !Field.Inside(r.X, r.Y, -26))
            {
                Raiders.Remove(r);
                if (r.Towing && r.Target is { } gone)
                {
                    Canisters.Remove(gone);
                    Play(Sfx.Escape);
                    Effects.Add(new Effect { Kind = EffectKind.Word, X = Math.Clamp(r.X, 80, Field.Width - 80), Y = Math.Clamp(r.Y, Field.Top + 30, Field.Bottom - 30), Text = "STOLEN", Life = 1.2, Colour = 0xFFFF6A5A });
                }
                continue;
            }
            Shoot(r, dt);
        }
    }

    /// <summary>A point on the way to somewhere, swung to one side and back as the raider weaves.</summary>
    private static (double X, double Y) Weave(Raider r, double x, double y, double amount)
    {
        double a = Math.Atan2(y - r.Y, x - r.X), off = Math.Sin(r.T * 1.6 + r.Phase) * amount * r.Weave;
        return (x - Math.Sin(a) * off, y + Math.Cos(a) * off);
    }

    /// <summary>Where a raider leaves by: straight out from the depot through where it is, to the edge and beyond.</summary>
    private static (double X, double Y) Exit(double x, double y)
    {
        double dx = x - Field.CX, dy = y - Field.CY, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        return (x + dx / d * 600, y + dy / d * 600);
    }

    /// <summary>The nearest canister nobody has hooked, and if it can, one that no other raider is after.</summary>
    private Canister? Choose(Raider r) =>
        Canisters.Where(c => c.Carrier == null)
            .OrderBy(c => Field.Distance(r.X, r.Y, c.X, c.Y) + (Raiders.Any(o => o != r && o.Target == c) ? 250 : 0))
            .FirstOrDefault();

    /// <summary>Gunners and hunters fire straight ahead when the tank is in front of them.</summary>
    private void Shoot(Raider r, double dt)
    {
        if (r.Kind == RaiderKind.Runner) return;
        r.FireIn -= dt;
        var k = Tank;
        if (!k.Alive || k.Safe > 0 || Phase != Phase.Play || r.FireIn > 0 || !Field.Inside(r.X, r.Y)) return;
        double d = Field.Distance(r.X, r.Y, k.X, k.Y);
        if (d > 380 || Math.Abs(Math.IEEERemainder(Math.Atan2(k.Y - r.Y, k.X - r.X) - r.Angle, Math.PI * 2)) > 0.25) return;
        r.FireIn = RaiderFireGap;
        double fx = Math.Cos(r.Angle), fy = Math.Sin(r.Angle);
        Shells.Add(new Shell { X = r.X + fx * 16, Y = r.Y + fy * 16, Vx = fx * RaiderShellSpeed, Vy = fy * RaiderShellSpeed, Life = 1.5, Hostile = true });
        Play(Sfx.RaiderFire);
    }

    // ───────────────────────────── shells, and what they hit ─────────────────────────────

    private void MoveShells(double dt)
    {
        foreach (var s in Shells)
        {
            for (int k = 0; k < 2 && s.Life > 0; k++)
            {
                s.X += s.Vx * dt / 2;
                s.Y += s.Vy * dt / 2;
                if (!Field.Inside(s.X, s.Y, -10)) { s.Life = 0; break; }
                if (s.Hostile) HitTank(s);
                else HitRaider(s);
            }
            s.Life -= dt;
        }
        Shells.RemoveAll(s => s.Life <= 0);
        Raiders.RemoveAll(r => r.Dead);
    }

    private void HitRaider(Shell s)
    {
        foreach (var r in Raiders)
        {
            if (r.Dead || r.Delay > 0 || Field.Distance(s.X, s.Y, r.X, r.Y) > Raider.Radius + 3) continue;
            s.Life = 0;
            Destroy(r);
            return;
        }
    }

    /// <summary>A raider destroyed: anything it was towing is dropped where it is.</summary>
    private void Destroy(Raider r)
    {
        r.Dead = true;
        Award(PointsFor(r.Kind), r.X, r.Y);
        Sparks(r.X, r.Y, Colour(r.Kind), 12, 1.1);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = r.X, Y = r.Y, Life = 0.5, Size = 1.4, Colour = Colour(r.Kind) });
        if (r.Towing && r.Target is { } c)
        {
            c.Carrier = null;
            Play(Sfx.Drop);
        }
        Play(Sfx.RaiderBlast);
    }

    private void HitTank(Shell s)
    {
        var k = Tank;
        if (!k.Alive || Phase != Phase.Play || Field.Distance(s.X, s.Y, k.X, k.Y) > Tank.Radius + 2) return;
        s.Life = 0;
        if (k.Safe > 0) return;
        Kill("shot");
    }

    /// <summary>A raider running into the tank: both go.</summary>
    private void Crashes()
    {
        var k = Tank;
        if (!k.Alive) return;
        foreach (var r in Raiders)
        {
            if (r.Dead || r.Delay > 0 || Field.Distance(r.X, r.Y, k.X, k.Y) > Raider.Radius + Tank.Radius - 2) continue;
            if (k.Safe > 0) continue;
            Destroy(r);
            Kill("rammed");
            break;
        }
        Raiders.RemoveAll(r => r.Dead);
    }

    /// <summary>The tank lost: there are no lives to count, but the raiders get a free run until it's back.</summary>
    private void Kill(string cause)
    {
        var k = Tank;
        Cause = cause;
        k.Alive = false;
        k.Speed = 0;
        k.Back = RespawnSeconds;
        Sparks(k.X, k.Y, TankColour, 16, 1.5);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = k.X, Y = k.Y, Life = 0.7, Size = 2, Colour = 0xFFFFFFFF });
        Play(Sfx.TankHit);
    }

    public const uint TankColour = 0xFFFFE85A, FuelColour = 0xFF6AFFB0;

    public static uint Colour(RaiderKind kind) => kind switch
    {
        RaiderKind.Runner => 0xFF6AD8FF, RaiderKind.Gunner => 0xFFFF7AD8, _ => 0xFFFF6A4A,
    };

    private void Clear()
    {
        Bonus = Canisters.Count * 25 * Wave;
        Shells.RemoveAll(s => s.Hostile);
        Music(false);
        Play(Sfx.WaveClear);
        Say($"WAVE {Wave} HELD", $"{Canisters.Count} CANISTERS - BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Lost()
    {
        Music(false);
        Say("THE FUEL IS GONE", null, LostSeconds);
        SetPhase(Phase.Lost);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 14, Text = points.ToString(), Life = 0.7, Colour = 0xFFFFE14A });
    }

    private void Sparks(double x, double y, uint colour, int n, double life)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 170;
            Wreckage.Add(new Debris
            {
                X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Angle = _rng.NextDouble() * 6, Spin = (_rng.NextDouble() - 0.5) * 12,
                Length = 5 + _rng.NextDouble() * 12, Life = life * (0.5 + _rng.NextDouble() * 0.5), Colour = colour,
            });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Angle += d.Spin * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a wave, past its number, with its raiders already on their way in.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        foreach (var r in Raiders) r.Delay = Math.Max(0, r.Delay - StartSeconds);
        Tank.Safe = 0;
        Message = SubMessage = null;
    }

    internal Raider PutRaider(RaiderKind kind, double x, double y, double angle = 0)
    {
        var r = new Raider { Kind = kind, X = x, Y = y, Angle = angle, FireIn = 0 };
        Raiders.Add(r);
        return r;
    }

    internal void PutTank(double x, double y, double angle = -Math.PI / 2)
    {
        PlaceTank();
        Tank.X = x;
        Tank.Y = y;
        Tank.Angle = angle;
        Tank.Safe = 0;
    }

    internal void SetScore(int n) => Score = n;
}
