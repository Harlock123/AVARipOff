using AVARipOff.Engine;

namespace AVARipOff.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaripoff-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A wave in play with its raiders cleared away but for one waiting off the screen, so it isn't over.</summary>
    private static GameEngine Game(int wave = 1, bool calm = true, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = calm };
        g.Go(wave);
        g.Raiders.Clear();
        g.PutRaider(RaiderKind.Runner, -100, -100).Delay = 9999;
        return g;
    }

    /// <summary>A live wave like that: the raiders put there move, hook, tow and fire.</summary>
    private static GameEngine Live(int wave = 1, int seed = 7) => Game(wave, calm: false, seed);

    private static readonly Controls FireButton = new(Fire: true);

    private static Raider Put(GameEngine g, RaiderKind kind, double x, double y) =>
        g.PutRaider(kind, x, y, Math.Atan2(Field.CY - y, Field.CX - x));

    // ───────────────────────────── the depot and the tank ─────────────────────────────

    [Fact]
    public void Eight_canisters_stand_in_a_ring_round_the_depot_with_the_tank_in_the_middle()
    {
        var g = Game();
        Assert.Equal(GameEngine.CanisterCount, g.Canisters.Count);
        Assert.All(g.Canisters, c => Assert.Equal(GameEngine.DepotRadius, Field.Distance(c.X, c.Y, Field.CX, Field.CY), 6));
        Assert.Equal((Field.CX, Field.CY), (g.Tank.X, g.Tank.Y));
    }

    [Fact]
    public void The_tank_turns_speeds_up_coasts_reverses_slower_and_stays_on_the_screen()
    {
        var g = Game();
        double a = g.Tank.Angle;
        g.Run(30, new Controls(Turn: 1));
        Assert.Equal(a + GameEngine.TurnRate * 0.5, g.Tank.Angle, 3);
        g.PutTank(Field.CX, Field.CY, Math.PI);
        g.Run(60, new Controls(Drive: 1));
        Assert.Equal(GameEngine.MaxSpeed, g.Tank.Speed, 3);
        g.Idle(60);
        Assert.Equal(0, g.Tank.Speed);
        g.Run(60, new Controls(Drive: -1));
        Assert.Equal(-GameEngine.ReverseSpeed, g.Tank.Speed, 3);
        g.Run(60 * 6, new Controls(Drive: 1));
        Assert.Equal(Tank.Radius, g.Tank.X, 3);
    }

    [Fact]
    public void Shots_fly_from_the_gun_two_at_a_time_and_only_so_far()
    {
        var g = Game();
        g.Run(1, FireButton);
        var s = Assert.Single(g.Shells);
        Assert.True(s.Y < g.Tank.Y - 10);
        Assert.Equal(-GameEngine.ShellSpeed, s.Vy, 3);
        int most = 0;
        for (int i = 0; i < 40; i++) { g.Run(1, FireButton); most = Math.Max(most, g.Shells.Count); }
        Assert.Equal(GameEngine.MaxShells, most);
        g.Idle((int)(GameEngine.ShellLife * 60) + 2);
        Assert.Empty(g.Shells);
    }

    // ───────────────────────────── the raiders ─────────────────────────────

    [Theory]
    [InlineData(RaiderKind.Runner, 1, 50)]
    [InlineData(RaiderKind.Gunner, 3, 225)]
    [InlineData(RaiderKind.Hunter, 6, 600)]
    public void A_shot_raider_is_worth_more_the_more_dangerous_it_is_and_every_wave(RaiderKind kind, int wave, int points)
    {
        var g = Game(wave);
        g.PutRaider(kind, Field.CX, Field.CY - 250);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.Single(g.Raiders);
        Assert.Equal(points, g.Score);
    }

    [Fact]
    public void A_raider_hooks_the_nearest_canister_and_tows_it_off_the_screen_for_good()
    {
        var g = Live();
        g.PutTank(Field.CX, Field.Bottom - 40);
        g.Tank.Safe = 1000;
        var r = Put(g, RaiderKind.Runner, 80, Field.CY);
        var nearest = g.Canisters.OrderBy(c => Field.Distance(c.X, c.Y, 80, Field.CY)).First();
        int t = 0;
        while (!r.Towing && t++ < 600) g.Idle(1);
        Assert.Same(nearest, r.Target);
        Assert.Same(r, nearest.Carrier);
        // Slower with it in tow, and the canister trails along behind.
        Assert.Equal(g.RaiderSpeed * GameEngine.SpeedOf(RaiderKind.Runner) * GameEngine.TowSpeed, g.SpeedOf(r), 6);
        g.Idle(60);
        Assert.Equal(Raider.Tow, Field.Distance(r.X, r.Y, nearest.X, nearest.Y), 3);
        t = 0;
        while (g.Canisters.Contains(nearest) && t++ < 60 * 20) g.Idle(1);
        Assert.Equal(GameEngine.CanisterCount - 1, g.Canisters.Count);
        Assert.DoesNotContain(r, g.Raiders);
    }

    [Fact]
    public void Shooting_a_raider_with_a_canister_in_tow_drops_it_where_it_is_and_another_can_take_it()
    {
        var g = Live();
        g.Tank.Safe = 1000;
        var r = Put(g, RaiderKind.Runner, Field.CX, Field.CY - 200);
        var c = g.Canisters[0];
        c.X = r.X;
        c.Y = r.Y + 10;
        c.Carrier = r;
        r.Target = c;
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.DoesNotContain(r, g.Raiders);
        Assert.Null(c.Carrier);
        Assert.InRange(Field.Distance(c.X, c.Y, Field.CX, Field.CY), 120, 260);
        var next = Put(g, RaiderKind.Runner, Field.CX + 30, Field.Top - 10);
        int t = 0;
        while (!next.Towing && t++ < 600) g.Idle(1);
        Assert.Same(c, next.Target);
    }

    [Fact]
    public void Raiders_go_for_different_canisters()
    {
        var g = Live();
        g.Tank.Safe = 1000;
        var a = Put(g, RaiderKind.Runner, 80, Field.CY);
        var b = Put(g, RaiderKind.Runner, 80, Field.CY + 20);
        g.Idle(2);
        Assert.NotNull(a.Target);
        Assert.NotNull(b.Target);
        Assert.NotSame(a.Target, b.Target);
    }

    [Fact]
    public void Gunners_fire_at_the_tank_in_front_of_them_and_runners_never_do()
    {
        var g = Live(3);
        g.PutTank(Field.CX, Field.CY);
        g.Canisters.Clear();
        g.Canisters.Add(new Canister { X = 40, Y = 40 + Field.Top });
        var run = g.PutRaider(RaiderKind.Runner, Field.CX - 250, Field.CY, 0);
        g.Idle(20);
        Assert.DoesNotContain(g.Shells, s => s.Hostile);
        g.Raiders.Remove(run);
        g.PutRaider(RaiderKind.Gunner, Field.CX - 250, Field.CY, 0);
        int t = 0;
        while (!g.Shells.Any(s => s.Hostile) && t++ < 120) g.Idle(1);
        var shell = Assert.Single(g.Shells, s => s.Hostile);
        Assert.True(shell.Vx > 0);
        t = 0;
        while (g.Tank.Alive && t++ < 120) g.Idle(1);
        Assert.False(g.Tank.Alive);
        Assert.Equal("shot", g.Cause);
    }

    [Fact]
    public void A_hunter_rams_the_tank_and_both_go_and_the_tank_comes_back_at_the_depot_safe()
    {
        var g = Live(6);
        g.PutTank(Field.CX - 200, Field.CY);
        g.PutRaider(RaiderKind.Hunter, Field.CX + 200, Field.CY, Math.PI).FireIn = 999;
        int t = 0;
        while (g.Tank.Alive && t++ < 600) g.Idle(1);
        Assert.False(g.Tank.Alive);
        Assert.Equal("rammed", g.Cause);
        Assert.Single(g.Raiders);
        Assert.Equal(600, g.Score);
        g.Idle((int)(GameEngine.RespawnSeconds * 60) + 2);
        Assert.True(g.Tank.Alive);
        Assert.Equal((Field.CX, Field.CY), (g.Tank.X, g.Tank.Y));
        Assert.True(g.Tank.Safe > 0);
    }

    [Fact]
    public void A_new_tank_waits_while_a_raider_is_at_the_depot()
    {
        var g = Game();
        g.Raiders.Add(new Raider { X = Field.CX + 30, Y = Field.CY });
        g.Shells.Add(new Shell { X = g.Tank.X - 30, Y = g.Tank.Y, Vx = 300, Life = 1, Hostile = true });
        g.Idle(10);
        Assert.False(g.Tank.Alive);
        g.Idle((int)(GameEngine.RespawnSeconds * 60) + 30);
        Assert.False(g.Tank.Alive);
        g.Raiders.RemoveAll(r => r.Delay <= 0);
        g.Idle(2);
        Assert.True(g.Tank.Alive);
    }

    [Fact]
    public void Each_wave_brings_more_raiders_together_from_every_side_and_more_dangerous_kinds()
    {
        int Count(GameEngine g, RaiderKind k) => g.Raiders.Count(r => r.Kind == k);
        var one = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        one.Go(1);
        Assert.Equal(one.RaidersPerWave, one.Raiders.Count);
        Assert.Equal(one.Raiders.Count, Count(one, RaiderKind.Runner));
        // Off the screen, from more than one side, and close behind one another.
        Assert.All(one.Raiders, r => Assert.False(Field.Inside(r.X, r.Y)));
        Assert.True(one.Raiders.Select(r => r.X < 0 ? 0 : r.X > Field.Width ? 1 : r.Y < Field.Top ? 2 : 3).Distinct().Count() >= 3);
        Assert.True(one.Raiders.Max(r => r.Delay) < 1.5);
        var three = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        three.Go(3);
        Assert.True(Count(three, RaiderKind.Gunner) >= 1);
        Assert.Equal(0, Count(three, RaiderKind.Hunter));
        var eight = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        eight.Go(8);
        Assert.True(Count(eight, RaiderKind.Hunter) >= 1);
        Assert.True(eight.Raiders.Count > one.Raiders.Count);
        Assert.True(eight.RaiderSpeed > one.RaiderSpeed);
        Assert.True(eight.RaiderFireGap < three.RaiderFireGap);
    }

    // ───────────────────────────── waves and the whole game ─────────────────────────────

    [Fact]
    public void A_wave_held_pays_for_the_fuel_left_and_the_next_wave_keeps_what_is_left()
    {
        var g = Game(2);
        g.Raiders.Clear();
        g.Canisters.RemoveAt(0);
        g.PutRaider(RaiderKind.Runner, Field.CX, Field.CY - 250);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(7 * 25 * 2, g.Bonus);
        int score = g.Score;
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(score + g.Bonus, g.Score);
        Assert.Equal(3, g.Wave);
        Assert.Equal(7, g.Canisters.Count);
    }

    [Fact]
    public void The_last_canister_towed_away_ends_the_game()
    {
        var g = Live();
        g.Tank.Safe = 1000;
        g.PutTank(Field.CX, Field.Bottom - 30);
        while (g.Canisters.Count > 1) g.Canisters.RemoveAt(0);
        Put(g, RaiderKind.Runner, 80, Field.CY);
        int t = 0;
        while (g.Phase != Phase.Lost && t++ < 60 * 30) g.Idle(1);
        Assert.Equal(Phase.Lost, g.Phase);
        Assert.Empty(g.Canisters);
        g.Idle((int)(GameEngine.LostSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_alarm_sounds_while_a_canister_is_in_tow()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder()));
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Go(1);
        g.Raiders.Clear();
        g.PutRaider(RaiderKind.Runner, -100, -100).Delay = 9999;
        g.Tank.Safe = 1000;
        var r = Put(g, RaiderKind.Runner, 80, Field.CY);
        int t = 0;
        while (!r.Towing && t++ < 600) g.Idle(1);
        g.Idle(1);
        Assert.Contains(Sfx.Hook, heard);
        Assert.Equal(Sfx.Music, heard.Last(s => s is Sfx.Music or Sfx.MusicStop));
        g.Raiders.Remove(r);
        r.Target!.Carrier = null;
        g.Idle(1);
        Assert.Equal(Sfx.MusicStop, heard.Last(s => s is Sfx.Music or Sfx.MusicStop));
    }

    [Fact]
    public void A_high_score_asks_for_initials_and_goes_in_the_table()
    {
        var g = Live();
        g.SetScore(99999);
        g.Canisters.Clear();
        g.Idle(1);
        Assert.Equal(Phase.Lost, g.Phase);
        g.Idle((int)(GameEngine.LostSeconds * 60) + 60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "GAS") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("GAS", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var waves = new List<int>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            waves.Add(g.Wave);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal(new[] { 1, 2 }, waves);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(1, g.Wave);
        Assert.Equal(GameEngine.CanisterCount, g.Canisters.Count);
    }

    [Fact]
    public void The_computer_player_holds_the_first_wave()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        for (int i = 0; i < 60 * 60 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on wave {g.Wave}, {g.Raiders.Count} raiders left");
        Assert.True(g.Canisters.Count >= GameEngine.CanisterCount - 1);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(4);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Canisters.Count} {g.Tank.X:0.00} {g.Tank.Y:0.00} {g.Raiders.Count} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Live();
        g.Run(30, new Controls(Drive: 1));
        double y = g.Tank.Y;
        g.Pause();
        g.Idle(120);
        Assert.Equal(y, g.Tank.Y);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
