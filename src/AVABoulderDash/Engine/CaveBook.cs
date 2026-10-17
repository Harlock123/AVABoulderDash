namespace AVABoulderDash.Engine;

/// <summary>
/// The eight caves, A to H. Each is scattered from its own seed and then drawn over, so it comes out the same
/// every time; the tests prove the computer player can clear every one in the time.
/// </summary>
public static class CaveBook
{
    public static readonly CaveDef[] All =
    [
        new()
        {
            Letter = 'A', Name = "FIRST DIG", Hint = "Collect the diamonds; mind the boulders over your head.",
            Seed = 101, Needed = 12, Value = 10, Extra = 15, Time = 120,
            Scatter = [(Tile.Boulder, 24), (Tile.Diamond, 14), (Tile.Space, 10)],
            Entrance = (3, 3), Exit = (37, 19),
        },
        new()
        {
            Letter = 'B', Name = "ROCKFALL", Hint = "Two long walls, and boulders piled on them.",
            Seed = 202, Needed = 15, Value = 10, Extra = 20, Time = 120,
            Scatter = [(Tile.Boulder, 44), (Tile.Diamond, 15), (Tile.Space, 6)],
            Draw = g =>
            {
                g.Line(Tile.Wall, 1, 7, 28, 1, 0);
                g.Line(Tile.Wall, 11, 14, 28, 1, 0);
            },
            Entrance = (2, 2), Exit = (37, 20),
        },
        new()
        {
            Letter = 'C', Name = "FIREFLY HOLLOWS", Hint = "Fireflies circle in their hollows. Dig in and they come out.",
            Seed = 303, Needed = 15, Value = 15, Extra = 20, Time = 120,
            Scatter = [(Tile.Boulder, 30), (Tile.Diamond, 14), (Tile.Space, 4)],
            Draw = g =>
            {
                g.Pocket(Tile.Firefly, 10, 5);
                g.Pocket(Tile.Firefly, 25, 4);
                g.Pocket(Tile.Firefly, 16, 12);
                g.Pocket(Tile.Firefly, 30, 15);
            },
            Entrance = (2, 10), Exit = (37, 2),
        },
        new()
        {
            Letter = 'D', Name = "BUTTERFLY GARDEN", Hint = "Drop a boulder on a butterfly and it turns to diamonds.",
            Seed = 404, Needed = 14, Value = 15, Extra = 25, Time = 120,
            Scatter = [(Tile.Boulder, 34), (Tile.Diamond, 13), (Tile.Space, 4)],
            Draw = g =>
            {
                g.Pocket(Tile.Butterfly, 8, 5, 4, 3);
                g.Pocket(Tile.Butterfly, 20, 7, 4, 3);
                g.Pocket(Tile.Butterfly, 30, 11, 4, 3);
                g.Pocket(Tile.Butterfly, 12, 15, 4, 3);
            },
            Entrance = (37, 2), Exit = (2, 20),
        },
        new()
        {
            Letter = 'E', Name = "THE SLIME", Hint = "The slime grows. Shut it in and it turns to diamonds; let it grow and it turns to stone.",
            Seed = 505, Needed = 16, Value = 15, Extra = 25, Time = 130, SlimeSlow = 60,
            Scatter = [(Tile.Boulder, 30), (Tile.Diamond, 15), (Tile.Space, 5)],
            Draw = g =>
            {
                g.Set(Tile.Slime, 20, 11);
                g.Set(Tile.Slime, 21, 11);
                g.Pocket(Tile.Firefly, 7, 15);
                g.Pocket(Tile.Firefly, 31, 5);
            },
            Entrance = (2, 2), Exit = (37, 20),
        },
        new()
        {
            Letter = 'F', Name = "THE MAGIC WALL", Hint = "Boulders that fall through the magic wall come out as diamonds, while it hums.",
            Seed = 606, Needed = 15, Value = 20, Extra = 30, Time = 120, MagicTime = 25,
            Scatter = [(Tile.Boulder, 40), (Tile.Diamond, 11), (Tile.Space, 4)],
            Draw = g =>
            {
                g.Line(Tile.Magic, 4, 11, 32, 1, 0);
                g.Line(Tile.Space, 4, 12, 32, 1, 0);
            },
            Entrance = (2, 19), Exit = (37, 19),
        },
        new()
        {
            Letter = 'G', Name = "THE MAZE", Hint = "Walls everywhere, with one way through each.",
            Seed = 707, Needed = 18, Value = 15, Extra = 25, Time = 140,
            Scatter = [(Tile.Boulder, 28), (Tile.Diamond, 16), (Tile.Space, 4)],
            Draw = g =>
            {
                foreach (var (x, gap) in new[] { (6, 3), (12, 17), (18, 9), (24, 4), (30, 15) })
                {
                    g.Line(Tile.Wall, x, 1, 20, 0, 1);
                    g.Line(Tile.Dirt, x, gap, 2, 0, 1);
                }
                g.Pocket(Tile.Firefly, 14, 4);
                g.Pocket(Tile.Butterfly, 26, 12);
            },
            Entrance = (2, 2), Exit = (37, 20),
        },
        new()
        {
            Letter = 'H', Name = "THE VAULT", Hint = "A vault of diamonds, a way in at the bottom, and everything else besides.",
            Seed = 808, Needed = 20, Value = 20, Extra = 30, Time = 150, SlimeSlow = 70,
            Scatter = [(Tile.Boulder, 34), (Tile.Diamond, 12), (Tile.Space, 4)],
            Draw = g =>
            {
                g.Box(Tile.Steel, 15, 7, 10, 7);
                g.Box(Tile.Diamond, 16, 8, 8, 5, filled: true);
                g.Set(Tile.Dirt, 19, 13);
                g.Pocket(Tile.Butterfly, 5, 15);
                g.Pocket(Tile.Firefly, 31, 4);
                g.Set(Tile.Slime, 33, 17);
            },
            Entrance = (2, 2), Exit = (37, 20),
        },
    ];
}
