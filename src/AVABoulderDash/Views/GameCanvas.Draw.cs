using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVABoulderDash.Engine;
using Effect = AVABoulderDash.Engine.Effect;

namespace AVABoulderDash.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVABoulderDash/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Stone = 0xFF3A3448, Moss = 0xFF6AA05A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double T = Field.Tile;
    // The window onto the cave, in squares, eased after Digby.
    private double _camX = -1, _camY = -1;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            DrawCave(dc);
            DrawStrip(dc);
        }
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── the cave ─────────────────────────────

    /// <summary>How far back along the way it came a moving thing is drawn, by how far through the turn it is.</summary>
    private static (double Dx, double Dy) Behind(byte from, double p) => from switch
    {
        1 => (0, -(1 - p)),
        2 => (-(1 - p), 0),
        3 => (1 - p, 0),
        4 => (0, 1 - p),
        _ => (0, 0),
    };

    private void DrawCave(DrawingContext dc)
    {
        var g = Engine;
        var grid = g.Grid;
        double p = Math.Clamp(g.TurnProgress, 0, 1);
        // Digby's place (between squares, on his way), and the window eased after him.
        var digby = grid.Inside(g.X, g.Y) && grid[g.X, g.Y].Tile == Tile.Digby ? Behind(grid[g.X, g.Y].From, p) : (0, 0);
        double wantX = Math.Clamp(g.X + digby.Item1 + 0.5 - Field.ViewCols / 2.0, 0, Field.Cols - Field.ViewCols);
        double wantY = Math.Clamp(g.Y + digby.Item2 + 0.5 - Field.ViewRows / 2.0, 0, Field.Rows - Field.ViewRows);
        if (_camX < 0 || Math.Abs(wantX - _camX) > 8 || Math.Abs(wantY - _camY) > 6) (_camX, _camY) = (wantX, wantY);
        double ox = -_camX * T, oy = Field.Top - _camY * T;
        using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.ViewRows * T)))
        {
            int x0 = (int)Math.Floor(_camX), y0 = (int)Math.Floor(_camY);
            for (int y = Math.Max(0, y0 - 1); y < Math.Min(Field.Rows, y0 + Field.ViewRows + 2); y++)
            for (int x = Math.Max(0, x0 - 1); x < Math.Min(Field.Cols, x0 + Field.ViewCols + 2); x++)
            {
                var c = grid[x, y];
                var (bx, by) = Behind(c.From, p);
                DrawTile(dc, c, x, y, ox + (x + bx) * T, oy + (y + by) * T);
            }
            foreach (var e in g.Effects)
                if (e.Kind == EffectKind.Word)
                    Text(dc, e.Text, new Point(Math.Clamp(ox + (e.X + 0.5) * T, 160, Field.Width - 160), oy + e.Y * T - e.Age * 30), 26, Alpha(e.Colour, Math.Min(1, (1 - e.Age / e.Life) * 2)), center: true);
        }
        foreach (var e in g.Effects)
            if (e.Kind == EffectKind.Flash)
                dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.6 * (1 - e.Age / e.Life))), new Rect(0, Field.Top, Field.Width, Field.ViewRows * T));
    }

    /// <summary>Eases the window towards where it should be (called as the screen's clock moves).</summary>
    private void Follow(double dt)
    {
        var g = Engine;
        if (_camX < 0) return;
        double wantX = Math.Clamp(g.X + 0.5 - Field.ViewCols / 2.0, 0, Field.Cols - Field.ViewCols);
        double wantY = Math.Clamp(g.Y + 0.5 - Field.ViewRows / 2.0, 0, Field.Rows - Field.ViewRows);
        double k = 1 - Math.Exp(-dt * 6);
        _camX += (wantX - _camX) * k;
        _camY += (wantY - _camY) * k;
    }

    private void DrawTile(DrawingContext dc, Cell c, int gx, int gy, double x, double y)
    {
        var g = Engine;
        int turns = g.Turns;
        switch (c.Tile)
        {
            case Tile.Space:
                break;
            case Tile.Dirt:
                dc.FillRectangle(Brush(0xFF6A4220), new Rect(x, y, T, T));
                for (int i = 0; i < 6; i++)
                {
                    uint h = Hash(gx * 7 + i, gy, 3);
                    dc.FillRectangle(Brush(h % 2 == 0 ? 0xFF8A5A2E : 0xFF4A2C14), new Rect(x + h % 28, y + (h >> 8) % 28, 4, 3));
                }
                break;
            case Tile.Wall or Tile.Magic:
            {
                bool magic = c.Tile == Tile.Magic, busy = magic && g.Magic == MagicState.Busy;
                uint brick = magic ? (busy ? ((turns + gx) % 4 < 2 ? 0xFF6A8AF0 : 0xFF9A6AF0) : 0xFF3A4A8A) : 0xFF8A3A2A;
                dc.FillRectangle(Brush(0xFF2A1A14), new Rect(x, y, T, T));
                for (int row = 0; row < 2; row++)
                {
                    double off = (row + gy) % 2 == 0 ? 0 : T / 2;
                    for (double bx = -off; bx < T; bx += T / 2 + 0)
                    {
                        double l = Math.Max(0, bx + 1), r = Math.Min(T, bx + T / 2 - 1);
                        if (r > l) dc.FillRectangle(Brush(brick), new Rect(x + l, y + row * T / 2 + 1, r - l, T / 2 - 2));
                    }
                }
                if (busy) dc.FillRectangle(Brush(0x40FFFFFF), new Rect(x, y + (turns * 4 % (int)T), T, 3));
                break;
            }
            case Tile.Steel:
                dc.FillRectangle(Brush(0xFF5A6070), new Rect(x, y, T, T));
                dc.FillRectangle(Brush(0xFF8A90A0), new Rect(x, y, T, 3));
                dc.FillRectangle(Brush(0xFF8A90A0), new Rect(x, y, 3, T));
                dc.FillRectangle(Brush(0xFF2A2E38), new Rect(x, y + T - 3, T, 3));
                dc.FillRectangle(Brush(0xFF2A2E38), new Rect(x + T - 3, y, 3, T));
                foreach (var (rx, ry) in new[] { (7, 7), (25, 7), (7, 25), (25, 25) })
                    dc.FillEllipse(x + rx, y + ry, 2, 0xFF2A2E38);
                break;
            case Tile.Boulder:
                dc.FillEllipse(x + T / 2, y + T / 2 + 1, T / 2 - 1, 0xFF4A4A50);
                dc.FillEllipse(x + T / 2 - 1, y + T / 2, T / 2 - 3, 0xFF8A8A94);
                dc.FillEllipse(x + T / 2 - 5, y + T / 2 - 6, 6, 0xFFB8B8C4);
                dc.DrawLine(Pen(0xFF5A5A64, 1.5), new Point(x + 12, y + 20), new Point(x + 18, y + 24));
                dc.DrawLine(Pen(0xFF5A5A64, 1.5), new Point(x + 18, y + 24), new Point(x + 24, y + 19));
                break;
            case Tile.Diamond:
                DrawDiamond(dc, x, y, (turns + gx * 3 + gy * 5) % 8);
                break;
            case Tile.Digby:
                DrawDigby(dc, x, y, g.Facing, c.From != 0, turns);
                break;
            case Tile.Firefly:
            {
                double a = (turns * 0.6 + _clock * 4) % (Math.PI * 2), r = 11;
                var pts = Enumerable.Range(0, 4).Select(i => new Point(x + T / 2 + Math.Cos(a + i * Math.PI / 2) * r, y + T / 2 + Math.Sin(a + i * Math.PI / 2) * r)).ToArray();
                Poly(dc, 0, (int)(_clock * 8) % 2 == 0 ? 0xFFFFC030u : 0xFFFF6A20u, 3, pts);
                Poly(dc, 0, 0xFFFFF0A0, 2, pts.Select(q => new Point(x + T / 2 + (q.X - x - T / 2) * 0.5, y + T / 2 + (q.Y - y - T / 2) * 0.5)).ToArray());
                break;
            }
            case Tile.Butterfly:
            {
                double flap = Math.Abs(Math.Sin(_clock * 10 + gx)) * 11 + 3;
                double cx = x + T / 2, cy = y + T / 2;
                Poly(dc, 0xFFD060F0, 0xFF6A2A8A, 1.5, new Point(cx, cy), new Point(cx - flap, cy - 11), new Point(cx - flap, cy + 3));
                Poly(dc, 0xFFD060F0, 0xFF6A2A8A, 1.5, new Point(cx, cy), new Point(cx + flap, cy - 11), new Point(cx + flap, cy + 3));
                Poly(dc, 0xFF60C0F0, 0xFF2A4A8A, 1.5, new Point(cx, cy + 2), new Point(cx - flap * 0.7, cy + 11), new Point(cx, cy + 9));
                Poly(dc, 0xFF60C0F0, 0xFF2A4A8A, 1.5, new Point(cx, cy + 2), new Point(cx + flap * 0.7, cy + 11), new Point(cx, cy + 9));
                dc.FillRectangle(Brush(0xFF2A1A2A), new Rect(cx - 1.5, cy - 9, 3, 18), 1.5f);
                break;
            }
            case Tile.Slime:
            {
                double pulse = Math.Sin(_clock * 4 + gx * 1.3 + gy * 0.7) * 2;
                dc.FillRectangle(Brush(0xFF1E5A1E), new Rect(x, y, T, T));
                dc.FillEllipse(x + T / 2, y + T / 2, T / 2 + pulse, 0xFF3AA03A);
                dc.FillEllipse(x + 10 + Hash(gx, gy, 1) % 10, y + 10 + Hash(gx, gy, 2) % 10, 4 + pulse / 2, 0xFF8AF08A);
                break;
            }
            case Tile.Entrance:
                if (g.Phase == Phase.Start && (int)(g.PhaseTime * 6) % 2 == 0) DrawTile(dc, new Cell(Tile.Steel), gx, gy, x, y);
                else
                {
                    dc.FillRectangle(Brush(0xFF2A2E38), new Rect(x, y, T, T));
                    dc.DrawRectangle(Pen(0xFF8AF08A, 2), new Rect(x + 3, y + 3, T - 6, T - 6));
                }
                break;
            case Tile.Exit:
                if (g.ExitOpen && (int)(_clock * 4) % 2 == 0)
                {
                    dc.FillRectangle(Brush(0xFF0A0A0A), new Rect(x, y, T, T));
                    dc.DrawRectangle(Pen(0xFFFFFFFF, 3), new Rect(x + 2, y + 2, T - 4, T - 4));
                }
                else DrawTile(dc, new Cell(Tile.Steel), gx, gy, x, y);
                break;
            case Tile.Blast or Tile.DiamondBlast:
            {
                double s = (c.Stage + 1) / GameEngine.BlastTurns;
                uint col = c.Tile == Tile.Blast ? 0xFFFFA030 : 0xFF8FF0FF;
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4 + c.Stage;
                    dc.DrawLine(Pen(Alpha(col, 1 - s * 0.6), 3), new Point(x + T / 2, y + T / 2), new Point(x + T / 2 + Math.Cos(a) * T * 0.5 * s, y + T / 2 + Math.Sin(a) * T * 0.5 * s));
                }
                dc.FillEllipse(x + T / 2, y + T / 2, T * 0.35 * (1 - s * 0.5), Alpha(0xFFFFFFFF, 1 - s));
                break;
            }
        }
    }

    private static void DrawDiamond(DrawingContext dc, double x, double y, int twinkle)
    {
        double cx = x + T / 2;
        Poly(dc, 0xFF4AC8F0, 0xFF1A6A9A, 1.5, new Point(cx, y + 4), new Point(x + T - 5, y + 12), new Point(cx, y + T - 3), new Point(x + 5, y + 12));
        Poly(dc, 0xFF9AE8FF, null, 0, new Point(cx, y + 4), new Point(x + T - 5, y + 12), new Point(cx, y + 12));
        Poly(dc, 0xFF2A9AC8, null, 0, new Point(x + 5, y + 12), new Point(cx, y + 12), new Point(cx, y + T - 3));
        if (twinkle < 2)
        {
            double sx = x + 10 + twinkle * 10, sy = y + 9;
            dc.DrawLine(Pen(0xFFFFFFFF, 1.5), new Point(sx - 4, sy), new Point(sx + 4, sy));
            dc.DrawLine(Pen(0xFFFFFFFF, 1.5), new Point(sx, sy - 4), new Point(sx, sy + 4));
        }
    }

    /// <summary>Digby: a small digger in a yellow helmet with a lamp, overalls and boots.</summary>
    private void DrawDigby(DrawingContext dc, double x, double y, int facing, bool moving, int turns)
    {
        double cx = x + T / 2, swing = moving ? Math.Sin(_clock * 18) * 3 : 0;
        bool tap = !moving && turns % 16 > 12;
        // Boots and legs.
        dc.FillRectangle(Brush(0xFF2A4AA0), new Rect(cx - 6 + swing, y + 20, 5, 8));
        dc.FillRectangle(Brush(0xFF2A4AA0), new Rect(cx + 1 - swing, y + 20, 5, 8));
        dc.FillRectangle(Brush(0xFF3A2414), new Rect(cx - 7 + swing, y + 27 - (tap ? 1 : 0), 7, 4));
        dc.FillRectangle(Brush(0xFF3A2414), new Rect(cx - swing, y + 27, 7, 4));
        // Body, arms.
        dc.FillRectangle(Brush(0xFF3A6AD0), new Rect(cx - 7, y + 11, 14, 11), 2);
        dc.FillRectangle(Brush(0xFFE0A030), new Rect(cx - 7, y + 15, 14, 2));
        dc.DrawLine(Pen(0xFFF0C8A0, 3), new Point(cx - 7, y + 13), new Point(cx - 10 - swing, y + 20));
        dc.DrawLine(Pen(0xFFF0C8A0, 3), new Point(cx + 7, y + 13), new Point(cx + 10 + swing, y + 20));
        // Head, helmet and lamp.
        dc.FillEllipse(cx, y + 8, 6, 0xFFF0C8A0);
        dc.FillEllipse(cx + facing * 2.5, y + 8, 1.2, 0xFF101010);
        if (turns % 40 == 0) dc.DrawLine(Pen(0xFF101010, 1), new Point(cx + facing * 2.5 - 1.5, y + 8), new Point(cx + facing * 2.5 + 1.5, y + 8));
        dc.FillRectangle(Brush(0xFFF0D020), new Rect(cx - 7, y + 1, 14, 5), 2);
        dc.FillRectangle(Brush(0xFFF0D020), new Rect(cx - 8, y + 5, 16, 2));
        dc.FillEllipse(cx + facing * 4, y + 3, 2.2, 0xFFFFFFC0);
    }

    // ───────────────────────────── around the edges ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF100C08), new Rect(0, 0, Field.Width, Field.Hud));
        dc.DrawLine(Pen(0xFF5A4A2A, 2), new Point(0, Field.Hud), new Point(Field.Width, Field.Hud));
        if (!InGame)
        {
            Text(dc, "SCORE " + g.Score.ToString("000000"), new Point(24, 40), 30, Yellow);
            Text(dc, "HI " + Math.Max(g.HighScores.TopScore, g.Score).ToString("000000"), new Point(Field.Width - 24, 40), 30, Dim, right: true);
            return;
        }
        // Diamonds: how many in, how many needed, and what the next is worth.
        DrawDiamond(dc, 20, 24, (int)(_clock * 6) % 8);
        string got = g.ExitOpen ? $"{g.Diamonds}" : $"{g.Diamonds}/{g.Needed}";
        Text(dc, got, new Point(60, 40), 40, g.ExitOpen ? 0xFF8FFF8F : White);
        Text(dc, $"WORTH {g.Value}", new Point(200, 40), 26, Dim);
        bool low = g.TimeLeft <= 10 && g.Phase == Phase.Play;
        Text(dc, "TIME", new Point(Field.Width / 2 - 70, 40), 26, Dim);
        Text(dc, ((int)Math.Ceiling(g.TimeLeft)).ToString("000"), new Point(Field.Width / 2 + 20, 40), 44, low && (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
        for (int i = 0; i < Math.Min(g.Lives, 6); i++) DrawDigby(dc, 640 + i * 30, 24, 1, false, 1);
        Text(dc, g.Score.ToString("000000"), new Point(Field.Width - 24, 40), 40, Yellow, right: true);
    }

    private void DrawStrip(DrawingContext dc)
    {
        var g = Engine;
        double y = Field.Bottom;
        dc.FillRectangle(Brush(0xFF100C08), new Rect(0, y, Field.Width, Field.Height - y));
        dc.DrawLine(Pen(0xFF5A4A2A, 2), new Point(0, y), new Point(Field.Width, y));
        Text(dc, $"CAVE {g.Def.Letter}", new Point(24, y + 26), 30, Yellow);
        Text(dc, g.Def.Name, new Point(140, y + 26), 30, White);
        if (g.Round > 0) Text(dc, $"ROUND {g.Round + 1}", new Point(24, y + 56), 22, Dim);
        string note = g.ExitOpen ? "THE WAY OUT IS OPEN" : g.Magic == MagicState.Busy ? "THE MAGIC WALL HUMS" : g.Slime > 0 ? $"SLIME {g.Slime}" : "";
        if (note.Length > 0) Text(dc, note, new Point(Field.Width - 24, y + 26), 26, g.ExitOpen && (int)(_clock * 3) % 2 == 0 ? 0xFF8FFF8F : Dim, right: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Top + 200;
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 420, cy - 50, 840, 104), 10);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 20 ? 44 : 56, msg == "OUT OF TIME" ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 32), 24, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        // A wall of dirt and boulders, a band of diamonds.
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 32; x++)
        {
            var t = Hash(x, y, 9) % 5 == 0 ? Tile.Boulder : Tile.Dirt;
            DrawTile(dc, new Cell(t), x, y, x * T, Field.Hud + 8 + y * T);
        }
        string title = "AVA BOULDER DASH";
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 24 + 24 + i * 48, 250 + Math.Sin(_clock * 3 + i * 0.4) * 3), 84, i % 2 == 0 ? 0xFFF0D020 : 0xFFFFF0A0, center: true);
        Text(dc, "AFTER THE 1984 COMMODORE 64 CLASSIC", new Point(cx, 304), 24, Dim, center: true);
        for (int i = 0; i < 9; i++) DrawDiamond(dc, cx - 9 * 22 + i * 44, 326, ((int)(_clock * 8) + i * 3) % 8);
        Text(dc, "START AT CAVE", new Point(cx, 396), 28, White, center: true);
        for (int i = 0; i < GameEngine.Starts.Length; i++)
        {
            var def = CaveBook.All[GameEngine.Starts[i]];
            bool on = i == g.Choice;
            double y = 436 + i * 36;
            if (on) dc.FillRectangle(Brush(0x80403010), new Rect(cx - 230, y - 16, 460, 32), 6);
            Text(dc, $"{(on ? "> " : "  ")}{def.Letter}  {def.Name}", new Point(cx - 200, y), 28, on ? Yellow : White);
        }
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER TO DIG", new Point(cx, 616), 40, Yellow, center: true);
        Text(dc, "ARROWS: MOVE AND DIG    HOLD SPACE + ARROW: GRAB WITHOUT MOVING    PUSH BOULDERS SIDEWAYS", new Point(cx, 664), 20, 0xFFC0A880, center: true);
        Text(dc, "COLLECT ENOUGH DIAMONDS AND THE WAY OUT OPENS.    P: PAUSE    M: SOUND", new Point(cx, 692), 20, 0xFFC0A880, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF0A0806), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "THE GREAT DIGGERS", new Point(cx, 140), 64, 0xFFF0D020, center: true);
        Text(dc, "RANK NAME    SCORE  CAVES", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF0A0806), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
}
