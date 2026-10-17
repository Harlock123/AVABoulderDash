namespace AVABoulderDash.Engine;

/// <summary>
/// The computer player, for the demonstration and the tests. Each turn it finds the nearest diamonds it can dig
/// its way to (or the way out, once it's open), and before taking a step it tries the next few turns out on a
/// copy of the cave: a step that ends under a boulder, or next to a firefly, isn't taken.
/// </summary>
public sealed class Pilot
{
    private int _turnSeen = -1;
    private Controls _choice;
    private readonly Dictionary<(int, int), int> _shunned = [];
    private readonly Queue<(int, int)> _trail = new();
    private (int X, int Y)? _target;
    private int _waited;

    /// <summary>What it's doing now, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _turnSeen = -1;
        _choice = default;
        _shunned.Clear();
        _trail.Clear();
        _target = null;
        Doing = "";
    }

    public Controls Decide(GameEngine g)
    {
        if (g.State is not (GameState.Playing or GameState.Attract) || g.Phase != Phase.Play || !g.Alive) { _turnSeen = -1; return default; }
        // Decide once a turn, early in it, and hold that until the turn comes.
        if (g.Turns == _turnSeen) return _choice;
        _turnSeen = g.Turns;
        _choice = Choose(g);
        return _choice;
    }

    private static readonly (int Dx, int Dy)[] Ways = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    private static bool Passable(GameEngine g, int x, int y)
    {
        var c = g.Grid.Inside(x, y) ? g.Grid[x, y] : new Cell(Tile.Steel);
        return c.Tile switch
        {
            Tile.Space or Tile.Dirt => true,
            Tile.Diamond => !c.Falling,
            Tile.Exit => g.ExitOpen,
            _ => false,
        };
    }

    /// <summary>A square beside a firefly or butterfly, or under something falling.</summary>
    private static bool Risky(GameEngine g, int x, int y)
    {
        foreach (var (dx, dy) in Ways)
            if (g.Grid.At(x + dx, y + dy) is Tile.Firefly or Tile.Butterfly) return true;
        return g.Grid.Inside(x, y - 1) && g.Grid[x, y - 1].Falling;
    }

    private Controls Choose(GameEngine g)
    {
        if (Doing != "waiting") _waited = 0;
        foreach (var k in _shunned.Keys.ToList())
            if (--_shunned[k] <= 0) _shunned.Remove(k);
        _trail.Enqueue((g.X, g.Y));
        if (_trail.Count > 40) _trail.Dequeue();

        // Every square Digby can dig his way to, and the way there.
        var from = new Dictionary<(int, int), (int, int)>();
        var order = new List<(int X, int Y)>();
        var todo = new Queue<(int X, int Y)>();
        todo.Enqueue((g.X, g.Y));
        from[(g.X, g.Y)] = (g.X, g.Y);
        while (todo.Count > 0)
        {
            var (x, y) = todo.Dequeue();
            order.Add((x, y));
            foreach (var (dx, dy) in Ways)
            {
                int nx = x + dx, ny = y + dy;
                if (from.ContainsKey((nx, ny)) || !Passable(g, nx, ny) || Risky(g, nx, ny)) continue;
                from[(nx, ny)] = (x, y);
                todo.Enqueue((nx, ny));
            }
        }
        List<(int X, int Y)> Path((int X, int Y) to)
        {
            var path = new List<(int, int)>();
            for (var at = to; at != (g.X, g.Y); at = from[at]) path.Add(at);
            path.Reverse();
            return path;
        }

        bool Goal((int X, int Y) s) => g.ExitOpen ? s == g.Grid.Exit : g.Grid[s.X, s.Y].Tile == Tile.Diamond;
        var targets = order.Skip(1).Where(s => Goal(s) && !_shunned.ContainsKey(s)).Take(8).ToList();
        // Keep to the one it was making for, while it's still there to be had (so as not to dither between two).
        if (_target is { } kept && targets.Remove(kept)) targets.Insert(0, kept);
        foreach (var t in targets)
        {
            var path = Path(t);
            var first = path[0];
            var c = new Controls(first.X - g.X, first.Y - g.Y);
            if (Safe(g, path))
            {
                Doing = g.ExitOpen ? "to the way out" : "to a diamond";
                _target = t;
                return c;
            }
            _shunned[t] = 6;
        }
        _target = null;
        // Nothing to make for safely: dig towards the nearest diamond anywhere, or push a boulder, or wait.
        var options = new List<(Controls C, string Why)>();
        var nearest = Nearest(g);
        foreach (var (dx, dy) in Ways.OrderBy(w => nearest is { } n ? Math.Abs(g.X + w.Dx - n.X) + Math.Abs(g.Y + w.Dy - n.Y) : 0))
        {
            int nx = g.X + dx, ny = g.Y + dy;
            if (Passable(g, nx, ny) && !_trail.Contains((nx, ny))) options.Add((new Controls(dx, dy), "digging about"));
            else if (dy == 0 && g.Grid.At(nx, ny) == Tile.Boulder && g.Grid.At(nx + dx, ny) == Tile.Space) options.Add((new Controls(dx, 0), "pushing"));
        }
        foreach (var (dx, dy) in Ways)
            if (Passable(g, g.X + dx, g.Y + dy)) options.Add((new Controls(dx, dy), "wandering"));
        foreach (var (c, why) in options)
            if (g.Survives([c, c, default, default, default, default], Free))
            {
                Doing = why;
                return c;
            }
        // Nothing safe to do for five seconds: shut in, most likely. Start the cave again.
        Doing = "waiting";
        if (++_waited > 40) { _waited = 0; g.GiveUp(); }
        return default;
    }

    /// <summary>Along the way (up to twenty steps), then standing still a few turns: alive at the end of it?</summary>
    private static bool Safe(GameEngine g, List<(int X, int Y)> path)
    {
        var turns = new List<Controls>();
        var at = (g.X, g.Y);
        foreach (var step in path.Take(20))
        {
            turns.Add(new Controls(step.X - at.X, step.Y - at.Y));
            at = step;
        }
        for (int i = 0; i < 3; i++) turns.Add(default);
        return g.Survives(turns, Free);
    }

    /// <summary>Not shut in: there's somewhere to step, or a boulder to push, from where Digby stands.</summary>
    public static bool Free(GameEngine g)
    {
        if (!g.Alive) return true;
        foreach (var (dx, dy) in Ways)
        {
            int x = g.X + dx, y = g.Y + dy;
            if (Passable(g, x, y)) return true;
            if (dy == 0 && g.Grid.At(x, y) == Tile.Boulder && g.Grid.At(x + dx, y) == Tile.Space) return true;
        }
        return false;
    }

    private static (int X, int Y)? Nearest(GameEngine g)
    {
        (int, int)? best = null;
        int bestD = int.MaxValue;
        for (int y = 0; y < Field.Rows; y++)
        for (int x = 0; x < Field.Cols; x++)
            if (g.ExitOpen ? (x, y) == g.Grid.Exit : g.Grid[x, y].Tile == Tile.Diamond)
            {
                int d = Math.Abs(x - g.X) + Math.Abs(y - g.Y);
                if (d < bestD) { bestD = d; best = (x, y); }
            }
        return best;
    }
}
