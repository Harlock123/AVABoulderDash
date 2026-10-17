using AVABoulderDash.Engine;

namespace AVABoulderDash.Tests;

/// <summary>
/// How the computer player gets on in each cave (set BOULDER_PROBE to run it, for tuning: "1" for the pilot,
/// "lazy" for a player who never touches the controls).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_digs()
    {
        string? mode = Environment.GetEnvironmentVariable("BOULDER_PROBE");
        if (mode is null) return;
        for (int cave = 0; cave < CaveBook.All.Length; cave++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "boulder-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.Go(cave);
            g.SetLives(20);
            g.Pilot.Reset();
            var doing = new Dictionary<string, int>();
            var deaths = new List<string>();
            int t = 0, lives = g.Lives;
            for (; t < 60 * 900 && g.State == GameState.Playing && g.CaveIndex == cave && g.Cleared == 0; t++)
            {
                g.Tick(mode == "lazy" ? default : g.Pilot.Decide(g));
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Lives < lives) { deaths.Add($"{g.Cause} at ({g.X},{g.Y}) with {g.Diamonds} in, {g.TimeLeft:0}s left"); }
                lives = g.Lives;
            }
            var def = CaveBook.All[cave];
            output.WriteLine($"{def.Letter} {def.Name}: {(g.Cleared > 0 ? "CLEARED" : "not cleared")} after {t / 60}s, deaths {deaths.Count}, diamonds {g.Diamonds}/{def.Needed}, score {g.Score}");
            foreach (var d in deaths.Take(4)) output.WriteLine("   died: " + d);
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Take(8).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
