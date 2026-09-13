using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace SnakeApp
{
    internal enum GameState { Greeting, Playing, Paused, GameOver }

    internal sealed class SnakeForm : Form
    {
        // ---- board geometry ----
        private const int Cols = 24;
        private const int Rows = 24;
        private const int Cell = 24;
        private const int Pad = 20;
        private const int HeaderH = 70;
        private const int FooterH = 48;

        private const int BoardW = Cols * Cell;
        private const int BoardH = Rows * Cell;

        // ---- palette ----
        private static readonly Color ColBg       = C("#0E1620");
        private static readonly Color ColBoard    = C("#15202D");
        private static readonly Color ColBoardAlt = C("#182534");
        private static readonly Color ColEdge     = C("#243345");
        private static readonly Color ColText     = C("#E8EFF7");
        private static readonly Color ColMuted    = C("#7C8FA5");
        private static readonly Color ColAccent   = C("#4ADE80");
        private static readonly Color ColHead     = C("#6EE7A0");
        private static readonly Color ColTail     = C("#0E9F6E");
        private static readonly Color ColFood     = C("#F87171");
        private static readonly Color ColBonus    = C("#FBBF24");

        private static Color C(string hex) { return ColorTranslator.FromHtml(hex); }

        // ---- state ----
        private GameState state = GameState.Greeting;
        private readonly List<Point> snake = new List<Point>();
        private readonly Queue<Point> inputs = new Queue<Point>();
        private Point dir, lastDir;
        private Point food;
        private bool hasBonus;
        private Point bonus;
        private int bonusLeft;
        private int score, best, apples, growPending;
        private bool newBest;

        private readonly Random rng = new Random();
        private readonly Timer logic = new Timer();
        private readonly Timer frame = new Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();

        private static readonly string SavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SnakeApp", "highscore.txt");

        public SnakeForm()
        {
            Text = "Snake 2D";
            BackColor = ColBg;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(BoardW + Pad * 2, HeaderH + BoardH + FooterH);
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            best = LoadBest();

            logic.Interval = 120;
            logic.Tick += delegate { Step(); };

            frame.Interval = 25;
            frame.Tick += delegate { Invalidate(); };
            frame.Start();
        }

        // ---------------- persistence ----------------

        private static int LoadBest()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    int v;
                    if (int.TryParse(File.ReadAllText(SavePath).Trim(), out v)) return v;
                }
            }
            catch { }
            return 0;
        }

        private void SaveBest()
        {
            try
            {
                string dir = Path.GetDirectoryName(SavePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SavePath, best.ToString());
            }
            catch { }
        }

        // ---------------- game flow ----------------

        private void StartGame()
        {
            snake.Clear();
            inputs.Clear();
            int cy = Rows / 2;
            snake.Add(new Point(6, cy));
            snake.Add(new Point(5, cy));
            snake.Add(new Point(4, cy));
            dir = lastDir = new Point(1, 0);
            score = 0; apples = 0; growPending = 0;
            hasBonus = false; bonusLeft = 0; newBest = false;
            PlaceFood();
            state = GameState.Playing;
            logic.Interval = 120;
            logic.Start();
        }

        private void EndGame()
        {
            logic.Stop();
            state = GameState.GameOver;
            if (score > best) { best = score; newBest = true; SaveBest(); }
        }

        private void ToMenu()
        {
            logic.Stop();
            state = GameState.Greeting;
        }

        private void TogglePause()
        {
            if (state == GameState.Playing) { logic.Stop(); state = GameState.Paused; }
            else if (state == GameState.Paused) { state = GameState.Playing; logic.Start(); }
        }

        private bool Occupied(Point p)
        {
            for (int i = 0; i < snake.Count; i++) if (snake[i] == p) return true;
            return false;
        }

        private void PlaceFood()
        {
            if (snake.Count >= Cols * Rows) return;
            Point p;
            do { p = new Point(rng.Next(Cols), rng.Next(Rows)); }
            while (Occupied(p) || (hasBonus && p == bonus));
            food = p;
        }

        private void PlaceBonus()
        {
            if (snake.Count + 2 >= Cols * Rows) return;
            Point p;
            do { p = new Point(rng.Next(Cols), rng.Next(Rows)); }
            while (Occupied(p) || p == food);
            bonus = p;
            hasBonus = true;
            bonusLeft = 45;
        }

        private void Step()
        {
            // pull the next non-reversing input
            while (inputs.Count > 0)
            {
                Point c = inputs.Dequeue();
                if (c.X == -lastDir.X && c.Y == -lastDir.Y) continue;
                dir = c;
                break;
            }
            lastDir = dir;

            // No walls: the board wraps, so leaving one edge brings you back
            // in on the opposite one. Biting yourself is the only way to lose.
            Point head = snake[0];
            Point nh = new Point(((head.X + dir.X) % Cols + Cols) % Cols,
                                 ((head.Y + dir.Y) % Rows + Rows) % Rows);

            int limit = snake.Count - (growPending > 0 ? 0 : 1);
            for (int i = 0; i < limit; i++)
                if (snake[i] == nh) { EndGame(); return; }

            snake.Insert(0, nh);

            if (nh == food)
            {
                score += 1;
                apples += 1;
                growPending += 2;
                PlaceFood();
                if (apples % 5 == 0 && !hasBonus) PlaceBonus();
                logic.Interval = Math.Max(58, 120 - apples * 3);
            }
            else if (hasBonus && nh == bonus)
            {
                score += 5;
                growPending += 3;
                hasBonus = false;
            }

            if (growPending > 0) growPending--;
            else snake.RemoveAt(snake.Count - 1);

            if (hasBonus && --bonusLeft <= 0) hasBonus = false;
        }

        // ---------------- input ----------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HandleKey(keyData & Keys.KeyCode)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Push(int x, int y)
        {
            if (state != GameState.Playing) return;
            if (inputs.Count < 3) inputs.Enqueue(new Point(x, y));
        }

        private bool HandleKey(Keys k)
        {
            switch (k)
            {
                case Keys.Up: case Keys.W: Push(0, -1); return true;
                case Keys.Down: case Keys.S: Push(0, 1); return true;
                case Keys.Left: case Keys.A: Push(-1, 0); return true;
                case Keys.Right: case Keys.D: Push(1, 0); return true;

                case Keys.Enter:
                    if (state == GameState.Greeting || state == GameState.GameOver) StartGame();
                    else if (state == GameState.Paused) TogglePause();
                    return true;

                case Keys.Space: case Keys.P:
                    if (state == GameState.Greeting || state == GameState.GameOver) StartGame();
                    else TogglePause();
                    return true;

                case Keys.Escape:
                    if (state == GameState.Greeting) Close();
                    else ToMenu();
                    return true;
            }
            return false;
        }

        // ---------------- painting ----------------

        private static GraphicsPath Rounded(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }
            float d = rad * 2f;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Color Lerp(Color a, Color b, float t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static void Str(Graphics g, string s, Font f, Color c, float cx, float y)
        {
            using (StringFormat sf = new StringFormat())
            using (SolidBrush b = new SolidBrush(c))
            {
                sf.Alignment = StringAlignment.Center;
                g.DrawString(s, f, b, cx, y, sf);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(ColBg);

            float t = (float)clock.Elapsed.TotalSeconds;
            Rectangle board = new Rectangle(Pad, HeaderH, BoardW, BoardH);

            DrawHeader(g);
            DrawBoard(g, board);

            if (state != GameState.Greeting)
            {
                DrawFood(g, board, t);
                DrawSnake(g, board);
            }

            if (state == GameState.Greeting) DrawGreeting(g, board, t);
            else if (state == GameState.Paused) DrawPaused(g, board);
            else if (state == GameState.GameOver) DrawGameOver(g, board);

            DrawFooter(g);
        }

        private void DrawHeader(Graphics g)
        {
            using (Font ft = new Font("Segoe UI", 15f, FontStyle.Bold))
            using (Font fs = new Font("Segoe UI", 9.5f))
            using (Font fv = new Font("Segoe UI", 15f, FontStyle.Bold))
            using (SolidBrush bt = new SolidBrush(ColText))
            using (SolidBrush bm = new SolidBrush(ColMuted))
            using (SolidBrush ba = new SolidBrush(ColAccent))
            using (StringFormat right = new StringFormat())
            {
                g.DrawString("SNAKE", ft, ba, Pad - 2, 22);
                right.Alignment = StringAlignment.Far;

                float rx = ClientSize.Width - Pad;
                string bs = best.ToString(), ss = score.ToString();

                g.DrawString(bs, fv, bt, rx, 26, right);
                float x = rx - g.MeasureString(bs, fv).Width - 6;
                g.DrawString("BEST", fs, bm, x, 32, right);

                x -= g.MeasureString("BEST", fs).Width + 26;
                g.DrawString(ss, fv, bt, x, 26, right);
                x -= g.MeasureString(ss, fv).Width + 6;
                g.DrawString("SCORE", fs, bm, x, 32, right);
            }
        }

        private void DrawFooter(Graphics g)
        {
            string hint;
            switch (state)
            {
                case GameState.Greeting: hint = "ENTER  play      ESC  back"; break;
                case GameState.Playing:  hint = "ARROWS / WASD  move      SPACE  pause      ESC  menu"; break;
                case GameState.Paused:   hint = "SPACE  resume      ESC  menu"; break;
                default:                 hint = "ENTER  play again      ESC  menu"; break;
            }
            using (Font f = new Font("Segoe UI", 9f))
                Str(g, hint, f, ColMuted, ClientSize.Width / 2f, HeaderH + BoardH + 16);
        }

        private void DrawBoard(Graphics g, Rectangle board)
        {
            using (GraphicsPath p = Rounded(new RectangleF(board.X - 1, board.Y - 1, board.Width + 2, board.Height + 2), 14f))
            using (SolidBrush b = new SolidBrush(ColBoard))
            using (Pen pen = new Pen(ColEdge, 1.5f))
            {
                g.FillPath(b, p);
                g.DrawPath(pen, p);
            }

            using (GraphicsPath clip = Rounded(new RectangleF(board.X, board.Y, board.Width, board.Height), 13f))
            using (SolidBrush alt = new SolidBrush(ColBoardAlt))
            {
                g.SetClip(clip);
                for (int y = 0; y < Rows; y++)
                    for (int x = 0; x < Cols; x++)
                        if (((x + y) & 1) == 0)
                            g.FillRectangle(alt, board.X + x * Cell, board.Y + y * Cell, Cell, Cell);
                g.ResetClip();
            }
        }

        private static RectangleF CellRect(Rectangle board, Point p, float inset)
        {
            return new RectangleF(board.X + p.X * Cell + inset, board.Y + p.Y * Cell + inset,
                                  Cell - inset * 2, Cell - inset * 2);
        }

        private void DrawFood(Graphics g, Rectangle board, float t)
        {
            float pulse = 1f + 0.10f * (float)Math.Sin(t * 5.0);
            RectangleF r = CellRect(board, food, 4f);
            float grow = (r.Width * pulse - r.Width) / 2f;
            r.Inflate(grow, grow);

            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(r);
                using (PathGradientBrush pg = new PathGradientBrush(gp))
                {
                    pg.CenterPoint = new PointF(r.X + r.Width * 0.35f, r.Y + r.Height * 0.32f);
                    pg.CenterColor = Lerp(ColFood, Color.White, 0.45f);
                    pg.SurroundColors = new Color[] { C("#DC2626") };
                    g.FillPath(pg, gp);
                }
            }
            using (Pen stem = new Pen(C("#16A34A"), 2.2f))
            {
                stem.StartCap = LineCap.Round;
                stem.EndCap = LineCap.Round;
                g.DrawLine(stem, r.X + r.Width / 2f, r.Y + 1.5f, r.X + r.Width / 2f + 3.5f, r.Y - 3f);
            }

            if (hasBonus)
            {
                RectangleF br = CellRect(board, bonus, 3.5f);
                GraphicsState st = g.Save();
                g.TranslateTransform(br.X + br.Width / 2f, br.Y + br.Height / 2f);
                g.RotateTransform(t * 90f);
                RectangleF d = new RectangleF(-br.Width / 2f, -br.Height / 2f, br.Width, br.Height);
                using (GraphicsPath dia = new GraphicsPath())
                {
                    dia.AddPolygon(new PointF[] {
                        new PointF(0, d.Y), new PointF(d.Right, 0),
                        new PointF(0, d.Bottom), new PointF(d.X, 0) });
                    using (LinearGradientBrush lb = new LinearGradientBrush(d, C("#FDE68A"), ColBonus, 60f))
                        g.FillPath(lb, dia);
                }
                g.Restore(st);

                using (Pen ring = new Pen(Color.FromArgb(150, ColBonus), 2f))
                    g.DrawArc(ring, CellRect(board, bonus, 1.5f), -90f, 360f * (bonusLeft / 45f));
            }
        }

        private void DrawSnake(Graphics g, Rectangle board)
        {
            int n = snake.Count;
            for (int i = n - 1; i >= 1; i--)
            {
                float k = n <= 1 ? 0f : (float)i / (n - 1);
                RectangleF r = CellRect(board, snake[i], 2.2f);
                using (GraphicsPath p = Rounded(r, 7f))
                using (SolidBrush b = new SolidBrush(Lerp(C("#34D399"), ColTail, k)))
                    g.FillPath(b, p);
            }

            RectangleF hr = CellRect(board, snake[0], 1.2f);
            using (GraphicsPath p = Rounded(hr, 8f))
            using (LinearGradientBrush lb = new LinearGradientBrush(hr, ColHead, ColAccent, 45f))
            using (Pen glow = new Pen(Color.FromArgb(70, Color.White), 1.2f))
            {
                g.FillPath(lb, p);
                g.DrawPath(glow, p);
            }

            // eyes, facing the direction of travel
            float cx = hr.X + hr.Width / 2f, cy = hr.Y + hr.Height / 2f;
            float fx = dir.X, fy = dir.Y, px = -fy, py = fx;
            float fwd = hr.Width * 0.22f, side = hr.Width * 0.20f, rad = 2.6f;
            using (SolidBrush eye = new SolidBrush(C("#0B1118")))
                for (int s = -1; s <= 1; s += 2)
                {
                    float ex = cx + fx * fwd + px * side * s;
                    float ey = cy + fy * fwd + py * side * s;
                    g.FillEllipse(eye, ex - rad, ey - rad, rad * 2, rad * 2);
                }
        }

        // ---------------- overlays ----------------

        private static void Scrim(Graphics g, Rectangle board, int alpha)
        {
            using (GraphicsPath p = Rounded(new RectangleF(board.X, board.Y, board.Width, board.Height), 13f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 6, 11, 17)))
                g.FillPath(b, p);
        }

        private static void Pill(Graphics g, string text, float cx, float cy, Color fill, Color fg, float alpha)
        {
            using (Font f = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            {
                SizeF s = g.MeasureString(text, f);
                RectangleF r = new RectangleF(cx - s.Width / 2f - 18f, cy, s.Width + 36f, s.Height + 14f);
                using (GraphicsPath p = Rounded(r, r.Height / 2f))
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * alpha), fill)))
                    g.FillPath(b, p);
                Str(g, text, f, fg, cx, r.Y + 7f);
            }
        }

        private void DrawGreeting(Graphics g, Rectangle board, float t)
        {
            Scrim(g, board, 236);

            float cx = board.X + board.Width / 2f;
            float top = board.Y + 178f;

            // decorative snake wiggling across the top
            for (int i = 9; i >= 0; i--)
            {
                float k = i / 9f;
                float x = cx - 108f + i * 24f;
                float y = board.Y + 130f + (float)Math.Sin(t * 2.2 + i * 0.55) * 9f;
                float sz = 16f - k * 4f;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(210, Lerp(ColAccent, ColTail, k))))
                using (GraphicsPath p = Rounded(new RectangleF(x - sz / 2, y - sz / 2, sz, sz), 5f))
                    g.FillPath(b, p);
            }

            using (Font fbig = new Font("Segoe UI", 38f, FontStyle.Bold))
            using (Font fsub = new Font("Segoe UI", 12f))
            using (Font fsm = new Font("Segoe UI", 9.5f))
            {
                Str(g, "What's up?", fbig, ColText, cx, top);
                Str(g, "Good to see you. Fancy a game of Snake?", fsub, ColMuted, cx, top + 70f);

                float pulse = 0.70f + 0.30f * (float)((Math.Sin(t * 3.0) + 1) / 2);
                Pill(g, "PRESS  ENTER  TO  PLAY", cx, top + 120f, ColAccent, C("#06231A"), pulse);

                Str(g, "Arrows or WASD to steer  ·  the edges wrap around  ·  don't bite yourself",
                    fsm, ColMuted, cx, top + 182f);
                Str(g, "Gold diamonds are worth 5 — grab them before they vanish",
                    fsm, Color.FromArgb(190, ColBonus), cx, top + 204f);

                if (best > 0)
                    Str(g, "Your best so far: " + best, fsm, ColMuted, cx, top + 242f);
            }
        }

        private void DrawPaused(Graphics g, Rectangle board)
        {
            Scrim(g, board, 170);
            float cx = board.X + board.Width / 2f;
            float cy = board.Y + board.Height / 2f;
            using (Font f = new Font("Segoe UI", 26f, FontStyle.Bold))
            using (Font fs = new Font("Segoe UI", 10.5f))
            {
                Str(g, "Paused", f, ColText, cx, cy - 46f);
                Str(g, "Press SPACE to jump back in", fs, ColMuted, cx, cy + 6f);
            }
        }

        private void DrawGameOver(Graphics g, Rectangle board)
        {
            Scrim(g, board, 216);
            float cx = board.X + board.Width / 2f;
            float cy = board.Y + board.Height / 2f;

            using (Font fbig = new Font("Segoe UI", 29f, FontStyle.Bold))
            using (Font fnum = new Font("Segoe UI", 44f, FontStyle.Bold))
            using (Font fs = new Font("Segoe UI", 10.5f))
            using (Font fsm = new Font("Segoe UI", 9.5f))
            {
                Str(g, "Game over", fbig, ColText, cx, cy - 134f);
                Str(g, "SCORE", fsm, ColMuted, cx, cy - 78f);
                Str(g, score.ToString(), fnum, ColAccent, cx, cy - 60f);

                if (newBest) Pill(g, "NEW BEST!", cx, cy + 14f, ColBonus, C("#3A2A02"), 1f);
                else Str(g, "Best: " + best, fs, ColMuted, cx, cy + 18f);

                Str(g, "Length " + snake.Count + "  ·  " + apples + " apple" + (apples == 1 ? "" : "s"),
                    fsm, ColMuted, cx, cy + 60f);
                Str(g, "Press ENTER to play again", fs, ColText, cx, cy + 98f);
            }
        }

    }
}
