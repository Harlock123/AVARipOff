using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVARipOff.Engine;
using AVARipOff.Views;
using Controls = AVARipOff.Engine.Controls;

namespace AVARipOff.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set RIPOFF_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaripoff-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("RIPOFF_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVARipOff-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 61850, 12);
        g.HighScores.Insert("AVA", 44200, 9);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A wave, and the computer at work in it until something worth showing.</summary>
    private static void Fight(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 60)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Tank.Alive && g.Tank.Safe <= 0 && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_raiders_come_in() => Fight(2, "raiders", g => g.Raiders.Count(r => r.Delay <= 0 && Field.Inside(r.X, r.Y, 40)) >= 3 && g.Shells.Any());

    [AvaloniaFact]
    public void A_canister_in_tow() => Fight(7, "towing", g => g.Raiders.Count(r => r.Towing && Field.Inside(r.X, r.Y, 60) && Field.Distance(r.X, r.Y, Field.CX, Field.CY) > 130) >= 2, seed: 2, seconds: 120);

    [AvaloniaFact]
    public void Gunners_firing() => Fight(5, "gunners", g => g.Shells.Count(s => s.Hostile) >= 2 && g.Raiders.Any(r => r.Kind == RaiderKind.Gunner && Field.Inside(r.X, r.Y, 40)), seconds: 120);

    [AvaloniaFact]
    public void A_hunter_on_the_tank() => Fight(7, "hunter", g => g.Raiders.Any(r => r.Kind == RaiderKind.Hunter && Field.Distance(r.X, r.Y, g.Tank.X, g.Tank.Y) < 150) && g.Wreckage.Count > 4, seconds: 120);

    [AvaloniaFact]
    public void A_wave_held()
    {
        var (w, c, g) = Open(4);
        g.Go(3);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > 0.5, 90));
        Shot(w, c, "held");
    }

    [AvaloniaFact]
    public void The_fuel_gone()
    {
        var (w, c, g) = Open(4);
        g.Go(9);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Lost && g.PhaseTime > 0.6, 400));
        Shot(w, c, "fuel-gone");
    }
}
