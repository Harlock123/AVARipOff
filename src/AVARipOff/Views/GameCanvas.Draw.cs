using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVARipOff.Engine;
using Effect = AVARipOff.Engine.Effect;

namespace AVARipOff.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVARipOff/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);



    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3.2), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF02040A), new Rect(0, 0, Field.Width, Field.Height));
        DrawGround(dc);
        if (InGame)
        {
            DrawDepot(dc, 1);
            foreach (var r in g.Raiders)
                if (r.Towing && r.Target is { } t) DrawTowLine(dc, r.X, r.Y, t.X, t.Y);
            foreach (var c in g.Canisters) DrawCanister(dc, c.X, c.Y, c.Carrier != null, 1);
            foreach (var r in g.Raiders) if (r.Delay <= 0) DrawRaider(dc, r.Kind, r.X, r.Y, r.Angle, r.T, 1);
            DrawTank(dc);
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1), c = Math.Cos(d.Angle) * d.Length / 2, s = Math.Sin(d.Angle) * d.Length / 2;
                Glow(dc, new Point(d.X - c, d.Y - s), new Point(d.X + c, d.Y + s), d.Colour, a, 1.6);
            }
            foreach (var b in g.Shells)
            {
                double l = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
                if (b.Hostile)
                {
                    dc.DrawEllipse(Brush(Alpha(0xFFFF7AD8, 0.3)), null, new Point(b.X, b.Y), 6, 6);
                    dc.DrawEllipse(Brush(0xFFFFD0F0), null, new Point(b.X, b.Y), 2.6, 2.6);
                }
                else Glow(dc, new Point(b.X - b.Vx / l * 10, b.Y - b.Vy / l * 10), new Point(b.X, b.Y), 0xFFFFFFE0, 1, 2.4);
            }
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    /// <summary>The plain: a faint survey grid of crosses, and a few stars.</summary>
    private void DrawGround(DrawingContext dc)
    {
        var pen = Pen(0x203A5AA0, 1);
        for (double x = 32; x < Field.Width; x += 64)
        for (double y = Field.Top + 24; y < Field.Bottom; y += 64)
        {
            dc.DrawLine(pen, new Point(x - 3, y), new Point(x + 3, y));
            dc.DrawLine(pen, new Point(x, y - 3), new Point(x, y + 3));
        }
        for (int i = 0; i < 70; i++)
        {
            uint h = Hash(i, 9, 4);
            double x = h % 1024, y = Field.Top + (h >> 10) % 688, tw = 0.25 + 0.3 * Math.Abs(Math.Sin(_clock * (0.5 + (h >> 20) % 5 * 0.3) + i));
            dc.DrawEllipse(Brush(Alpha(0xFFDDE4FF, tw)), null, new Point(x, y), 0.9, 0.9);
        }
    }

    /// <summary>The fuel depot: a ring of marks round where the canisters stand.</summary>
    private void DrawDepot(DrawingContext dc, double alpha)
    {
        double r = GameEngine.DepotRadius + 30;
        for (int i = 0; i < 24; i++)
        {
            double a = i * Math.PI / 12 + _clock * 0.15;
            dc.DrawLine(Pen(Alpha(GameEngine.FuelColour, 0.28 * alpha), 1.4), new Point(Field.CX + Math.Cos(a) * r, Field.CY + Math.Sin(a) * r),
                new Point(Field.CX + Math.Cos(a + 0.13) * r, Field.CY + Math.Sin(a + 0.13) * r));
        }
    }

    /// <summary>A canister: a glowing triangle with a fill cap; flickering while it's being towed.</summary>
    private void DrawCanister(DrawingContext dc, double x, double y, bool towed, double k)
    {
        double a = towed && (int)(_clock * 12) % 2 == 0 ? 0.55 : 1;
        Shape(dc, x, y, -Math.PI / 2, k, GameEngine.FuelColour, a, (9, 0), (-6, 8), (-6, -8));
        dc.DrawEllipse(Brush(Alpha(GameEngine.FuelColour, 0.35 * a)), null, new Point(x, y + 1 * k), 3 * k, 3 * k);
    }

    private void DrawTowLine(DrawingContext dc, double ax, double ay, double bx, double by)
    {
        int n = 6;
        for (int i = 0; i < n; i += 2)
        {
            double t0 = (double)i / n, t1 = (i + 1.0) / n;
            Glow(dc, new Point(ax + (bx - ax) * t0, ay + (by - ay) * t0), new Point(ax + (bx - ax) * t1, ay + (by - ay) * t1), 0xFFFFFFFF, 0.6, 1.2);
        }
    }

    private void DrawTank(DrawingContext dc)
    {
        var k = Engine.Tank;
        if (!k.Alive) return;
        if (k.Safe > 0 && (int)(_clock * 10) % 2 == 0) return;
        DrawTankAt(dc, k.X, k.Y, k.Angle, 1);
    }

    /// <summary>The player's tank from above: a hull with its treads, a round turret, and the gun fixed straight ahead.</summary>
    private static void DrawTankAt(DrawingContext dc, double x, double y, double angle, double k)
    {
        uint col = GameEngine.TankColour;
        Shape(dc, x, y, angle, k, col, 1, (13, -9), (13, 9), (-12, 9), (-12, -9));
        Shape(dc, x, y, angle, k, col, 0.55, (14, -12), (14, -9), (-13, -9), (-13, -12));
        Shape(dc, x, y, angle, k, col, 0.55, (14, 12), (14, 9), (-13, 9), (-13, 12));
        Shape(dc, x, y, angle, k, col, 1, (5, -5), (5, 5), (-5, 5), (-5, -5));
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Glow(dc, new Point(x + c * 5 * k, y + s * 5 * k), new Point(x + c * 20 * k, y + s * 20 * k), col, 1, 2.6);
    }

    /// <summary>
    /// A raider, by kind: the runner a swept dart, the gunner a squat hull with a barrel, the hunter a spiked wedge
    /// with its jaws working.
    /// </summary>
    private static void DrawRaider(DrawingContext dc, RaiderKind kind, double x, double y, double angle, double t, double k)
    {
        uint col = GameEngine.Colour(kind);
        switch (kind)
        {
            case RaiderKind.Runner:
                Shape(dc, x, y, angle, k, col, 1, (14, 0), (-10, -10), (-5, 0), (-10, 10));
                Shape(dc, x, y, angle, k, col, 0.6, (4, -3), (4, 3), (-3, 0));
                break;
            case RaiderKind.Gunner:
            {
                Shape(dc, x, y, angle, k, col, 1, (9, -10), (12, 0), (9, 10), (-11, 10), (-11, -10));
                Shape(dc, x, y, angle, k, col, 0.6, (3, -5), (3, 5), (-6, 5), (-6, -5));
                double c = Math.Cos(angle), s = Math.Sin(angle);
                Glow(dc, new Point(x + c * 3 * k, y + s * 3 * k), new Point(x + c * 20 * k, y + s * 20 * k), col, 1, 2.2);
                break;
            }
            default:
            {
                double jaw = 3 + Math.Sin(t * 14) * 2.5;
                Shape(dc, x, y, angle, k, col, 1, (14, -jaw), (2, -11), (-12, -8), (-8, 0), (-12, 8), (2, 11), (14, jaw), (4, 0));
                Shape(dc, x, y, angle, k, col, 0.6, (-2, -4), (-2, 4), (-7, 0));
                break;
            }
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 20, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 34, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"WAVE {g.Wave}", new Point(512, 18), 26, GameEngine.TankColour, center: true);
        // The fuel left: one mark for each canister, dimmed for the ones gone.
        for (int i = 0; i < GameEngine.CanisterCount; i++)
        {
            double x = 512 - (GameEngine.CanisterCount - 1) * 13 + i * 26;
            bool have = i < g.Canisters.Count;
            Shape(dc, x, 52, -Math.PI / 2, 0.9, have ? GameEngine.FuelColour : 0xFF3A4A5A, have ? 1 : 0.6, (9, 0), (-6, 8), (-6, -8));
        }
        if (!g.Tank.Alive && g.Phase == Phase.Play)
            Text(dc, "TANK DOWN - THE RAIDERS HAVE A FREE RUN", new Point(512, Field.Top + 20), 24, Red, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 230;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0040810), new Rect(cx - w / 2, cy - 46, w, h), 6);
        dc.DrawRectangle(null, Pen(Alpha(GameEngine.FuelColour, 0.45), 1.2), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Lost ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        string title = "AVA RIP OFF";
        uint[] hues = [GameEngine.TankColour, GameEngine.FuelColour, 0xFF6AD8FF, 0xFFFF7AD8];
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 28 + 28 + i * 56, 120), 112, hues[(i + (int)(_clock * 3)) % hues.Length], center: true);
        Text(dc, "AFTER THE 1980 VECTOR ARCADE CLASSIC", new Point(cx, 186), 28, Dim, center: true);
        // A runner circling with a canister in tow.
        double a = _clock * 0.8, rx = cx + Math.Cos(a) * 190, ry = 262 + Math.Sin(a) * 48, head = Math.Atan2(Math.Cos(a) * 48, -Math.Sin(a) * 190);
        double tx = rx - Math.Cos(head) * Raider.Tow, ty = ry - Math.Sin(head) * Raider.Tow;
        DrawTowLine(dc, rx, ry, tx, ty);
        DrawCanister(dc, tx, ty, true, 1.2);
        DrawRaider(dc, RaiderKind.Runner, rx, ry, head, _clock, 1.3);
        DrawTankAt(dc, cx, 262, -Math.PI / 2 + Math.Sin(_clock * 1.1) * 0.8, 1.5);
        Text(dc, "THE RAIDERS ARE AFTER YOUR FUEL - SHOOT THEM BEFORE THEY TOW IT OFF THE SCREEN", new Point(cx, 336), 23, White, center: true);
        Text(dc, "THE GAME ENDS WHEN THE LAST CANISTER IS GONE", new Point(cx, 364), 23, White, center: true);
        (string Name, string Points, RaiderKind Kind)[] legend =
        [
            ("RUNNER", "50 x WAVE", RaiderKind.Runner),
            ("GUNNER", "75 x WAVE", RaiderKind.Gunner),
            ("HUNTER", "100 x WAVE", RaiderKind.Hunter),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = cx - 220 + i * 220;
            DrawRaider(dc, legend[i].Kind, x, 432, -Math.PI / 2 + Math.Sin(_clock + i) * 0.4, _clock, 1.6);
            Text(dc, legend[i].Name, new Point(x, 478), 22, Dim, center: true);
            Text(dc, legend[i].Points, new Point(x, 504), 22, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 576), 46, Yellow, center: true);
        Text(dc, "ARROWS: TURN, FORWARD AND BACK    SPACE: FIRE    P: PAUSE    M: SOUND", new Point(cx, 646), 23, Dim, center: true);
        Text(dc, "GAMEPAD: STICK OR D-PAD TO DRIVE, A TO FIRE, START TO PAUSE", new Point(cx, 676), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP DEFENDERS", new Point(cx, 140), 68, GameEngine.FuelColour, center: true);
        Text(dc, "RANK NAME    SCORE    WAVE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
