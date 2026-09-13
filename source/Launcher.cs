using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnakeLauncher
{
    // The front door. Both games live in this one executable: the flat board
    // is WinForms/GDI+, the night sky is WPF 3D. Each is opened modally from
    // here and hands control back when it closes.
    internal sealed class Launcher : Window
    {
        private static readonly Color CText   = Hex("#E8EFF7");
        private static readonly Color CMuted  = Hex("#7C8FA5");
        private static readonly Color CAccent = Hex("#4ADE80");
        private static readonly Color CSky    = Hex("#7FB2FF");

        private static readonly string SaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnakeApp");

        private readonly List<Border> cards = new List<Border>();
        private readonly List<TextBlock> bests = new List<TextBlock>();
        private int picked;

        private static Color Hex(string s) { return (Color)ColorConverter.ConvertFromString(s); }

        private static int ReadBest(string file)
        {
            try
            {
                string p = Path.Combine(SaveDir, file);
                int v;
                if (File.Exists(p) && int.TryParse(File.ReadAllText(p).Trim(), out v)) return v;
            }
            catch { }
            return 0;
        }

        private static TextBlock T(string text, double size, Color col, FontWeight w)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = new FontFamily("Segoe UI");
            t.FontSize = size;
            t.FontWeight = w;
            t.Foreground = new SolidColorBrush(col);
            t.TextAlignment = TextAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        public Launcher()
        {
            Title = "Snake";
            Width = 760; Height = 560;
            MinWidth = 640; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Hex("#03060C"));

            Grid root = new Grid();

            LinearGradientBrush sky = new LinearGradientBrush();
            sky.StartPoint = new Point(0, 0);
            sky.EndPoint = new Point(0, 1);
            sky.GradientStops.Add(new GradientStop(Hex("#01030A"), 0));
            sky.GradientStops.Add(new GradientStop(Hex("#08121F"), 0.55));
            sky.GradientStops.Add(new GradientStop(Hex("#03060E"), 1));
            System.Windows.Shapes.Rectangle bg = new System.Windows.Shapes.Rectangle();
            bg.Fill = sky;
            root.Children.Add(bg);

            // a quiet scatter of stars behind everything
            Canvas stars = new Canvas();
            stars.IsHitTestVisible = false;
            Random r = new Random(8712);
            for (int i = 0; i < 150; i++)
            {
                System.Windows.Shapes.Ellipse e = new System.Windows.Shapes.Ellipse();
                double s = r.NextDouble() < 0.15 ? 2.4 : 1.3;
                e.Width = e.Height = s;
                e.Fill = new SolidColorBrush(Color.FromArgb((byte)(70 + r.Next(150)), 200, 216, 255));
                Canvas.SetLeft(e, r.NextDouble() * 760);
                Canvas.SetTop(e, r.NextDouble() * 560);
                stars.Children.Add(e);
            }
            root.Children.Add(stars);

            StackPanel col = new StackPanel();
            col.HorizontalAlignment = HorizontalAlignment.Center;
            col.VerticalAlignment = VerticalAlignment.Center;

            TextBlock title = T("What's up?", 52, CText, FontWeights.Bold);
            col.Children.Add(title);
            TextBlock sub = T("Two snakes. Take your pick.", 14.5, CMuted, FontWeights.Normal);
            sub.Margin = new Thickness(0, 4, 0, 30);
            col.Children.Add(sub);

            Grid picker = new Grid();
            picker.ColumnDefinitions.Add(new ColumnDefinition());
            picker.ColumnDefinitions.Add(new ColumnDefinition());

            Border a = MakeCard(0, "Snake 2D", CAccent, "■",
                                "The flat board. Apples, gold bonuses,\nedges that wrap right round.",
                                "highscore.txt");
            Border b = MakeCard(1, "Snake 3D", CSky, "✦",
                                "Endless night sky. Fly free, eat blocks,\nglide over your own tail.",
                                "highscore3d.txt");
            Grid.SetColumn(a, 0);
            Grid.SetColumn(b, 1);
            picker.Children.Add(a);
            picker.Children.Add(b);
            col.Children.Add(picker);

            TextBlock hint = T("←  →  choose      ENTER  play      ESC  quit", 11.5, CMuted, FontWeights.Normal);
            hint.Margin = new Thickness(0, 34, 0, 0);
            col.Children.Add(hint);

            root.Children.Add(col);
            Content = root;

            KeyDown += OnKeyDown;
            Activated += delegate { RefreshBests(); };
            // Make sure the window itself holds keyboard focus; there is no
            // focusable content to take it.
            Loaded += delegate { Activate(); Focus(); };
            Select(0);
#if CAPTURE
            // Test hooks. Windows will not let a background test harness pull
            // this window to the front, so synthetic keystrokes never arrive;
            // these drive the same code paths directly instead.
            string auto = Environment.GetEnvironmentVariable("SNAKE_AUTOPLAY");
            if (auto == "2d" || auto == "3d")
                Loaded += delegate { Play(auto == "2d" ? 0 : 1); };

            string key = Environment.GetEnvironmentVariable("SNAKE_TESTKEY");
            if (!string.IsNullOrEmpty(key))
                Loaded += delegate
                {
                    Key k = (Key)Enum.Parse(typeof(Key), key);
                    KeyEventArgs ev = new KeyEventArgs(
                        Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, k);
                    ev.RoutedEvent = Keyboard.KeyDownEvent;
                    RaiseEvent(ev);          // through the real routed-event path
                };
#endif
        }

        private Border MakeCard(int index, string name, Color accent, string glyph, string blurb, string bestFile)
        {
            StackPanel inner = new StackPanel();

            TextBlock g = T(glyph, 30, accent, FontWeights.Normal);
            g.Margin = new Thickness(0, 2, 0, 8);
            inner.Children.Add(g);

            inner.Children.Add(T(name, 23, CText, FontWeights.Bold));

            TextBlock d = T(blurb, 12, CMuted, FontWeights.Normal);
            d.Margin = new Thickness(0, 10, 0, 0);
            inner.Children.Add(d);

            TextBlock best = T("", 11.5, accent, FontWeights.Bold);
            best.Margin = new Thickness(0, 16, 0, 0);
            inner.Children.Add(best);
            bests.Add(best);

            Border card = new Border();
            card.Child = inner;
            card.CornerRadius = new CornerRadius(16);
            card.Padding = new Thickness(24, 22, 24, 22);
            card.Margin = new Thickness(10, 0, 10, 0);
            card.BorderThickness = new Thickness(1.5);
            card.Cursor = Cursors.Hand;
            card.Tag = index;
            card.MouseEnter += delegate { Select(index); };
            card.MouseLeftButtonUp += delegate { Play(index); };
            cards.Add(card);
            return card;
        }

        private void RefreshBests()
        {
            int a = ReadBest("highscore.txt"), b = ReadBest("highscore3d.txt");
            bests[0].Text = a > 0 ? "BEST  " + a : "not played yet";
            bests[1].Text = b > 0 ? "BEST  " + b : "not played yet";
        }

        private void Select(int i)
        {
            picked = i;
            for (int k = 0; k < cards.Count; k++)
            {
                bool on = k == i;
                cards[k].Background = new SolidColorBrush(on
                    ? Color.FromArgb(235, 16, 27, 42)
                    : Color.FromArgb(150, 8, 14, 24));
                cards[k].BorderBrush = new SolidColorBrush(on
                    ? (k == 0 ? CAccent : CSky)
                    : Hex("#22304A"));
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left: case Key.A: Select(0); e.Handled = true; break;
                case Key.Right: case Key.D: Select(1); e.Handled = true; break;
                case Key.D1: case Key.NumPad1: Select(0); Play(0); e.Handled = true; break;
                case Key.D2: case Key.NumPad2: Select(1); Play(1); e.Handled = true; break;
                case Key.Enter: case Key.Space: Play(picked); e.Handled = true; break;
                default: Log("key " + e.Key); break;
                case Key.Escape: Close(); e.Handled = true; break;
            }
        }

        // Set SNAKE_LOG=1 to trace launching and record anything a game throws
        // on the way up. Off by default, so nothing is written during play.
        private static readonly bool Logging = Environment.GetEnvironmentVariable("SNAKE_LOG") == "1";

        private static void Log(string msg)
        {
            if (!Logging) return;
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "snake-launcher.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n");
            }
            catch { }
        }

        private void Play(int which)
        {
            Log("Play(" + which + ") entered");
            Hide();
            try
            {
                if (which == 0)
                {
                    using (SnakeApp.SnakeForm f = new SnakeApp.SnakeForm())
                    {
                        Log("2D form built, showing");
                        f.ShowDialog();
                        Log("2D form closed");
                    }
                }
                else
                {
                    Snake3D.Game g = new Snake3D.Game();
                    Log("3D window built, showing");
                    g.ShowDialog();
                    Log("3D window closed");
                }
            }
            catch (Exception ex)
            {
                Log("FAILED: " + ex);
            }
            finally
            {
                RefreshBests();
                Show();
                Activate();
                Log("back at the picker");
            }
        }

        [STAThread]
        private static void Main()
        {
            // Must happen before any WinForms control exists, so it is done
            // here rather than inside the 2D game.
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(new Launcher());
        }
    }
}
