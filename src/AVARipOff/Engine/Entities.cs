namespace AVARipOff.Engine;

/// <summary>The controls: turning (-1 left, 1 right), driving (1 forward, -1 back), and fire.</summary>
public readonly record struct Controls(int Turn = 0, int Drive = 0, bool Fire = false);

/// <summary>The screen: an open plain below the score bar, with the fuel depot in the middle of it.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Top = 80, Bottom = Height;
    public const double CX = Width / 2, CY = (Top + Bottom) / 2;

    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));

    /// <summary>Whether a point is on the screen's plain (with room to spare, if <paramref name="margin"/> is given).</summary>
    public static bool Inside(double x, double y, double margin = 0) => x >= margin && x <= Width - margin && y >= Top + margin && y <= Bottom - margin;

    /// <summary>How far a point is from the nearest edge of the plain.</summary>
    public static double ToEdge(double x, double y) => Math.Min(Math.Min(x, Width - x), Math.Min(y - Top, Bottom - y));
}

/// <summary>The player's tank: turns on the spot, drives forward and back, and fires the way it faces.</summary>
public sealed class Tank
{
    public double X = Field.CX, Y = Field.CY, Angle = -Math.PI / 2, Speed;
    public bool Alive = true;
    public double Safe, FireIn, Back;
    public const double Radius = 13;
}

public sealed class Shell
{
    public double X, Y, Vx, Vy, Life;
    /// <summary>Fired by a raider, at the tank.</summary>
    public bool Hostile;
}

/// <summary>A fuel canister: in the depot, or being towed away by a raider, or dropped where its raider was hit.</summary>
public sealed class Canister
{
    public double X, Y;
    public Raider? Carrier;
    public const double Radius = 8;
}

public enum RaiderKind
{
    /// <summary>Quick, unarmed: it goes for the fuel and nothing else.</summary>
    Runner,
    /// <summary>Armed: it goes for the fuel too, but fires at the tank when it's in front of it.</summary>
    Gunner,
    /// <summary>It goes for the tank first: rams it or shoots it, and only then for the fuel.</summary>
    Hunter,
}

/// <summary>A raider: in from the edge to hook a canister, then away with it the nearest way off the screen.</summary>
public sealed class Raider
{
    public RaiderKind Kind;
    public double X, Y, Angle, T, FireIn;
    /// <summary>Which way it weaves on its way in, and how far through the weave it starts.</summary>
    public double Weave = 1, Phase;
    public Canister? Target;
    /// <summary>Waiting off the screen before it comes in.</summary>
    public double Delay;
    public bool Dead;
    public bool Towing => Target?.Carrier == this;
    public const double Radius = 12, Tow = 28;
}

/// <summary>A broken-off line, spinning away.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Angle, Spin, Length, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
