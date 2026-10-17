namespace AVABoulderDash.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>How a cave is going.</summary>
public enum Phase
{
    /// <summary>The way in flashing; the cave already alive round it.</summary>
    Start,
    /// <summary>Digging.</summary>
    Play,
    /// <summary>Out of the cave: the time left counted into the score.</summary>
    Out,
    /// <summary>Crushed, blown up or out of time: a moment, then the cave again (or the game is over).</summary>
    Dying,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Dig, Step, Boulder, Diamond, Collect, Push, ExitOpen, Explode, Magic, SlimeShut, Appear, Bonus, TimeLow, Die, ExtraLife,
    CaveStart, CaveClear, TitleTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed partial class GameEngine
{
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;
    /// <summary>The caves a game can start from.</summary>
    public static readonly int[] Starts = [0, 2, 4, 6];

    private double _accumulator, _idle;
    private bool _startPrev, _upPrev, _downPrev;
    private bool _tunePending = true;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame(0);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    /// <summary>The cave picked on the title screen to start from (one of <see cref="Starts"/>).</summary>
    public int Choice { get; private set; }

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    /// <summary>One 1/60 s step.</summary>
    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        bool up = c.Dy < 0 && !_upPrev, down = c.Dy > 0 && !_downPrev;
        _upPrev = c.Dy < 0;
        _downPrev = c.Dy > 0;
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }

        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                if (up) { Choice = (Choice + Starts.Length - 1) % Starts.Length; _idle = 0; }
                if (down) { Choice = (Choice + 1) % Starts.Length; _idle = 0; }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame(Starts[Choice]);
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(Starts[Choice]); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 3.5)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame(Starts[Choice]);
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>A new game from a cave.</summary>
    public void StartGame(int cave)
    {
        NewGame(cave);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    /// <summary>The demonstration: the computer digs the first cave.</summary>
    private void StartAttract()
    {
        NewGame(0);
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Effects.Clear();
        Message = SubMessage = null;
        NewGame(Starts[Choice]);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
    }

    public void Resume()
    {
        if (State == GameState.Paused) State = GameState.Playing;
    }

    private void GameOver()
    {
        Play(Sfx.GameOver);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}

public enum EffectKind { Score, Word, Flash }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFE14A;
    public bool Done => Age >= Life;
}
