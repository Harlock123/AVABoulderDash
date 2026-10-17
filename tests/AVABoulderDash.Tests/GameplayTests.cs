using Avalonia.Input;
using AVABoulderDash.Engine;

namespace AVABoulderDash.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaboulderdash-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A cave for a test: steel round the edge, the inside all dirt (or space), Digby's way in at (5,10).</summary>
    private static Grid Cave(Tile inside = Tile.Dirt)
    {
        var g = new Grid { Entrance = (5, 10), Exit = (30, 10) };
        for (int y = 0; y < Field.Rows; y++)
        for (int x = 0; x < Field.Cols; x++)
            g[x, y] = new Cell(x == 0 || y == 0 || x == Field.Cols - 1 || y == Field.Rows - 1 ? Tile.Steel : inside);
        g[5, 10] = new Cell(Tile.Entrance);
        g[30, 10] = new Cell(Tile.Exit);
        return g;
    }

    private static CaveDef Def(int needed = 5, int time = 100) => new() { Letter = 'T', Name = "TEST", Needed = needed, Value = 10, Extra = 25, Time = time, Seed = 1 };

    private static GameEngine In(Grid grid, CaveDef? def = null)
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame(0);
        g.Try(grid, def ?? Def());
        return g;
    }

    private static readonly Controls Left = new(-1, 0), Right = new(1, 0), Up = new(0, -1), Down = new(0, 1);

    private static void Turns(GameEngine g, int n, Controls c = default)
    {
        for (int i = 0; i < n; i++) g.Step(c);
    }

    // ───────────────────────────── digging ─────────────────────────────

    [Fact]
    public void Digby_comes_in_at_the_way_in_and_digs_through_dirt()
    {
        var g = In(Cave());
        Assert.True(g.Alive);
        Assert.Equal((5, 10), (g.X, g.Y));
        g.Step(Right);
        Assert.Equal((6, 10), (g.X, g.Y));
        Assert.Equal(Tile.Space, g.Grid.At(5, 10));
        g.Step(Down);
        Assert.Equal((6, 11), (g.X, g.Y));
    }

    [Fact]
    public void Steel_and_walls_stop_him()
    {
        var grid = Cave();
        grid[6, 10] = new Cell(Tile.Wall);
        grid[5, 9] = new Cell(Tile.Steel);
        var g = In(grid);
        g.Step(Right);
        g.Step(Up);
        Assert.Equal((5, 10), (g.X, g.Y));
    }

    [Fact]
    public void Holding_the_button_grabs_without_moving()
    {
        var grid = Cave();
        grid[6, 10] = new Cell(Tile.Diamond);
        var g = In(grid);
        g.Step(new Controls(1, 0, true));
        Assert.Equal((5, 10), (g.X, g.Y));
        Assert.Equal(1, g.Diamonds);
        Assert.Equal(Tile.Space, g.Grid.At(6, 10));
    }

    [Fact]
    public void A_boulder_is_pushed_sideways_after_a_moment_and_only_into_space()
    {
        var grid = Cave();
        grid[6, 10] = new Cell(Tile.Boulder);
        grid[7, 10] = new Cell(Tile.Space);
        var g = In(grid);
        g.Step(Right);
        Assert.Equal(Tile.Boulder, g.Grid.At(6, 10));
        g.Step(Right);
        Assert.Equal(Tile.Boulder, g.Grid.At(7, 10));
        Assert.Equal((6, 10), (g.X, g.Y));
        // Against dirt beyond, it won't go.
        Turns(g, 4, Right);
        Assert.Equal(Tile.Boulder, g.Grid.At(7, 10));
    }

    // ───────────────────────────── falling ─────────────────────────────

    [Fact]
    public void A_boulder_falls_through_space_and_lands()
    {
        var grid = Cave();
        grid[10, 3] = new Cell(Tile.Boulder);
        for (int y = 4; y < 8; y++) grid[10, y] = new Cell(Tile.Space);
        var g = In(grid);
        Turns(g, 6);
        Assert.Equal(Tile.Boulder, g.Grid.At(10, 7));
        Assert.False(g.Grid[10, 7].Falling);
    }

    [Fact]
    public void A_boulder_rolls_off_another_into_room_beside_and_below()
    {
        var grid = Cave();
        grid[10, 8] = new Cell(Tile.Boulder);
        grid[10, 9] = new Cell(Tile.Boulder);
        grid[9, 8] = new Cell(Tile.Space);
        grid[9, 9] = new Cell(Tile.Space);
        var g = In(grid);
        Turns(g, 3);
        Assert.Equal(Tile.Space, g.Grid.At(10, 8));
        Assert.Equal(Tile.Boulder, g.Grid.At(9, 9));
    }

    [Fact]
    public void A_resting_boulder_overhead_is_safe_but_a_falling_one_crushes()
    {
        var grid = Cave();
        grid[5, 9] = new Cell(Tile.Boulder);
        var g = In(grid);
        Turns(g, 5);
        Assert.True(g.Alive);
        // Step out from under it, and back: it has come down into the gap and lands on him.
        var g2 = In(Cave());
        g2.Grid[6, 6] = new Cell(Tile.Boulder);
        for (int y = 7; y < 10; y++) g2.Grid[6, y] = new Cell(Tile.Space);
        g2.Step(Right);
        Turns(g2, 4);
        Assert.False(g2.Alive);
        Assert.Equal("crushed", g2.Cause);
        Assert.Equal(GameEngine.Lives0 - 1, g2.Lives);
    }

    // ───────────────────────────── diamonds and the way out ─────────────────────────────

    [Fact]
    public void Enough_diamonds_open_the_way_out_and_raise_their_worth()
    {
        var grid = Cave();
        for (int x = 6; x < 9; x++) grid[x, 10] = new Cell(Tile.Diamond);
        var g = In(grid, Def(needed: 2));
        g.Step(Right);
        Assert.Equal(10, g.Score);
        Assert.False(g.ExitOpen);
        g.Step(Right);
        Assert.True(g.ExitOpen);
        Assert.Equal(25, g.Value);
        g.Step(Right);
        Assert.Equal(10 + 10 + 25, g.Score);
    }

    [Fact]
    public void The_way_out_ends_the_cave_counts_the_time_into_the_score_and_starts_the_next()
    {
        var grid = Cave(Tile.Space);
        var g = In(grid, Def(needed: 0, time: 60));
        Assert.True(g.ExitOpen);
        double left = 0;
        for (int i = 0; i < 40 && g.Phase == Phase.Play; i++) { left = g.TimeLeft; g.Step(Right); }
        Assert.Equal(Phase.Out, g.Phase);
        g.Run(60 * 3);
        int bonus = (int)Math.Ceiling(left) * GameEngine.BonusPerSecond;
        Assert.InRange(g.Score, bonus - GameEngine.BonusPerSecond, bonus);
        g.Run(60 * 2);
        Assert.Equal(1, g.Cleared);
        Assert.Equal(1, g.CaveIndex);
    }

    [Fact]
    public void A_shut_way_out_is_just_wall()
    {
        var grid = Cave(Tile.Space);
        var g = In(grid, Def(needed: 5));
        Turns(g, 30, Right);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal((29, 10), (g.X, g.Y));
    }

    // ───────────────────────────── creatures ─────────────────────────────

    [Fact]
    public void A_firefly_circles_its_hollow_and_blows_up_when_it_meets_Digby()
    {
        var grid = Cave();
        grid.Pocket(Tile.Firefly, 12, 5);
        var g = In(grid);
        var seen = new HashSet<(int, int)>();
        for (int i = 0; i < 16; i++)
        {
            g.Step();
            for (int y = 5; y < 8; y++)
            for (int x = 12; x < 15; x++)
                if (g.Grid.At(x, y) == Tile.Firefly) seen.Add((x, y));
        }
        // Turning left at every chance, it goes round and round in a tight square, never out of its hollow.
        Assert.Equal(4, seen.Count);
        // Dig in next to it.
        var g2 = In(Cave());
        g2.Grid[7, 10] = new Cell(Tile.Firefly);
        g2.Step(Right);
        g2.Step();
        Assert.False(g2.Alive);
        Assert.Equal("blown up", g2.Cause);
    }

    [Fact]
    public void A_boulder_dropped_on_a_butterfly_leaves_diamonds()
    {
        var grid = Cave();
        grid[20, 4] = new Cell(Tile.Boulder) { Falling = true };
        grid[20, 5] = new Cell(Tile.Space);
        grid[20, 6] = new Cell(Tile.Butterfly);
        grid[19, 6] = new Cell(Tile.Wall);
        grid[21, 6] = new Cell(Tile.Wall);
        var g = In(grid);
        Turns(g, 8);
        int diamonds = 0;
        for (int y = 4; y < 9; y++)
        for (int x = 18; x < 23; x++)
            if (g.Grid.At(x, y) == Tile.Diamond) diamonds++;
        Assert.True(diamonds >= 6);
    }

    [Fact]
    public void A_blast_spares_steel()
    {
        var grid = Cave();
        grid[1, 1] = new Cell(Tile.Firefly);
        grid[2, 1] = new Cell(Tile.Slime);
        var g = In(grid);
        Turns(g, 6);
        Assert.Equal(Tile.Steel, g.Grid.At(0, 0));
        Assert.Equal(Tile.Steel, g.Grid.At(1, 0));
    }

    // ───────────────────────────── the slime and the magic wall ─────────────────────────────

    [Fact]
    public void Shut_in_the_slime_turns_to_diamonds()
    {
        var grid = Cave();
        grid.Box(Tile.Wall, 15, 3, 3, 3);
        grid[16, 4] = new Cell(Tile.Slime);
        var g = In(grid);
        g.Step();
        Assert.Equal(Tile.Diamond, g.Grid.At(16, 4));
    }

    [Fact]
    public void Grown_too_big_the_slime_turns_to_boulders()
    {
        var grid = Cave(Tile.Space);
        for (int x = 10; x < 30; x++)
        for (int y = 1; y < 11; y++)
            grid[x, y] = new Cell(Tile.Slime);
        grid[30, 10] = new Cell(Tile.Exit);
        var g = In(grid);
        g.Step();
        Assert.Equal(Tile.Boulder, g.Grid.At(15, 10));
    }

    [Fact]
    public void The_magic_wall_turns_falling_boulders_to_diamonds_while_it_hums_then_stops()
    {
        var grid = Cave(Tile.Space);
        grid.Line(Tile.Magic, 15, 8, 3, 1, 0);
        grid[16, 6] = new Cell(Tile.Boulder);
        grid[16, 12] = new Cell(Tile.Dirt);
        var def = Def();
        def.MagicTime = 3;
        var g = In(grid, def);
        Turns(g, 8);
        Assert.Equal(Tile.Diamond, g.Grid.At(16, 11));
        Assert.Equal(MagicState.Busy, g.Magic);
        Turns(g, 10);
        Assert.Equal(MagicState.Spent, g.Magic);
    }

    // ───────────────────────────── lives, time, the game ─────────────────────────────

    [Fact]
    public void Out_of_time_costs_a_life_and_the_cave_starts_again()
    {
        var g = In(Cave(), Def(time: 2));
        g.Run(60 * 3);
        Assert.Equal("out of time", g.Cause);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        g.Run((int)(60 * (GameEngine.DyingSeconds + GameEngine.StartSeconds)) + 10);
        Assert.True(g.Alive);
    }

    [Fact]
    public void Giving_up_costs_a_life()
    {
        var g = In(Cave());
        g.GiveUp();
        Assert.False(g.Alive);
        Assert.Equal("gave up", g.Cause);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
    }

    [Fact]
    public void The_last_life_lost_ends_the_game_and_a_good_score_goes_on_the_table()
    {
        var g = In(Cave());
        g.SetLives(1);
        g.SetScore(90000);
        g.GiveUp();
        g.Run((int)(60 * (GameEngine.DyingSeconds + 4)) + 10);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void Every_five_hundred_points_is_a_life()
    {
        var grid = Cave();
        grid[6, 10] = new Cell(Tile.Diamond);
        var g = In(grid);
        g.SetScore(495);
        g.Step(Right);
        Assert.Equal(GameEngine.Lives0 + 1, g.Lives);
    }

    [Fact]
    public void Looking_ahead_leaves_the_cave_as_it_was()
    {
        var g = In(Cave());
        var before = g.Grid.Copy();
        Assert.True(g.Survives([Right, Right, Down]));
        Assert.Equal((5, 10), (g.X, g.Y));
        for (int y = 0; y < Field.Rows; y++)
        for (int x = 0; x < Field.Cols; x++)
            Assert.Equal(before.At(x, y), g.Grid.At(x, y));
    }

    // ───────────────────────────── title, keys ─────────────────────────────

    [Fact]
    public void The_title_chooses_the_cave_to_start_from()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default);
        g.Tick(Down);
        g.Tick(default);
        g.Tick(Down);
        g.Tick(default);
        Assert.Equal(2, g.Choice);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal('E', g.Def.Letter);
    }

    [Fact]
    public void Left_alone_the_title_runs_the_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.True(g.State == GameState.HighScores, "the demonstration should hand over to the high scores");
        g.Run((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_keys_steer_Digby_the_right_way()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.S);
        Assert.Equal(1, input.Player.Dy);
        input.OnKeyUp(Key.S);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        input.Dispose();
        // Up on the keys takes him up the screen.
        var g = In(Cave());
        g.Step(input.Player with { Dx = 0, Dy = -1, Fire = false });
        Assert.Equal((5, 9), (g.X, g.Y));
    }
}
