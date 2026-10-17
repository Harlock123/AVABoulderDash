namespace AVABoulderDash.Engine;

public enum MagicState { Still, Busy, Spent }

public sealed partial class GameEngine
{
    /// <summary>The cave moves in turns: everything in it moves at most one square a turn.</summary>
    public const double TurnSeconds = 0.125;
    public const double StartSeconds = 2.0, DyingSeconds = 2.5, BlastTurns = 4;
    public const int Lives0 = 3, LifeEvery = 500, BonusPerSecond = 5, SlimeLimit = 200;
    /// <summary>Turns spent pushing before a boulder gives.</summary>
    public const int PushTurns = 2;

    public Pilot Pilot { get; } = new();
    public int Score { get; private set; }
    public int Lives { get; private set; }
    /// <summary>Caves cleared this game.</summary>
    public int Cleared { get; private set; }
    public int CaveIndex { get; private set; }
    /// <summary>Times round all eight caves (each time round has less time).</summary>
    public int Round { get; private set; }
    public CaveDef Def { get; private set; } = CaveBook.All[0];
    public Grid Grid { get; private set; } = new();
    public int Diamonds { get; private set; }
    public int Needed => Def.Needed;
    /// <summary>What the next diamond is worth.</summary>
    public int Value => ExitOpen ? Def.Extra : Def.Value;
    public bool ExitOpen { get; private set; }
    public double TimeLeft { get; private set; }
    /// <summary>Where Digby is (or was), and which way he last went.</summary>
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Facing { get; private set; } = 1;
    public bool Alive { get; private set; }
    public string Cause { get; private set; } = "";
    public MagicState Magic { get; private set; }
    public double MagicLeft { get; private set; }
    public int Slime { get; private set; }
    /// <summary>How far through the current turn (0 to 1), for drawing things on their way.</summary>
    public double TurnProgress => _turn / TurnSeconds;
    /// <summary>Turns since the cave began (it drives the flashing and twinkling).</summary>
    public int Turns { get; private set; }
    public double SlimeTime { get; private set; }

    private double _turn;
    private int _push, _nextLife;
    private Random _rng = new(1);
    // The direction asked for since the last turn (a quick tap between turns still counts).
    private int _askDx, _askDy;
    private bool _askFire;

    private void NewGame(int cave)
    {
        Score = 0;
        Lives = Lives0;
        Cleared = 0;
        Round = 0;
        _nextLife = LifeEvery;
        Begin(cave);
    }

    /// <summary>Into a cave (again): laid out fresh, the clock full, the way in flashing.</summary>
    private void Begin(int cave)
    {
        CaveIndex = cave;
        Def = CaveBook.All[cave];
        Load(Grid.Make(Def), Def);
    }

    private void Load(Grid grid, CaveDef def)
    {
        Def = def;
        Grid = grid;
        Diamonds = 0;
        ExitOpen = def.Needed <= 0;
        // Each time round, twenty seconds less (but never under half a minute).
        TimeLeft = Math.Max(Math.Min(def.Time, 30), def.Time - Round * 20);
        (X, Y) = Grid.Entrance;
        Alive = false;
        Magic = MagicState.Still;
        MagicLeft = 0;
        SlimeTime = 0;
        Turns = 0;
        _turn = 0;
        _push = 0;
        _rng = new Random(def.Seed * 7 + 1);
        Slime = Count(Tile.Slime);
        SetPhase(Phase.Start);
        Say($"CAVE {def.Letter}: {def.Name}", def.Hint, StartSeconds + 0.8);
        Play(Sfx.CaveStart);
    }

