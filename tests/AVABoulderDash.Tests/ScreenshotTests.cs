using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVABoulderDash.Engine;
using AVABoulderDash.Views;
using Controls = AVABoulderDash.Engine.Controls;

namespace AVABoulderDash.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set BOULDER_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaboulderdash-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
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
        var dir = Environment.GetEnvironmentVariable("BOULDER_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVABoulderDash-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
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
        g.HighScores.Insert("DIG", 48300, 8);
        g.HighScores.Insert("AVA", 21500, 3);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A cave dug by the computer player until something worth showing.</summary>
    private static void Dig(int cave, string name, Func<GameEngine, bool> moment, int seconds = 120)
    {
        var (w, c, g) = Open();
        g.Go(cave);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_way_in() => Dig(0, "first-dig", g => g.Diamonds >= 3);

    [AvaloniaFact]
    public void Rockfall() => Dig(1, "rockfall", g => g.Diamonds >= 6);

    [AvaloniaFact]
    public void Fireflies() => Dig(2, "fireflies", g => Near(g, Tile.Firefly, 5));

    [AvaloniaFact]
    public void Butterflies() => Dig(3, "butterflies", g => Near(g, Tile.Butterfly, 6));

    [AvaloniaFact]
    public void The_slime() => Dig(4, "slime", g => g.Slime > 6 && Near(g, Tile.Slime, 7));

    [AvaloniaFact]
    public void The_magic_wall() => Dig(5, "magic-wall", g => g.Diamonds >= 5);

    [AvaloniaFact]
    public void The_way_out_open() => Dig(6, "way-out", g => g.ExitOpen && g.Effects.Any(e => e.Kind == EffectKind.Word && e.Age > 0.2));

    [AvaloniaFact]
    public void The_vault() => Dig(7, "vault", g => g.Diamonds >= 4);

    [AvaloniaFact]
    public void A_blast()
    {
        var (w, c, g) = Open();
        g.Go(2);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Diamonds >= 2, 60));
        g.GiveUp();
        g.Run(4);
        c.Advance(0.05);
        Shot(w, c, "blast");
    }

    private static bool Near(GameEngine g, Tile t, int r)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
            if (g.Grid.At(g.X + dx, g.Y + dy) == t) return true;
        return false;
    }
}
