namespace AVABoulderDash.Engine;

/// <summary>The controls: left and right, up and down (up the screen is -1), and the button (to grab without moving).</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>The screen: the score bar, then a window onto the cave of 32 by 19 squares, then the cave's name.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 80;
    public const double Tile = 32;
    public const int ViewCols = 32, ViewRows = 19;
    public const double Top = Hud, Bottom = Top + ViewRows * Tile;
    /// <summary>A cave is 40 squares across and 22 down, steel all round.</summary>
    public const int Cols = 40, Rows = 22;
}

/// <summary>What a square of the cave holds.</summary>
public enum Tile : byte
{
    Space, Dirt, Wall, Steel, Boulder, Diamond, Digby, Firefly, Butterfly, Slime,
    /// <summary>A magic wall: still until something falls on it, then busy for a while, then spent.</summary>
    Magic,
    /// <summary>Where Digby comes in (flashing, before he appears), and the way out (shut until enough diamonds are in).</summary>
    Entrance, Exit,
    /// <summary>A blast, burning out to empty space or to diamonds.</summary>
    Blast, DiamondBlast,
}

/// <summary>
/// A square: what's in it, and a little more: whether a boulder or diamond is falling, which way a firefly or
/// butterfly faces (0 up, 1 right, 2 down, 3 left), how far through a blast is.
/// </summary>
public struct Cell
{
    public Tile Tile;
    public bool Falling;
    public byte Dir, Stage;
    /// <summary>Moved already this turn (so a thing moved down or right isn't moved again).</summary>
    public bool Done;
    /// <summary>Where it moved from this turn, for drawing it on its way: 0 nowhere, 1 above, 2 the left, 3 the right, 4 below.</summary>
    public byte From;

    public Cell(Tile t) { Tile = t; Falling = false; Dir = 0; Stage = 0; Done = false; }

    /// <summary>Boulders and diamonds roll off it.</summary>
    public readonly bool Rounded => Tile is Tile.Boulder or Tile.Diamond or Tile.Wall or Tile.Magic;
    public readonly bool Enemy => Tile is Tile.Firefly or Tile.Butterfly;
}

/// <summary>
/// A cave, the way the old games kept them: a seed and a table of chances to scatter dirt, boulders, diamonds
/// and space at random, then walls, pens and creatures drawn over the top; a number of diamonds to collect, what
/// each is worth (before and after that), and the time allowed.
/// </summary>
public sealed class CaveDef
{
    public char Letter;
    public string Name = "", Hint = "";
    public int Seed, Needed, Value, Extra, Time;
    /// <summary>What's scattered: each thing with its chance out of 256, tried in order.</summary>
    public (Tile Tile, int Chance)[] Scatter = [];
    /// <summary>Walls, pens and creatures, drawn after the scatter.</summary>
    public Action<Grid> Draw = _ => { };
    /// <summary>How long the slime grows slowly before it speeds up, in seconds.</summary>
    public int SlimeSlow = 40;
    /// <summary>How long a magic wall stays busy once something falls on it, in seconds.</summary>
    public int MagicTime = 20;
    /// <summary>Where Digby comes in, and the way out.</summary>
    public (int X, int Y) Entrance = (2, 2), Exit = (37, 20);
}

/// <summary>A cave's squares, with the drawing tools a cave is described with.</summary>
public sealed class Grid
{
    public readonly Cell[,] Cells = new Cell[Field.Cols, Field.Rows];
    public (int X, int Y) Entrance, Exit;

    public ref Cell this[int x, int y] => ref Cells[x, y];
    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Field.Cols && y < Field.Rows;
    public Tile At(int x, int y) => Inside(x, y) ? Cells[x, y].Tile : Tile.Steel;

    public void Set(Tile t, int x, int y, byte dir = 0)
    {
        if (x <= 0 || y <= 0 || x >= Field.Cols - 1 || y >= Field.Rows - 1) return;
        Cells[x, y] = new Cell(t) { Dir = dir };
    }

    public void Line(Tile t, int x, int y, int length, int dx, int dy)
    {
        for (int i = 0; i < length; i++) Set(t, x + i * dx, y + i * dy);
    }

    /// <summary>A rectangle's outline (or filled).</summary>
    public void Box(Tile t, int x, int y, int w, int h, bool filled = false)
    {
        for (int i = 0; i < w; i++)
        for (int j = 0; j < h; j++)
            if (filled || i == 0 || j == 0 || i == w - 1 || j == h - 1) Set(t, x + i, y + j);
    }

    /// <summary>A pocket of space with a creature in it, circling.</summary>
    public void Pocket(Tile creature, int x, int y, int w = 3, int h = 3)
    {
        Box(Tile.Space, x, y, w, h, filled: true);
        Set(creature, x, y, 3);
    }

    public Grid Copy()
    {
        var g = new Grid { Entrance = Entrance, Exit = Exit };
        Array.Copy(Cells, g.Cells, Cells.Length);
        return g;
    }

    /// <summary>A cave laid out from its description.</summary>
    public static Grid Make(CaveDef def)
    {
        var (entrance, exit) = (def.Entrance, def.Exit);
        var g = new Grid();
        var rng = new Random(def.Seed);
        for (int y = 0; y < Field.Rows; y++)
        for (int x = 0; x < Field.Cols; x++)
        {
            if (x == 0 || y == 0 || x == Field.Cols - 1 || y == Field.Rows - 1) { g.Cells[x, y] = new Cell(Tile.Steel); continue; }
            int r = rng.Next(256);
            var t = Tile.Dirt;
            foreach (var (tile, chance) in def.Scatter)
                if (r < chance) { t = tile; break; }
                else r -= chance;
            g.Cells[x, y] = new Cell(t);
        }
        def.Draw(g);
        // The way in, with dirt round it so nothing drops on Digby as he arrives; and the way out, in the wall.
        g.Entrance = entrance;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            if (g.At(entrance.X + dx, entrance.Y + dy) is not (Tile.Steel or Tile.Wall)) g.Set(Tile.Dirt, entrance.X + dx, entrance.Y + dy);
        g.Set(Tile.Entrance, entrance.X, entrance.Y);
        g.Exit = exit;
        g.Cells[exit.X, exit.Y] = new Cell(Tile.Exit);
        return g;
    }
}