    private int Count(Tile t)
    {
        int n = 0;
        foreach (var c in Grid.Cells) if (c.Tile == t) n++;
        return n;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        // Remember what's asked for until the turn comes.
        if (c.Dx != 0 || c.Dy != 0) { _askDx = c.Dx; _askDy = c.Dx != 0 ? 0 : c.Dy; }
        _askFire |= c.Fire;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime >= StartSeconds)
                {
                    Grid[X, Y] = new Cell(Tile.Digby);
                    Alive = true;
                    SetPhase(Phase.Play);
                    Play(Sfx.Appear);
                }
                break;
            case Phase.Play:
                double before = TimeLeft;
                TimeLeft = Math.Max(0, TimeLeft - dt);
                if (TimeLeft <= 10 && (int)before != (int)TimeLeft) Play(Sfx.TimeLow);
                if (TimeLeft <= 0 && Alive) { Die("out of time"); Explode(X, Y, toDiamonds: false); }
                break;
            case Phase.Out:
                // The time left, counted into the score.
                if (TimeLeft > 0)
                {
                    double take = Math.Min(TimeLeft, dt * 60);
                    int whole = (int)Math.Ceiling(TimeLeft) - (int)Math.Ceiling(TimeLeft - take);
                    TimeLeft -= take;
                    if (whole > 0) { Award(whole * BonusPerSecond); Play(Sfx.Bonus); }
                }
                else if (PhaseTime > 1.5) Next();
                return;
            case Phase.Dying:
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) GameOver();
                    else Begin(CaveIndex);
                    return;
                }
                break;
        }
        _turn += dt;
        if (_turn >= TurnSeconds)
        {
            _turn -= TurnSeconds;
            Turn(new Controls(c.Dx != 0 || c.Dy != 0 ? c.Dx : _askDx, c.Dx != 0 || c.Dy != 0 ? (c.Dx != 0 ? 0 : c.Dy) : _askDy, c.Fire || _askFire));
            _askDx = _askDy = 0;
            _askFire = false;
        }
    }

    /// <summary>On to the next cave (round to the first again, with less time, after the last).</summary>
    private void Next()
    {
        Cleared++;
        int next = CaveIndex + 1;
        if (next >= CaveBook.All.Length) { next = 0; Round++; }
        Begin(next);
    }

    // ───────────────────────────── a turn ─────────────────────────────

    /// <summary>
    /// One turn of the cave: every square looked at once, top to bottom and left to right, as the old games did
    /// it; a thing moved down or right is marked so it isn't moved twice.
    /// </summary>
    private void Turn(Controls c)
    {
        Turns++;
        var g = Grid;
        for (int y = 0; y < Field.Rows; y++)
        for (int x = 0; x < Field.Cols; x++)
        {
            g[x, y].Done = false;
            g[x, y].From = 0;
        }
        if (Magic == MagicState.Busy && (MagicLeft -= TurnSeconds) <= 0) Magic = MagicState.Spent;
        if (Phase == Phase.Play) SlimeTime += TurnSeconds;
        bool slimeCanGrow = false;
        int slime = 0;
        for (int y = 0; y < Field.Rows; y++)
        for (int x = 0; x < Field.Cols; x++)
        {
            ref var cell = ref g[x, y];
            if (cell.Done) continue;
            switch (cell.Tile)
            {
                case Tile.Boulder or Tile.Diamond:
                    Fall(x, y);
                    break;
                case Tile.Firefly or Tile.Butterfly:
                    Creature(x, y);
                    break;
                case Tile.Digby:
                    if (Alive) DigbyTurn(x, y, c);
                    break;
                case Tile.Blast or Tile.DiamondBlast:
                    if (++cell.Stage >= BlastTurns) cell = new Cell(cell.Tile == Tile.DiamondBlast ? Tile.Diamond : Tile.Space) { Done = true };
                    break;
                case Tile.Slime:
                    slime++;
                    if (Grows(x, y, ref slimeCanGrow)) slime++;
                    break;
            }
        }
        SlimeTurn(slime, slimeCanGrow);
    }

    // ───────────────────────────── boulders and diamonds ─────────────────────────────

    private void Fall(int x, int y)
    {
        var g = Grid;
        ref var c = ref g[x, y];
        var below = g.At(x, y + 1);
        if (below == Tile.Space)
        {
            Move(x, y, x, y + 1, falling: true);
            return;
        }
        if (c.Falling)
        {
            switch (below)
            {
                case Tile.Digby or Tile.Firefly or Tile.Butterfly:
                    // Landed on something alive: it goes up, and takes the square round it.
                    if (below == Tile.Digby) Die("crushed");
                    Explode(x, y + 1, toDiamonds: below == Tile.Butterfly);
                    return;
                case Tile.Magic when Magic != MagicState.Spent:
                    if (Magic == MagicState.Still) { Magic = MagicState.Busy; MagicLeft = Def.MagicTime; }
                    var through = c.Tile == Tile.Boulder ? Tile.Diamond : Tile.Boulder;
                    c = new Cell(Tile.Space);
                    if (g.At(x, y + 2) == Tile.Space) g[x, y + 2] = new Cell(through) { Falling = true, Done = true, From = 1 };
                    Play(Sfx.Magic);
                    return;
            }
            c.Falling = false;
            Play(c.Tile == Tile.Boulder ? Sfx.Boulder : Sfx.Diamond);
        }
        // Off the top of something round, if there's room to the side and below it.
        if (!g[x, y + 1].Rounded || g[x, y + 1].Falling) return;
        if (g.At(x - 1, y) == Tile.Space && g.At(x - 1, y + 1) == Tile.Space) Move(x, y, x - 1, y, falling: true);
        else if (g.At(x + 1, y) == Tile.Space && g.At(x + 1, y + 1) == Tile.Space) Move(x, y, x + 1, y, falling: true);
    }

    /// <summary>A thing from one square to the next, marked as moved.</summary>
    private void Move(int x, int y, int nx, int ny, bool falling = false)
    {
        var c = Grid[x, y];
        c.Done = true;
        c.Falling = falling;
        c.From = (byte)(ny > y ? 1 : nx > x ? 2 : nx < x ? 3 : 4);
        Grid[nx, ny] = c;
        Grid[x, y] = new Cell(Tile.Space);
    }

    // ───────────────────────────── fireflies and butterflies ─────────────────────────────

    private static readonly (int Dx, int Dy)[] Ways = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>
    /// A firefly keeps a wall on its left (it turns left when it can, else goes on, else turns right); a
    /// butterfly the other way round. Either blows up when it touches Digby or the slime.
    /// </summary>
    private void Creature(int x, int y)
    {
        var g = Grid;
        ref var c = ref g[x, y];
        bool firefly = c.Tile == Tile.Firefly;
        foreach (var (dx, dy) in Ways)
            if (g.At(x + dx, y + dy) is Tile.Digby or Tile.Slime)
            {
                if (g.At(x + dx, y + dy) == Tile.Digby && !Alive) continue;
                Explode(x, y, toDiamonds: !firefly);
                return;
            }
        int d = c.Dir, turn = firefly ? (d + 3) % 4 : (d + 1) % 4, other = firefly ? (d + 1) % 4 : (d + 3) % 4;
        if (Free(x, y, turn)) { c.Dir = (byte)turn; Step(x, y, turn); }
        else if (Free(x, y, d)) Step(x, y, d);
        else c.Dir = (byte)other;
    }

    private bool Free(int x, int y, int d) => Grid.At(x + Ways[d].Dx, y + Ways[d].Dy) == Tile.Space;
    private void Step(int x, int y, int d) => Move(x, y, x + Ways[d].Dx, y + Ways[d].Dy);

    // ───────────────────────────── the slime ─────────────────────────────

    /// <summary>A square of slime may spread into dirt or space beside it; whether any of it could is noted.</summary>
    private bool Grows(int x, int y, ref bool canGrow)
    {
        var g = Grid;
        bool room = false;
        foreach (var (dx, dy) in Ways)
            if (g.At(x + dx, y + dy) is Tile.Space or Tile.Dirt) room = true;
        if (!room) return false;
        canGrow = true;
        int chance = SlimeTime < Def.SlimeSlow ? 4 : 30;
        if (_rng.Next(128) >= chance) return false;
        var (wx, wy) = Ways[_rng.Next(4)];
        if (g.At(x + wx, y + wy) is not (Tile.Space or Tile.Dirt)) return false;
        g[x + wx, y + wy] = new Cell(Tile.Slime) { Done = true };
        return true;
    }

    /// <summary>Shut in, the slime turns to diamonds; grown too big, to boulders.</summary>
    private void SlimeTurn(int count, bool canGrow)
    {
        Slime = count;
        if (count == 0) return;
        Tile? into = count >= SlimeLimit ? Tile.Boulder : !canGrow ? Tile.Diamond : null;
        if (into is not { } t) return;
        for (int y = 0; y < Field.Rows; y++)
        for (int x = 0; x < Field.Cols; x++)
            if (Grid[x, y].Tile == Tile.Slime) Grid[x, y] = new Cell(t);
        Slime = 0;
        Play(Sfx.SlimeShut);
        Word(t == Tile.Diamond ? "THE SLIME TURNS TO DIAMONDS!" : "THE SLIME TURNS TO STONE", X, Y - 1, t == Tile.Diamond ? 0xFF8FF0FF : 0xFFB0B0B0);
    }

    // ───────────────────────────── Digby ─────────────────────────────

    private void DigbyTurn(int x, int y, Controls c)
    {
        int dx = c.Dx, dy = c.Dx != 0 ? 0 : c.Dy;
        if (dx == 0 && dy == 0) { _push = 0; return; }
        if (dx != 0) Facing = dx;
        int tx = x + dx, ty = y + dy;
        var g = Grid;
        ref var target = ref g[tx, ty];
        bool grab = c.Fire;
        switch (target.Tile)
        {
            case Tile.Space or Tile.Dirt:
                Play(target.Tile == Tile.Dirt ? Sfx.Dig : Sfx.Step);
                if (grab) target = new Cell(Tile.Space);
                else Go(x, y, tx, ty);
                break;
            case Tile.Diamond when !target.Falling || dy != -1:
                Collect(tx, ty);
                if (grab) target = new Cell(Tile.Space);
                else Go(x, y, tx, ty);
                break;
            case Tile.Exit when ExitOpen && !grab:
                g[x, y] = new Cell(Tile.Space);
                (X, Y) = (tx, ty);
                Alive = false;
                SetPhase(Phase.Out);
                Play(Sfx.CaveClear);
                Say("OUT!", $"CAVE {Def.Letter} CLEARED", 2.5);
                break;
            case Tile.Boulder when dy == 0 && !target.Falling && g.At(tx + dx, ty) == Tile.Space:
                // Pushing: it gives after a moment's shove.
                if (++_push < PushTurns) break;
                _push = 0;
                Move(tx, ty, tx + dx, ty);
                Play(Sfx.Push);
                if (!grab) Go(x, y, tx, ty);
                return;
        }
        if (target.Tile != Tile.Boulder) _push = 0;
    }

    private void Go(int x, int y, int tx, int ty)
    {
        Move(x, y, tx, ty);
        (X, Y) = (tx, ty);
    }

    private void Collect(int x, int y)
    {
        Award(Value);
        Diamonds++;
        Play(Sfx.Collect);
        if (!ExitOpen && Diamonds >= Def.Needed)
        {
            ExitOpen = true;
            Play(Sfx.ExitOpen);
            Effects.Add(new Effect { Kind = EffectKind.Flash, Life = 0.25 });
            Word("THE WAY OUT IS OPEN", Grid.Exit.X, Grid.Exit.Y - 1, 0xFF8FFF8F);
        }
    }

    private void Award(int points)
    {
        Score += points;
        while (Score >= _nextLife)
        {
            _nextLife += LifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
            Word("EXTRA LIFE", X, Y - 1, 0xFF8FFF8F);
        }
    }

    // ───────────────────────────── blasts, and losing a life ─────────────────────────────

    /// <summary>The square and the eight round it go up (all but steel and the doors); a butterfly leaves diamonds.</summary>
    private void Explode(int cx, int cy, bool toDiamonds)
    {
        var g = Grid;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int x = cx + dx, y = cy + dy;
            if (!g.Inside(x, y) || g.At(x, y) is Tile.Steel or Tile.Entrance or Tile.Exit) continue;
            if (g.At(x, y) == Tile.Digby && Alive) Die("blown up");
            g[x, y] = new Cell(toDiamonds ? Tile.DiamondBlast : Tile.Blast) { Done = true };
        }
        Play(Sfx.Explode);
    }

    /// <summary>Stuck fast: give up this try at the cave (it costs a life, as being caught would).</summary>
    public void GiveUp()
    {
        if (!Alive || Phase != Phase.Play || State is not (GameState.Playing or GameState.Attract)) return;
        Die("gave up");
        Explode(X, Y, toDiamonds: false);
    }

    private void Die(string cause)
    {
        if (!Alive) return;
        Alive = false;
        Cause = cause;
        Lives--;
        SetPhase(Phase.Dying);
        Play(Sfx.Die);
        if (cause == "out of time") Say("OUT OF TIME", null, DyingSeconds);
    }

    private void Word(string text, double x, double y, uint colour, double life = 1.6) =>
        Effects.Add(new Effect { Kind = EffectKind.Word, X = x, Y = y, Text = text, Colour = colour, Life = life });

    // ───────────────────────────── looking ahead (for the computer player) ─────────────────────────────

    /// <summary>
    /// Whether Digby would still be alive after these controls, one set a turn, tried out on a copy of the cave (and,
    /// if asked, whether things are as wanted at the end); everything is put back after.
    /// </summary>
    public bool Survives(IReadOnlyList<Controls> turns, Func<GameEngine, bool>? after = null)
    {
        if (!Alive || Phase != Phase.Play) return true;
        var saved = (Grid.Copy(), X, Y, Facing, Alive, Diamonds, ExitOpen, Magic, MagicLeft, SlimeTime, Turns, _push, Score, Lives, Cause, _nextLife, Slime);
        var said = (Message, SubMessage, MessageTime);
        var rng = _rng;
        _rng = new Random(Turns * 31 + 7);
        var phase = (Phase, PhaseTime);
        bool silent = Silent;
        Silent = true;
        int effects = Effects.Count;
        bool alive = true;
        try
        {
            foreach (var c in turns)
            {
                Turn(c);
                if (!Alive) { alive = Phase == Phase.Out; break; }
            }
            if (alive && Alive && after != null) alive = after(this);
        }
        finally
        {
            (Grid, X, Y, Facing, Alive, Diamonds, ExitOpen, Magic, MagicLeft, SlimeTime, Turns, _push, Score, Lives, Cause, _nextLife, Slime) = saved;
            _rng = rng;
            (Phase, PhaseTime) = phase;
            (Message, SubMessage, MessageTime) = said;
            Silent = silent;
            if (Effects.Count > effects) Effects.RemoveRange(effects, Effects.Count - effects);
        }
        return alive;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    /// <summary>Runs a number of steps with the same controls.</summary>
    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a game at a cave, Digby already out of the way in.</summary>
    public void Go(int cave = 0)
    {
        StartGame(cave);
        Run((int)(StartSeconds * 60) + 2);
        Message = null;
    }

    /// <summary>Into a cave made for a test, Digby out at its way in.</summary>
    public void Try(Grid grid, CaveDef def)
    {
        State = GameState.Playing;
        Load(grid, def);
        Run((int)(StartSeconds * 60) + 2);
        Message = null;
    }

    /// <summary>One whole turn of the cave, with these controls.</summary>
    public void Step(Controls c = default)
    {
        int ticks = (int)Math.Ceiling((TurnSeconds - _turn) / Field.Step);
        for (int i = 0; i < ticks; i++) Tick(i == 0 ? c : c.Dx != 0 || c.Dy != 0 || c.Fire ? c : default);
    }

    public void SetScore(int score) => Score = score;
    public void SetLives(int lives) => Lives = lives;
    public void SetTime(double seconds) => TimeLeft = seconds;
    public void SetDiamonds(int n) => Diamonds = n;
}
