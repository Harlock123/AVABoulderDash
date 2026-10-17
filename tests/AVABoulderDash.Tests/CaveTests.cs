using AVABoulderDash.Engine;

namespace AVABoulderDash.Tests;

/// <summary>The eight caves: always the same, walled in steel, and every one clearable in the time.</summary>
public class CaveTests(ITestOutputHelper output)
{
    public static TheoryData<int> Caves() => [.. Enumerable.Range(0, CaveBook.All.Length)];

    private static int Count(Grid g, Tile t)
    {
        int n = 0;
        foreach (var c in g.Cells) if (c.Tile == t) n++;
        return n;
    }

    [Theory]
    [MemberData(nameof(Caves))]
    public void Every_cave_comes_out_the_same_walled_in_steel_with_a_way_in_a_way_out_and_diamonds_to_spare(int index)
    {
        var def = CaveBook.All[index];
        var a = Grid.Make(def);
        var b = Grid.Make(def);
        for (int y = 0; y < Field.Rows; y++)
        for (int x = 0; x < Field.Cols; x++)
        {
            Assert.Equal(a.At(x, y), b.At(x, y));
            if (x == 0 || y == 0 || x == Field.Cols - 1 || y == Field.Rows - 1)
                Assert.True(a.At(x, y) is Tile.Steel or Tile.Exit, $"cave {def.Letter}: a hole in the wall at ({x},{y})");
        }
        Assert.Equal(1, Count(a, Tile.Entrance));
        Assert.Equal(1, Count(a, Tile.Exit));
        Assert.True(Count(a, Tile.Diamond) >= def.Needed + 4, $"cave {def.Letter}: too few diamonds");
        Assert.Equal((char)('A' + index), def.Letter);
    }

    [Theory]
    [MemberData(nameof(Caves))]
    public void Every_cave_is_cleared_by_the_computer_player_without_dying(int index)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaboulderdash-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(index);
        g.Pilot.Reset();
        int t = 0;
        for (; t < 60 * 300 && g.Phase is Phase.Play; t++) g.Tick(g.Pilot.Decide(g));
        var def = CaveBook.All[index];
        output.WriteLine($"cave {def.Letter}: {g.Phase} after {t / 60.0:0.0}s with {g.TimeLeft:0}s left, {g.Diamonds} diamonds");
        Assert.Equal(Phase.Out, g.Phase);
        Assert.True(g.Lives >= GameEngine.Lives0);
        Assert.True(g.Diamonds >= def.Needed);
    }

    [Fact]
    public void A_player_who_does_nothing_is_safe_at_the_way_in_until_the_time_runs_out()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaboulderdash-tests-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go(0);
        g.Run((int)(60 * (CaveBook.All[0].Time - 1)));
        Assert.True(g.Alive);
        g.Run(120);
        Assert.False(g.Alive);
        Assert.Equal("out of time", g.Cause);
    }
}
