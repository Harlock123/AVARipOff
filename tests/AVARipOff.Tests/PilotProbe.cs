using AVARipOff.Engine;

namespace AVARipOff.Tests;

/// <summary>How the computer player gets on, wave by wave (set RIPOFF_PROBE to run it, for tuning: "1" for the pilot, "lazy" for one that just sits at the depot turning and firing).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("RIPOFF_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "ripoff-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            var deaths = new Dictionary<string, int>();
            var waves = new List<string>();
            var doing = new Dictionary<string, int>();
            bool alive = true;
            int cans = g.Canisters.Count;
            var stolen = new List<string>();
            int wave = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Wave <= 12 && g.State == GameState.Playing; t++)
            {
                var c = mode == "lazy" ? new Controls(Turn: 1, Fire: true) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (!g.Tank.Alive && alive) deaths[$"W{g.Wave} {g.Cause}"] = deaths.GetValueOrDefault($"W{g.Wave} {g.Cause}") + 1;
                if (g.Canisters.Count < cans) { stolen.Add($"W{g.Wave}"); cans = g.Canisters.Count; }
                alive = g.Tank.Alive;
                if (g.Wave != wave) { waves.Add($"W{wave}:{(t - start) / 60:0}s"); wave = g.Wave; start = t; }
            }
            output.WriteLine($"seed {seed}: wave {g.Wave} after {t / 60:0}s score {g.Score} | {string.Join(" ", waves)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderBy(d => d.Key).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine($"   canisters left {g.Canisters.Count}, stolen in {string.Join(" ", stolen)}");
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
