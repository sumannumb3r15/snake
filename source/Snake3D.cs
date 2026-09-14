using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
#if CAPTURE
using System.Windows.Media.Imaging;
#endif

namespace Snake3D
{
    internal enum GameState { Greeting, Playing, Paused }

    // Integer cell / direction triple. Space is unbounded, so these just keep
    // counting in whatever direction you fly.
    internal struct C3 : IEquatable<C3>
    {
        public int X, Y, Z;
        public C3(int x, int y, int z) { X = x; Y = y; Z = z; }

        public bool Equals(C3 o) { return X == o.X && Y == o.Y && Z == o.Z; }
        public override bool Equals(object o) { return o is C3 && Equals((C3)o); }
        public override int GetHashCode() { return (X * 73856093) ^ (Y * 19349663) ^ (Z * 83492791); }
        public static bool operator ==(C3 a, C3 b) { return a.Equals(b); }
        public static bool operator !=(C3 a, C3 b) { return !a.Equals(b); }

        public static C3 operator +(C3 a, C3 b) { return new C3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static C3 operator -(C3 a) { return new C3(-a.X, -a.Y, -a.Z); }

        public static C3 Cross(C3 a, C3 b)
        {
            return new C3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        // Chebyshev distance: how many cells off you are on the worst axis.
        public static int Cheb(C3 a, C3 b)
        {
            return Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
        }

        public double DistTo(C3 b)
        {
            double dx = X - b.X, dy = Y - b.Y, dz = Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    internal struct Food
    {
        public C3 Cell;
        public C3 Chunk;
        public bool Gold;
        public double Phase;
    }


    internal sealed class Particle
    {
        public Point3D Pos;
        public Vector3D Vel;
        public double Life, MaxLife, Size;
        public Material Mat;
    }

    // A little word that pops out of the snake's mouth when it eats.
    internal sealed class Pop
    {
        public TextBlock Label;
        public double Life, MaxLife, Drift, Size;
    }

    // ---------------------------------------------------------------------
    // A recycled bank of solids. Everything that moves is drawn by handing
    // this pool a shape, position, size and colour each frame; the models are
    // reused, never rebuilt.
    // ---------------------------------------------------------------------
    internal sealed class ShapePool
    {
        private readonly Model3DGroup group = new Model3DGroup();
        private readonly List<GeometryModel3D> models = new List<GeometryModel3D>();
        private readonly List<Transform3DGroup> xforms = new List<Transform3DGroup>();
        private int used;

        public Model3DGroup Group { get { return group; } }
        public void Begin() { used = 0; }

        public void Add(Geometry3D geo, double x, double y, double z, double s, double yawDeg, Material m)
        {
            Add(geo, x, y, z, s, s, s, yawDeg, m);
        }

        public void Add(Geometry3D geo, double x, double y, double z,
                        double sx, double sy, double sz, double yawDeg, Material m)
        {
            GeometryModel3D gm;
            Transform3DGroup tg;

            if (used < models.Count)
            {
                gm = models[used];
                tg = xforms[used];
            }
            else
            {
                gm = new GeometryModel3D();
                tg = new Transform3DGroup();
                tg.Children.Add(new ScaleTransform3D(1, 1, 1));
                tg.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0)));
                tg.Children.Add(new TranslateTransform3D(0, 0, 0));
                gm.Transform = tg;
                models.Add(gm);
                xforms.Add(tg);
                group.Children.Add(gm);
            }

            if (!ReferenceEquals(gm.Geometry, geo)) gm.Geometry = geo;
            ScaleTransform3D sc = (ScaleTransform3D)tg.Children[0];
            sc.ScaleX = sx; sc.ScaleY = sy; sc.ScaleZ = sz;
            ((AxisAngleRotation3D)((RotateTransform3D)tg.Children[1]).Rotation).Angle = yawDeg;
            TranslateTransform3D tr = (TranslateTransform3D)tg.Children[2];
            tr.OffsetX = x; tr.OffsetY = y; tr.OffsetZ = z;
            if (!ReferenceEquals(gm.Material, m)) gm.Material = m;
            used++;
        }

        public void End()
        {
            for (int i = used; i < models.Count; i++)
            {
                ScaleTransform3D sc = (ScaleTransform3D)xforms[i].Children[0];
                if (sc.ScaleX != 0) { sc.ScaleX = 0; sc.ScaleY = 0; sc.ScaleZ = 0; }
            }
        }
    }

    internal sealed class Game : Window
    {
        // ---- the world ----
        // Unbounded. No walls, no edges, no wrapping, and nothing that can end
        // a run. Cells are absolute, so anything out there stays exactly where
        // it is and is still there if you turn round and go back for it.
        private const double ViewDistance = 90.0;

        // Blocks and obstacles are not spawned around the player -- they are
        // already out there. Space is diced into chunks and each chunk's
        // contents come from a hash of its coordinates, so the world has a
        // fixed layout stretching out forever and nothing ever pops into being
        // in front of you.
        private const int ChunkSize = 44;
        private const double MarkerRange = 65.0;

        // Free flight, not a grid. The snake holds a continuous heading and
        // banks gradually while a direction is held, so it can end up pointing
        // anywhere at all - diagonals included - rather than snapping between
        // six axes.
        private const double TurnRate = 2.0;    // radians per second of banking
        private const double Speed0 = 8.5;      // units per second
        private const double BeadGap = 0.58;    // gap between body beads along the path
        private const double PathSample = 0.20; // how finely the flown path is recorded
        private const double GrabDist = 1.8;    // how near you must pass to take a block
        private const double HopLift = 1.6;     // how far it rises over what is in the way
        private const int Beads0 = 14, BeadsMax = 90;

        private const int GoldOneIn = 6;
        private const int MaxParticles = 260;

        private static readonly double LatticeStep = ViewDistance / 13.0;
        private static readonly double MoteSize = LatticeStep * 0.019;

        private static readonly string[] Yums =
        {
            "yum!", "nom nom", "tasty!", "mmm!", "gulp!", "crunch!",
            "delicious", "more!", "om nom", "scrummy"
        };
        private static readonly string[] GoldYums =
        {
            "jackpot!", "shiny!", "gold!", "oooh!", "treasure!"
        };

        // ---- colours ----
        private static readonly Color CText   = Hex("#E8EFF7");
        private static readonly Color CMuted  = Hex("#7C8FA5");
        private static readonly Color CAccent = Hex("#4ADE80");
        private static readonly Color CGold   = Hex("#FBBF24");

        // Set SNAKE3D_DEBUG=1 to show a frame-rate / state readout.
        private static readonly bool DebugHud = Environment.GetEnvironmentVariable("SNAKE3D_DEBUG") == "1";
        private int frames;
        private double fpsAcc, fps;

        // ---- 3D scene ----
        private Viewport3D viewport;
        private PerspectiveCamera cam;
        private ShapePool pool;
        private MeshGeometry3D cubeGeo, sphereGeo, beadGeo;
        private Material headMat, foodMat, goldMat, eyeMat, markMat, goldMarkMat, rockMat, rockMat2;
        private Material[] bodyMats;
        private TranslateTransform3D latticeXf, starXf;

        // ---- ui ----
        private TextBlock scoreText, bestText, hintText, dbgText;
        private Border overlay, card;
        private StackPanel overlayStack;
        private Canvas popLayer;
        private readonly List<Pop> pops = new List<Pop>();

        // ---- game state ----
        private GameState state = GameState.Greeting;
        private Point3D pos;                                    // continuous, not a cell
        private Vector3D fwd = new Vector3D(1, 0, 0);
        private Vector3D up = new Vector3D(0, 1, 0);
        private readonly List<Point3D> trail = new List<Point3D>();  // path flown, newest first
        private readonly List<Point3D> beads = new List<Point3D>();
        private int beadCount = Beads0;
        private int inX, inY;                                   // held banking / climbing
        private double speed = Speed0, lift, refreshT;
        private readonly List<Food> foods = new List<Food>();
        private readonly List<C3> rocks = new List<C3>();
        private readonly HashSet<C3> rockSet = new HashSet<C3>();
        private readonly HashSet<C3> eatenChunks = new HashSet<C3>();
        private int score, best, eaten;
        private bool boosting;

        private readonly List<Particle> particles = new List<Particle>();
        private readonly Random rng = new Random();

        // ---- timing / camera ----
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double lastTime;
        private Point3D camPos, camTgt;
        private Vector3D camUp = new Vector3D(0, 1, 0);
        private Vector3D camFwd = new Vector3D(0, 0, 1);
        private bool camInit;

        private static readonly string SavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SnakeApp", "highscore3d.txt");

        private static Color Hex(string s) { return (Color)ColorConverter.ConvertFromString(s); }

        // =================================================================
        // geometry helpers
        // =================================================================

        private static void AddQuad(MeshGeometry3D m, Point3D a, Point3D b, Point3D c, Point3D d, Vector3D n)
        {
            int i = m.Positions.Count;
            m.Positions.Add(a); m.Positions.Add(b); m.Positions.Add(c); m.Positions.Add(d);
            for (int k = 0; k < 4; k++) m.Normals.Add(n);
            m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 1); m.TriangleIndices.Add(i + 2);
            m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 2); m.TriangleIndices.Add(i + 3);
        }

        // Every face is wound counter-clockwise as seen from outside, so WPF
        // treats it as front-facing and no back material is needed.
        private static void AddBox(MeshGeometry3D m, double cx, double cy, double cz, double sx, double sy, double sz)
        {
            double x0 = cx - sx / 2, x1 = cx + sx / 2;
            double y0 = cy - sy / 2, y1 = cy + sy / 2;
            double z0 = cz - sz / 2, z1 = cz + sz / 2;

            AddQuad(m, new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), new Point3D(x1, y1, z0), new Vector3D(0, 1, 0));
            AddQuad(m, new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), new Vector3D(0, -1, 0));
            AddQuad(m, new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), new Vector3D(0, 0, 1));
            AddQuad(m, new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Vector3D(0, 0, -1));
            AddQuad(m, new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Vector3D(1, 0, 0));
            AddQuad(m, new Point3D(x0, y0, z0), new Point3D(x0, y0, z1), new Point3D(x0, y1, z1), new Point3D(x0, y1, z0), new Vector3D(-1, 0, 0));
        }

        private static MeshGeometry3D UnitCube()
        {
            MeshGeometry3D m = new MeshGeometry3D();
            AddBox(m, 0, 0, 0, 1, 1, 1);
            m.Freeze();
            return m;
        }

        // Unit-diameter sphere. Triangles are wound (a, a+1, b) so the normals
        // come out pointing away from the centre.
        private static MeshGeometry3D UnitSphere(int lon, int lat)
        {
            MeshGeometry3D m = new MeshGeometry3D();
            for (int i = 0; i <= lat; i++)
            {
                double phi = i / (double)lat * Math.PI;
                for (int j = 0; j <= lon; j++)
                {
                    double th = j / (double)lon * Math.PI * 2;
                    double x = Math.Sin(phi) * Math.Cos(th);
                    double y = Math.Cos(phi);
                    double z = Math.Sin(phi) * Math.Sin(th);
                    m.Positions.Add(new Point3D(x * 0.5, y * 0.5, z * 0.5));
                    m.Normals.Add(new Vector3D(x, y, z));
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j;
                    int b = a + lon + 1;
                    m.TriangleIndices.Add(a); m.TriangleIndices.Add(a + 1); m.TriangleIndices.Add(b);
                    m.TriangleIndices.Add(a + 1); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b);
                }
            m.Freeze();
            return m;
        }

        private static Material Mat(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            DiffuseMaterial d = new DiffuseMaterial(b);
            d.Freeze();
            return d;
        }

        private static Material GlowMat(Color c, Color glow)
        {
            MaterialGroup g = new MaterialGroup();
            g.Children.Add(Mat(c));
            SolidColorBrush b = new SolidColorBrush(glow);
            b.Freeze();
            EmissiveMaterial e = new EmissiveMaterial(b);
            e.Freeze();
            g.Children.Add(e);
            g.Freeze();
            return g;
        }

        // Light-independent: used for stars, which must not be shaded.
        private static Material LightMat(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            EmissiveMaterial e = new EmissiveMaterial(b);
            e.Freeze();
            return e;
        }

        private static Color Lerp(Color a, Color b, double t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t),
                                 (byte)(a.G + (b.G - a.G) * t),
                                 (byte)(a.B + (b.B - a.B) * t));
        }

        private static Vector3D Norm(Vector3D v)
        {
            double l = v.Length;
            return l < 1e-9 ? new Vector3D(0, 1, 0) : new Vector3D(v.X / l, v.Y / l, v.Z / l);
        }

        // =================================================================
        // construction
        // =================================================================

        public Game()
        {
            Title = "Snake 3D";
            Width = 980; Height = 700;
            MinWidth = 640; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Hex("#03060C"));

            best = LoadBest();
            BuildScene();
            BuildUi();
            ShowGreeting();

            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Closing += delegate { SaveBest(); };
            CompositionTarget.Rendering += OnFrame;
#if CAPTURE
            CaptureInit();
#endif
        }

        private void BuildScene()
        {
            pool = new ShapePool();
            cubeGeo = UnitCube();
            sphereGeo = UnitSphere(12, 8);
            beadGeo = UnitSphere(8, 5);

            headMat     = GlowMat(Hex("#86EFAC"), Hex("#16452C"));
            foodMat     = GlowMat(Hex("#F05252"), Hex("#4A1010"));
            goldMat     = GlowMat(Hex("#FBBF24"), Hex("#4A3402"));
            eyeMat      = Mat(Hex("#0B1118"));
            markMat     = GlowMat(Hex("#5E2A38"), Hex("#2A0F16"));
            goldMarkMat = GlowMat(Hex("#6B5220"), Hex("#3A2A08"));
            rockMat     = Mat(Hex("#3C4A63"));
            rockMat2    = Mat(Hex("#2C3850"));

            bodyMats = new Material[16];
            for (int i = 0; i < bodyMats.Length; i++)
                bodyMats[i] = Mat(Lerp(Hex("#34D399"), Hex("#0A6B48"), i / (double)(bodyMats.Length - 1)));

            // --- night sky ---
            // Stars sit on a huge shell that is moved to wherever the camera
            // is, every frame. Never rotated, never scaled -- so they keep
            // their bearings as you turn but never get any closer, which is
            // what makes them read as infinitely far off.
            MeshGeometry3D faint = new MeshGeometry3D();
            MeshGeometry3D bright = new MeshGeometry3D();
            Random sr = new Random(20260912);
            for (int i = 0; i < 900; i++)
            {
                double th = sr.NextDouble() * Math.PI * 2;
                double ph = Math.Acos(2 * sr.NextDouble() - 1);
                double r = 250.0;
                double x = r * Math.Sin(ph) * Math.Cos(th);
                double y = r * Math.Cos(ph);
                double z = r * Math.Sin(ph) * Math.Sin(th);
                bool big = sr.NextDouble() < 0.18;
                double s = big ? 1.1 + sr.NextDouble() * 1.5 : 0.45 + sr.NextDouble() * 0.7;
                AddBox(big ? bright : faint, x, y, z, s, s, s);
            }
            faint.Freeze(); bright.Freeze();
            starXf = new TranslateTransform3D(0, 0, 0);

            // --- drifting motes ---
            // Stars alone give no sense of movement, because they never shift.
            // These sit close by and slide past, which is what tells you that
            // you are travelling. The ball of them moves with the player in
            // whole-spacing steps, so the shifted copy lands on itself.
            MeshGeometry3D motes = new MeshGeometry3D();
            int steps = (int)Math.Ceiling(ViewDistance / LatticeStep);
            double r2 = ViewDistance * ViewDistance;
            for (int ix = -steps; ix <= steps; ix++)
                for (int iy = -steps; iy <= steps; iy++)
                    for (int iz = -steps; iz <= steps; iz++)
                    {
                        double mx = ix * LatticeStep, my = iy * LatticeStep, mz = iz * LatticeStep;
                        if (mx * mx + my * my + mz * mz > r2) continue;
                        AddBox(motes, mx, my, mz, MoteSize, MoteSize, MoteSize);
                    }
            motes.Freeze();
            latticeXf = new TranslateTransform3D(0, 0, 0);

            Model3DGroup world = new Model3DGroup();
            world.Children.Add(new AmbientLight(Hex("#5A6675")));
            world.Children.Add(new DirectionalLight(Hex("#DCE6FF"), new Vector3D(-0.45, -1.0, -0.35)));
            world.Children.Add(new DirectionalLight(Hex("#2A4066"), new Vector3D(0.6, 0.45, 0.7)));

            GeometryModel3D starsDim = new GeometryModel3D(faint, LightMat(Hex("#8FA4C8")));
            GeometryModel3D starsLit = new GeometryModel3D(bright, LightMat(Hex("#E8EEFF")));
            starsDim.Transform = starXf;
            starsLit.Transform = starXf;
            world.Children.Add(starsDim);
            world.Children.Add(starsLit);

            GeometryModel3D lattice = new GeometryModel3D(motes, LightMat(Hex("#24405E")));
            lattice.Transform = latticeXf;
            world.Children.Add(lattice);
            world.Children.Add(pool.Group);

            ModelVisual3D visual = new ModelVisual3D();
            visual.Content = world;

            cam = new PerspectiveCamera();
            cam.FieldOfView = 60;
            cam.Position = new Point3D(0, 16, -26);
            cam.LookDirection = new Vector3D(0, -10, 26);
            cam.UpDirection = new Vector3D(0, 1, 0);
            cam.NearPlaneDistance = 0.08;
            cam.FarPlaneDistance = 600;

            viewport = new Viewport3D();
            viewport.Camera = cam;
            viewport.Children.Add(visual);
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
            return t;
        }

        private void BuildUi()
        {
            Grid root = new Grid();

            LinearGradientBrush sky = new LinearGradientBrush();
            sky.StartPoint = new System.Windows.Point(0, 0);
            sky.EndPoint = new System.Windows.Point(0, 1);
            sky.GradientStops.Add(new GradientStop(Hex("#01030A"), 0));
            sky.GradientStops.Add(new GradientStop(Hex("#050B1A"), 0.5));
            sky.GradientStops.Add(new GradientStop(Hex("#0A1226"), 0.78));
            sky.GradientStops.Add(new GradientStop(Hex("#03060E"), 1));
            System.Windows.Shapes.Rectangle skyRect = new System.Windows.Shapes.Rectangle();
            skyRect.Fill = sky;
            root.Children.Add(skyRect);

            root.Children.Add(viewport);

            RadialGradientBrush vg = new RadialGradientBrush();
            vg.GradientOrigin = new System.Windows.Point(0.5, 0.5);
            vg.Center = new System.Windows.Point(0.5, 0.5);
            vg.RadiusX = 0.78; vg.RadiusY = 0.78;
            vg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.45));
            vg.GradientStops.Add(new GradientStop(Color.FromArgb(150, 1, 3, 8), 1));
            System.Windows.Shapes.Rectangle vgRect = new System.Windows.Shapes.Rectangle();
            vgRect.Fill = vg;
            vgRect.IsHitTestVisible = false;
            root.Children.Add(vgRect);

            popLayer = new Canvas();
            popLayer.IsHitTestVisible = false;
            root.Children.Add(popLayer);

            Grid hud = new Grid();
            hud.IsHitTestVisible = false;
            hud.Margin = new Thickness(22, 16, 22, 14);

            TextBlock brand = T("SNAKE 3D", 17, CAccent, FontWeights.Bold);
            brand.HorizontalAlignment = HorizontalAlignment.Left;
            brand.VerticalAlignment = VerticalAlignment.Top;
            hud.Children.Add(brand);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Top;
            TextBlock sl = T("SCORE", 11, CMuted, FontWeights.Normal);
            sl.Margin = new Thickness(0, 4, 7, 0);
            scoreText = T("0", 17, CText, FontWeights.Bold);
            scoreText.Margin = new Thickness(0, 0, 24, 0);
            TextBlock bl = T("BEST", 11, CMuted, FontWeights.Normal);
            bl.Margin = new Thickness(0, 4, 7, 0);
            bestText = T(best.ToString(), 17, CText, FontWeights.Bold);
            right.Children.Add(sl); right.Children.Add(scoreText);
            right.Children.Add(bl); right.Children.Add(bestText);
            hud.Children.Add(right);

            hintText = T("", 11.5, CMuted, FontWeights.Normal);
            hintText.VerticalAlignment = VerticalAlignment.Bottom;
            hud.Children.Add(hintText);

            if (DebugHud)
            {
                dbgText = T("", 11, CGold, FontWeights.Normal);
                dbgText.HorizontalAlignment = HorizontalAlignment.Left;
                dbgText.VerticalAlignment = VerticalAlignment.Bottom;
                hud.Children.Add(dbgText);
            }
            root.Children.Add(hud);

            overlayStack = new StackPanel();

            card = new Border();
            card.Background = new SolidColorBrush(Color.FromArgb(238, 5, 10, 19));
            card.BorderBrush = new SolidColorBrush(Hex("#27384E"));
            card.BorderThickness = new Thickness(1);
            card.CornerRadius = new CornerRadius(20);
            card.Padding = new Thickness(52, 38, 52, 40);
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.Child = overlayStack;

            overlay = new Border();
            overlay.Background = new SolidColorBrush(Color.FromArgb(110, 2, 5, 11));
            overlay.IsHitTestVisible = false;
            overlay.Child = card;
            root.Children.Add(overlay);

            Content = root;
        }

        // =================================================================
        // overlays
        // =================================================================

        private void ShowGreeting()
        {
            overlayStack.Children.Clear();

            TextBlock title = T("What's up?", 58, CText, FontWeights.Bold);
            title.Margin = new Thickness(0, 0, 0, 6);
            overlayStack.Children.Add(title);
            overlayStack.Children.Add(T("Endless night sky. Nothing to crash into, nothing to lose.", 15, CMuted, FontWeights.Normal));

            Border pill = new Border();
            pill.Background = new SolidColorBrush(CAccent);
            pill.CornerRadius = new CornerRadius(22);
            pill.Padding = new Thickness(26, 11, 26, 12);
            pill.Margin = new Thickness(0, 30, 0, 26);
            pill.HorizontalAlignment = HorizontalAlignment.Center;
            pill.Child = T("PRESS  ENTER  TO  FLY", 13, Hex("#06231A"), FontWeights.Bold);
            overlayStack.Children.Add(pill);

            overlayStack.Children.Add(T("←  →   or  A / D     bank left and right", 12.5, CMuted, FontWeights.Normal));
            TextBlock l2 = T("↑  ↓   or  W / S     climb and dive", 12.5, CMuted, FontWeights.Normal);
            l2.Margin = new Thickness(0, 6, 0, 0);
            overlayStack.Children.Add(l2);
            TextBlock l3 = T("Hold  SHIFT  to boost   ·   fly near a red block to eat it", 12.5, CMuted, FontWeights.Normal);
            l3.Margin = new Thickness(0, 14, 0, 0);
            overlayStack.Children.Add(l3);
            TextBlock l4 = T("Run into your own tail or a rock and you simply glide over it", 12.5, CMuted, FontWeights.Normal);
            l4.Margin = new Thickness(0, 6, 0, 0);
            overlayStack.Children.Add(l4);
            TextBlock l5 = T("Gold blocks are worth 5   ·   blocks stay where they are, so go back for them", 12.5, CGold, FontWeights.Normal);
            l5.Margin = new Thickness(0, 6, 0, 0);
            overlayStack.Children.Add(l5);

            if (best > 0)
            {
                TextBlock b = T("Your best so far: " + best, 12.5, CMuted, FontWeights.Normal);
                b.Margin = new Thickness(0, 22, 0, 0);
                overlayStack.Children.Add(b);
            }

            overlay.Background = new SolidColorBrush(Color.FromArgb(110, 2, 5, 11));
            overlay.Visibility = Visibility.Visible;
        }

        private void ShowPaused()
        {
            overlayStack.Children.Clear();
            overlayStack.Children.Add(T("Paused", 40, CText, FontWeights.Bold));
            TextBlock s = T("Press SPACE to carry on", 14, CMuted, FontWeights.Normal);
            s.Margin = new Thickness(0, 10, 0, 0);
            overlayStack.Children.Add(s);
            TextBlock st = T("Score " + score + "   ·   length " + beadCount + "   ·   " + eaten + " eaten",
                             12.5, CMuted, FontWeights.Normal);
            st.Margin = new Thickness(0, 18, 0, 0);
            overlayStack.Children.Add(st);
            overlay.Background = new SolidColorBrush(Color.FromArgb(95, 2, 5, 11));
            overlay.Visibility = Visibility.Visible;
        }

        private void HideOverlay() { overlay.Visibility = Visibility.Collapsed; }

        // =================================================================
        // persistence
        // =================================================================

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

        // =================================================================
        // world generation
        // =================================================================

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if (a % b != 0 && (a < 0) != (b < 0)) q--;
            return q;
        }

        // Scrambles three coordinates into one well-mixed number. Same chunk in,
        // same number out, forever -- which is what pins the world's layout down.
        private static uint Hash3(int x, int y, int z)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)x) * 16777619u;
                h = (h ^ (uint)y) * 16777619u;
                h = (h ^ (uint)z) * 16777619u;
                h ^= h >> 13; h *= 0x85ebca6bu; h ^= h >> 16;
                return h;
            }
        }

        private static Food FoodInChunk(C3 ch)
        {
            uint h1 = Hash3(ch.X, ch.Y, ch.Z);
            uint h2 = Hash3(ch.X + 7919, ch.Y + 104729, ch.Z + 1299709);

            Food f = new Food();
            f.Chunk = ch;
            f.Cell = new C3(ch.X * ChunkSize + (int)(h1 % ChunkSize),
                            ch.Y * ChunkSize + (int)((h1 >> 11) % ChunkSize),
                            ch.Z * ChunkSize + (int)(h2 % ChunkSize));
            f.Gold = (h2 >> 13) % GoldOneIn == 0;
            f.Phase = ((h2 >> 19) % 628) / 100.0;
            return f;
        }

        // A small clump of rocks per chunk, again straight out of the hash, so
        // they are landmarks that stay put rather than scenery that follows you.
        private static void RocksInChunk(C3 ch, List<C3> into)
        {
            uint h = Hash3(ch.X + 31337, ch.Y + 6971, ch.Z + 15485863);
            if (h % 100 >= 62) return;                       // not every chunk gets one

            int bx = ch.X * ChunkSize + (int)((h >> 3) % ChunkSize);
            int by = ch.Y * ChunkSize + (int)((h >> 11) % ChunkSize);
            int bz = ch.Z * ChunkSize + (int)((h >> 19) % ChunkSize);
            int count = 3 + (int)((h >> 27) % 5);

            for (int k = 0; k < count; k++)
            {
                uint hk = Hash3(bx + k * 977, by + k * 1049, bz + k * 1193);
                into.Add(new C3(bx + (int)(hk % 5) - 2,
                                by + (int)((hk >> 7) % 5) - 2,
                                bz + (int)((hk >> 14) % 5) - 2));
            }
        }

        // Collect everything belonging to chunks near the head, skipping food
        // already eaten. Cheap enough to redo on every step.
        private void RefreshWorld()
        {
            foods.Clear();
            rocks.Clear();
            rockSet.Clear();
            C3 h = new C3((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z));
            int cx = FloorDiv(h.X, ChunkSize), cy = FloorDiv(h.Y, ChunkSize), cz = FloorDiv(h.Z, ChunkSize);
            int r = (int)Math.Ceiling(ViewDistance / ChunkSize) + 1;

            for (int ix = cx - r; ix <= cx + r; ix++)
                for (int iy = cy - r; iy <= cy + r; iy++)
                    for (int iz = cz - r; iz <= cz + r; iz++)
                    {
                        C3 ch = new C3(ix, iy, iz);
                        if (!eatenChunks.Contains(ch))
                        {
                            Food f = FoodInChunk(ch);
                            if (f.Cell.DistTo(h) <= ViewDistance) foods.Add(f);
                        }

                        int before = rocks.Count;
                        RocksInChunk(ch, rocks);
                        for (int i = rocks.Count - 1; i >= before; i--)
                        {
                            if (rocks[i].DistTo(h) > ViewDistance) rocks.RemoveAt(i);
                            else rockSet.Add(rocks[i]);
                        }
                    }
        }

        // =================================================================
        // game flow
        // =================================================================

        private void StartGame()
        {
            pos = new Point3D(0, 0, 0);
            fwd = new Vector3D(1, 0, 0);
            up = new Vector3D(0, 1, 0);
            trail.Clear();
            trail.Add(pos);
            beads.Clear();
            beadCount = Beads0;
            inX = 0; inY = 0;
            speed = Speed0; lift = 0; refreshT = 0;
            particles.Clear();
            foods.Clear();
            eatenChunks.Clear();
            ClearPops();

            score = 0; eaten = 0;
            boosting = false;
            RefreshWorld();
            LayBeads();
#if CAPTURE
            // Worst case for the renderer: the longest body, every bead in view.
            if (Environment.GetEnvironmentVariable("SNAKE3D_LONG") == "1")
            {
                beadCount = BeadsMax;
                for (int i = 1; i < 400; i++)
                {
                    double a = i * 0.09;
                    trail.Add(new Point3D(Math.Cos(a) * 6 - 6, Math.Sin(a * 0.7) * 3, Math.Sin(a) * 6));
                }
                LayBeads();
            }
#endif

            state = GameState.Playing;
            camInit = false;
            HideOverlay();
            UpdateHud();
        }

        private void ToMenu()
        {
            state = GameState.Greeting;
            particles.Clear();
            ClearPops();
            camInit = false;
            ShowGreeting();
            UpdateHud();
        }

        private void TogglePause()
        {
            if (state == GameState.Playing) { state = GameState.Paused; ShowPaused(); }
            else if (state == GameState.Paused) { state = GameState.Playing; HideOverlay(); }
        }

        // Rodrigues: spin a vector around a unit axis.
        private static Vector3D Rot(Vector3D v, Vector3D axis, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            double k = Vector3D.DotProduct(axis, v) * (1 - c);
            Vector3D cr = Vector3D.CrossProduct(axis, v);
            return new Vector3D(v.X * c + cr.X * s + axis.X * k,
                                v.Y * c + cr.Y * s + axis.Y * k,
                                v.Z * c + cr.Z * s + axis.Z * k);
        }

        private static double Dist(Point3D a, Point3D b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        // Walk back along the flown path, dropping a bead every BeadGap units.
        // The body therefore traces the exact curve the head flew, however
        // gently it banked.
        private void LayBeads()
        {
            beads.Clear();
            beads.Add(pos);
            Point3D prev = pos;
            double walked = 0, want = BeadGap;
            for (int i = 0; i < trail.Count && beads.Count < beadCount; i++)
            {
                double seg = Dist(prev, trail[i]);
                while (seg > 1e-6 && walked + seg >= want && beads.Count < beadCount)
                {
                    double f = (want - walked) / seg;
                    beads.Add(new Point3D(prev.X + (trail[i].X - prev.X) * f,
                                          prev.Y + (trail[i].Y - prev.Y) * f,
                                          prev.Z + (trail[i].Z - prev.Z) * f));
                    want += BeadGap;
                }
                walked += seg;
                prev = trail[i];
            }
        }

        // Anything solid just ahead? Used only to lift over it -- nothing can
        // end a run.
        private bool BlockedAhead()
        {
            Point3D probe = new Point3D(pos.X + fwd.X * 2.4, pos.Y + fwd.Y * 2.4, pos.Z + fwd.Z * 2.4);
            for (int i = 10; i < beads.Count; i++)
                if (Dist(probe, beads[i]) < 1.4) return true;
            for (int r = 0; r < rocks.Count; r++)
                if (Dist(probe, new Point3D(rocks[r].X, rocks[r].Y, rocks[r].Z)) < 1.6) return true;
            return false;
        }

        private void Advance(double dt)
        {
            if (dt > 0.08) dt = 0.08;

            // bank and climb by however long the control is held
            double ang = TurnRate * dt;
            if (inX != 0) fwd = Rot(fwd, up, -inX * ang);
            if (inY != 0)
            {
                Vector3D right = Norm(Vector3D.CrossProduct(fwd, up));
                fwd = Rot(fwd, right, -inY * ang);
                up = Rot(up, right, -inY * ang);
            }
            // keep the frame orthonormal, or it drifts and skews over time
            fwd = Norm(fwd);
            up = Norm(up - fwd * Vector3D.DotProduct(up, fwd));

            // rise over whatever is in the way; the path itself bends, so the
            // whole body follows the same arc a moment later
            double wantLift = BlockedAhead() ? HopLift : 0;
            double nl = lift + (wantLift - lift) * (1 - Math.Exp(-5 * dt));
            double dLift = nl - lift;
            lift = nl;

            double sp = speed * (boosting ? 1.8 : 1.0);
            pos = new Point3D(pos.X + fwd.X * sp * dt + up.X * dLift,
                              pos.Y + fwd.Y * sp * dt + up.Y * dLift,
                              pos.Z + fwd.Z * sp * dt + up.Z * dLift);

            // record the path
            if (Dist(pos, trail[0]) >= PathSample)
            {
                trail.Insert(0, pos);
                int keep = (int)Math.Ceiling(beadCount * BeadGap / PathSample) + 6;
                if (trail.Count > keep) trail.RemoveRange(keep, trail.Count - keep);
            }
            LayBeads();

            // collect anything flown near
            for (int i = 0; i < foods.Count; i++)
            {
                Food f = foods[i];
                if (Dist(new Point3D(f.Cell.X, f.Cell.Y, f.Cell.Z), pos) > GrabDist) continue;
                score += f.Gold ? 5 : 1;
                eaten += 1;
                beadCount = Math.Min(BeadsMax, beadCount + (f.Gold ? 4 : 2));
                speed = Math.Min(15.0, Speed0 + eaten * 0.11);
                Spawn(f.Cell.X, f.Cell.Y, f.Cell.Z, f.Gold ? 22 : 12,
                      f.Gold ? goldMat : foodMat, f.Gold ? 0.22 : 0.24, f.Gold ? 4.2 : 3.2);
                Say(f.Gold);
                eatenChunks.Add(f.Chunk);
                if (score > best) best = score;
                RefreshWorld();
                UpdateHud();
                break;
            }

            refreshT += dt;
            if (refreshT > 0.25) { refreshT = 0; RefreshWorld(); }
        }

        // Zero-gravity burst: particles drift outward and fade.
        private void Spawn(double x, double y, double z, int n, Material m, double size, double speed)
        {
            for (int i = 0; i < n && particles.Count < MaxParticles; i++)
            {
                Particle p = new Particle();
                p.Pos = new Point3D(x + (rng.NextDouble() - 0.5) * 0.4,
                                    y + (rng.NextDouble() - 0.5) * 0.4,
                                    z + (rng.NextDouble() - 0.5) * 0.4);
                double th = rng.NextDouble() * Math.PI * 2;
                double ph = Math.Acos(2 * rng.NextDouble() - 1);
                double sp = speed * (0.35 + rng.NextDouble() * 0.9);
                p.Vel = new Vector3D(Math.Sin(ph) * Math.Cos(th) * sp,
                                     Math.Cos(ph) * sp,
                                     Math.Sin(ph) * Math.Sin(th) * sp);
                p.MaxLife = p.Life = 0.7 + rng.NextDouble() * 0.6;
                p.Size = size;
                p.Mat = m;
                particles.Add(p);
            }
        }

        // =================================================================
        // the snake talking with its mouth full
        // =================================================================

        private void Say(bool gold)
        {
            string[] pick = gold ? GoldYums : Yums;
            Pop p = new Pop();
            p.Size = gold ? 27 : 21;
            p.Label = T(pick[rng.Next(pick.Length)], p.Size, gold ? CGold : CText, FontWeights.Bold);
            // Invisible until UpdatePops has placed it; otherwise it shows for
            // one frame in the corner, where Canvas puts anything unpositioned.
            p.Label.Opacity = 0;
            p.MaxLife = p.Life = 1.3;
            p.Drift = (rng.NextDouble() - 0.5) * 90;
            popLayer.Children.Add(p.Label);
            pops.Add(p);
            if (pops.Count > 6) { popLayer.Children.Remove(pops[0].Label); pops.RemoveAt(0); }
        }

        private void ClearPops()
        {
            popLayer.Children.Clear();
            pops.Clear();
        }

        // The chase camera keeps the head in much the same spot on screen, so
        // the words can just rise from there without projecting 3D to 2D.
        private void UpdatePops(double dt)
        {
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                Pop p = pops[i];
                p.Life -= dt;
                if (p.Life <= 0)
                {
                    popLayer.Children.Remove(p.Label);
                    pops.RemoveAt(i);
                    continue;
                }

                double k = 1 - p.Life / p.MaxLife;          // 0 -> 1
                double w = popLayer.ActualWidth, h = popLayer.ActualHeight;
                if (w <= 0) continue;

                p.Label.Opacity = p.Life < 0.4 ? p.Life / 0.4 : Math.Min(1, k * 6);
                p.Label.FontSize = p.Size * (0.7 + 0.3 * Math.Min(1, k * 5));
                p.Label.Measure(new Size(1000, 1000));
                Canvas.SetLeft(p.Label, w / 2 + p.Drift - p.Label.DesiredSize.Width / 2);
                Canvas.SetTop(p.Label, h * 0.56 - k * 120);
            }
        }

        private void UpdateHud()
        {
            scoreText.Text = score.ToString();
            bestText.Text = best.ToString();
            switch (state)
            {
                case GameState.Greeting: hintText.Text = "ENTER  fly      ESC  back"; break;
                case GameState.Playing:  hintText.Text = "←  →  bank      ↑  ↓  climb / dive      SHIFT  boost      SPACE  pause      ESC  menu"; break;
                default:                 hintText.Text = "SPACE  resume      ESC  menu"; break;
            }
        }

        // =================================================================
        // input
        // =================================================================

        // Held input rather than queued turns - that is what makes a turn gradual.
        private void Hold(int x, int y)
        {
            if (state != GameState.Playing) { inX = 0; inY = 0; return; }
            if (x != 99) inX = x;
            if (y != 99) inY = y;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            Key k = e.Key;
            if (k == Key.System) k = e.SystemKey;

            if (k == Key.Left || k == Key.A) { Hold(-1, 99); e.Handled = true; return; }
            if (k == Key.Right || k == Key.D) { Hold(1, 99); e.Handled = true; return; }
            if (k == Key.Up || k == Key.W) { Hold(99, -1); e.Handled = true; return; }
            if (k == Key.Down || k == Key.S) { Hold(99, 1); e.Handled = true; return; }
            if (k == Key.LeftShift || k == Key.RightShift) { boosting = true; e.Handled = true; return; }

            if (k == Key.Enter)
            {
                if (state == GameState.Greeting) StartGame();
                else if (state == GameState.Paused) TogglePause();
                e.Handled = true;
            }
            else if (k == Key.Space || k == Key.P)
            {
                if (state == GameState.Greeting) StartGame();
                else TogglePause();
                e.Handled = true;
            }
            else if (k == Key.Escape)
            {
                if (state == GameState.Greeting) Close(); else ToMenu();   // back to the picker
                e.Handled = true;
            }
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            Key k = e.Key;
            if (k == Key.System) k = e.SystemKey;
            if (k == Key.LeftShift || k == Key.RightShift) boosting = false;
            else if (k == Key.Left || k == Key.A || k == Key.Right || k == Key.D) inX = 0;
            else if (k == Key.Up || k == Key.W || k == Key.Down || k == Key.S) inY = 0;
        }

        // =================================================================
        // per-frame update + render
        // =================================================================

        private void OnFrame(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = now - lastTime;
            lastTime = now;
            if (dt > 0.25) dt = 0.25;
            if (dt <= 0) return;

            if (state == GameState.Playing) Advance(dt);

            UpdateParticles(dt);
            UpdatePops(dt);
            Render(now, dt);

            if (dbgText != null)
            {
                frames++;
                fpsAcc += dt;
                if (fpsAcc >= 0.5) { fps = frames / fpsAcc; frames = 0; fpsAcc = 0; }
                dbgText.Text = string.Format("{0}  fps {1:0}  at {2:0.0},{3:0.0},{4:0.0}  beads {5}  food {6}  rocks {7}",
                                             state, fps, pos.X, pos.Y, pos.Z, beads.Count, foods.Count, rocks.Count);
            }
#if CAPTURE
            CaptureTick();
#endif
        }

        private void UpdateParticles(double dt)
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                Particle p = particles[i];
                p.Life -= dt;
                if (p.Life <= 0) { particles.RemoveAt(i); continue; }
                p.Pos = new Point3D(p.Pos.X + p.Vel.X * dt, p.Pos.Y + p.Vel.Y * dt, p.Pos.Z + p.Vel.Z * dt);
                double drag = 1 - Math.Min(0.9, 1.6 * dt);
                p.Vel = new Vector3D(p.Vel.X * drag, p.Vel.Y * drag, p.Vel.Z * drag);
            }
        }

        // A thin three-axis cross through a block, so a distant one's position
        // in space is readable from any angle. It grows with distance and
        // retracts as you close in, where the block speaks for itself.
        private void Marker(double x, double y, double z, double dist, Material m)
        {
            double len = Math.Min(4.5, (dist - 2.5) * 0.6);
            if (len < 0.3) return;
            const double w = 0.045;
            pool.Add(cubeGeo, x, y, z, len, w, w, 0, m);
            pool.Add(cubeGeo, x, y, z, w, len, w, 0, m);
            pool.Add(cubeGeo, x, y, z, w, w, len, 0, m);
        }

        private void Render(double t, double dt)
        {
            bool alive = state == GameState.Playing || state == GameState.Paused;

            double headX = pos.X, headY = pos.Y, headZ = pos.Z;
            Vector3D F = fwd, U = up;
            Vector3D R = Norm(Vector3D.CrossProduct(F, U));

            UpdateCamera(t, dt, headX, headY, headZ, F, U);

            pool.Begin();

            if (alive && beads.Count > 0)
            {
                // The beads sit along the flown path, close enough to overlap,
                // so the body reads as one smooth curve through whatever arc
                // the head just made.
                int n = beads.Count;
                for (int i = n - 1; i >= 0; i--)
                {
                    Point3D p = beads[i];
                    double k = n <= 1 ? 0 : i / (double)(n - 1);
                    double size = i == 0 ? 0.98 : 0.88 - k * 0.26;
                    Material m = i == 0 ? headMat : bodyMats[Math.Min(bodyMats.Length - 1, (int)(k * bodyMats.Length))];
                    pool.Add(sphereGeo, p.X, p.Y, p.Z, size, 0, m);
                }

                // eyes on the leading face of the head
                for (int s = -1; s <= 1; s += 2)
                    pool.Add(beadGeo, headX + F.X * 0.40 + R.X * 0.22 * s,
                             headY + F.Y * 0.40 + R.Y * 0.22 * s,
                             headZ + F.Z * 0.40 + R.Z * 0.22 * s, 0.19, 0, eyeMat);

                for (int i = 0; i < foods.Count; i++)
                {
                    Food f = foods[i];
                    double dx = f.Cell.X - headX, dy = f.Cell.Y - headY, dz = f.Cell.Z - headZ;
                    double d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 > ViewDistance * ViewDistance) continue;
                    double d = Math.Sqrt(d2);

                    double bob = Math.Sin(t * 3.0 + f.Phase) * 0.09;
                    double size = f.Gold ? 1.7 : 1.5;
                    pool.Add(cubeGeo, f.Cell.X, f.Cell.Y + bob, f.Cell.Z, size,
                             t * (f.Gold ? 130 : 55) + f.Phase * 30, f.Gold ? goldMat : foodMat);
                    if (d < MarkerRange)
                        Marker(f.Cell.X, f.Cell.Y + bob, f.Cell.Z, d, f.Gold ? goldMarkMat : markMat);
                }

                for (int i = 0; i < rocks.Count; i++)
                {
                    C3 c = rocks[i];
                    double dx = c.X - headX, dy = c.Y - headY, dz = c.Z - headZ;
                    if (dx * dx + dy * dy + dz * dz > ViewDistance * ViewDistance) continue;
                    uint hk = Hash3(c.X, c.Y, c.Z);
                    double s = 1.1 + (hk % 60) / 100.0;
                    pool.Add(cubeGeo, c.X, c.Y, c.Z, s, (hk >> 8) % 90, ((hk >> 5) & 1) == 0 ? rockMat : rockMat2);
                }
            }

            for (int i = 0; i < particles.Count; i++)
            {
                Particle p = particles[i];
                double shrink = Math.Max(0.15, p.Life / p.MaxLife);
                pool.Add(cubeGeo, p.Pos.X, p.Pos.Y, p.Pos.Z, p.Size * shrink, (p.Life * 200) % 360, p.Mat);
            }

            pool.End();

            latticeXf.OffsetX = Math.Round(headX / LatticeStep) * LatticeStep;
            latticeXf.OffsetY = Math.Round(headY / LatticeStep) * LatticeStep;
            latticeXf.OffsetZ = Math.Round(headZ / LatticeStep) * LatticeStep;

            // Stars follow the camera exactly, so they never get any closer.
            starXf.OffsetX = camPos.X;
            starXf.OffsetY = camPos.Y;
            starXf.OffsetZ = camPos.Z;
        }


        private void UpdateCamera(double t, double dt, double headX, double headY, double headZ,
                                  Vector3D F, Vector3D U)
        {
            Point3D wantPos, wantTgt;
            Vector3D wantUp;

            if (state == GameState.Greeting)
            {
                double a = t * 0.16;
                wantPos = new Point3D(headX + Math.Sin(a) * 26.0, headY + 9.0, headZ + Math.Cos(a) * 26.0);
                wantTgt = new Point3D(headX, headY, headZ);
                wantUp = new Vector3D(0, 1, 0);
            }
            else
            {
                double back = boosting ? 10.6 : 9.4;
                double high = boosting ? 3.8 : 4.3;
                wantPos = new Point3D(headX - F.X * back + U.X * high,
                                      headY - F.Y * back + U.Y * high,
                                      headZ - F.Z * back + U.Z * high);
                wantTgt = new Point3D(headX + F.X * 4.4, headY + F.Y * 4.4, headZ + F.Z * 4.4);
                wantUp = U;
            }

            if (state == GameState.Greeting)
            {
                // The orbit has no heading to follow, so ease the camera itself.
                double k = camInit ? 1 - Math.Exp(-7.5 * dt) : 1.0;
                camPos = new Point3D(camPos.X + (wantPos.X - camPos.X) * k,
                                     camPos.Y + (wantPos.Y - camPos.Y) * k,
                                     camPos.Z + (wantPos.Z - camPos.Z) * k);
                camTgt = new Point3D(camTgt.X + (wantTgt.X - camTgt.X) * k,
                                     camTgt.Y + (wantTgt.Y - camTgt.Y) * k,
                                     camTgt.Z + (wantTgt.Z - camTgt.Z) * k);
                camUp = Norm(camUp + (wantUp - camUp) * k);
                camFwd = Norm(new Vector3D(camTgt.X - camPos.X, camTgt.Y - camPos.Y, camTgt.Z - camPos.Z));
                camInit = true;
            }
            else
            {
                // The heading itself changes gradually now, so the camera only
                // needs a light trail behind it. Smoothing its heading and
                // hanging the position off that keeps it sweeping round the
                // snake rather than cutting the corner.
                double k = camInit ? 1 - Math.Exp(-7.0 * dt) : 1.0;

                camFwd = Norm(camFwd + (F - camFwd) * k);
                Vector3D un = camUp + (U - camUp) * k;
                // keep up square to forward, or the view slowly skews
                camUp = Norm(un - camFwd * Vector3D.DotProduct(un, camFwd));

                double back = boosting ? 10.6 : 9.4;
                double high = boosting ? 3.8 : 4.3;
                camPos = new Point3D(headX - camFwd.X * back + camUp.X * high,
                                     headY - camFwd.Y * back + camUp.Y * high,
                                     headZ - camFwd.Z * back + camUp.Z * high);
                camTgt = new Point3D(headX + camFwd.X * 4.4, headY + camFwd.Y * 4.4, headZ + camFwd.Z * 4.4);
                camInit = true;
            }

            cam.Position = camPos;
            cam.LookDirection = new Vector3D(camTgt.X - camPos.X, camTgt.Y - camPos.Y, camTgt.Z - camPos.Z);
            cam.UpDirection = camUp;
        }

#if CAPTURE
        // Test-only harness (built into a separate binary with /define:CAPTURE).
        // Renders the live visual tree straight to PNGs so the game can be
        // inspected frame by frame without screen-scraping the desktop.
        private int capFrame;
        private string capDir;
        private bool fpsLog;
        private int fpsFrames;
        private double lastSay;
        // Close() is not immediate, so without this the block below runs again on
        // the next frame -- on state it has already torn down.
        private bool capDone;

        // The game is opened as a dialog by the launcher, so Close() just hands
        // control back and the process lives on. Tests want the whole app gone.
        private static void CaptureQuit()
        {
            Application.Current.Shutdown();
        }

        private void CaptureInit()
        {
            capDir = Environment.GetEnvironmentVariable("SNAKE3D_CAPTURE");
            if (string.IsNullOrEmpty(capDir)) { capDir = null; return; }
            Directory.CreateDirectory(capDir);
            fpsLog = Environment.GetEnvironmentVariable("SNAKE3D_FPSLOG") == "1";
            if (Environment.GetEnvironmentVariable("SNAKE3D_CAPTURE_MENU") != "1")
                Loaded += delegate { StartGame(); };
        }

        private static bool Same(List<C3> a, List<C3> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private List<C3> SnapshotFood()
        {
            List<C3> cells = new List<C3>();
            for (int i = 0; i < foods.Count; i++) cells.Add(foods[i].Cell);
            cells.Sort(delegate(C3 a, C3 b)
            {
                if (a.X != b.X) return a.X - b.X;
                if (a.Y != b.Y) return a.Y - b.Y;
                return a.Z - b.Z;
            });
            return cells;
        }

        private void CaptureTick()
        {
            if (capDir == null) return;
            double t = clock.Elapsed.TotalSeconds;

            if (fpsLog)
            {
                if (t > 2.0 && t < 8.0) fpsFrames++;
                else if (t >= 8.0)
                {
                    File.WriteAllText(Path.Combine(capDir, "fps.txt"),
                        string.Format("{0:0.0} fps over 6s  score {1}  len {2}  food {3}  rocks {4}",
                                      fpsFrames / 6.0, score, beads.Count, foods.Count, rocks.Count));
                    CaptureQuit();
                }
                return;
            }

            if (Environment.GetEnvironmentVariable("SNAKE3D_TESTWORLD") == "1")
            {
                if (t < 2.0 || capDone) return;
                capDone = true;
                Point3D origin = pos;

                RefreshWorld();
                List<C3> before = SnapshotFood();
                int inView = before.Count, rockCount = rocks.Count;

                // 1. leave, return: the neighbourhood must be untouched
                pos = new Point3D(origin.X + 4000, origin.Y - 2500, origin.Z + 900);
                RefreshWorld();
                int elsewhere = foods.Count;
                pos = origin;
                RefreshWorld();
                bool unchanged = Same(before, SnapshotFood());

                // 2. eat one: it must go, and only it
                C3 gone = foods[3].Cell;
                eatenChunks.Add(foods[3].Chunk);
                RefreshWorld();
                List<C3> afterEat = SnapshotFood();
                bool collected = afterEat.Count == before.Count - 1 && !afterEat.Contains(gone);

                // 3. leave, return again: still gone, rest intact
                pos = new Point3D(origin.X - 3000, origin.Y + 1500, origin.Z - 700);
                RefreshWorld();
                pos = origin;
                RefreshWorld();
                bool staysGone = Same(afterEat, SnapshotFood());

                // 4. the forgiving grab: fly past a block without landing on it
                RefreshWorld();
                C3 target = foods[0].Cell;
                pos = new Point3D(target.X - 1.2, target.Y + 1.0, target.Z - 0.6);
                int scoreBefore = score;
                Advance(0.016);
                bool grabbed = score > scoreBefore;

                // 5. the bank: hold a turn and check the heading sweeps round
                //    gradually and settles between the axes
                pos = new Point3D(0, 0, 0);
                fwd = new Vector3D(1, 0, 0); up = new Vector3D(0, 1, 0);
                trail.Clear(); trail.Add(pos); LayBeads();
                inX = 1;
                Vector3D f0 = fwd;
                double half = 0;
                for (int i = 0; i < 48; i++)
                {
                    Advance(1.0 / 60);
                    if (i == 23) half = Math.Acos(Math.Max(-1, Math.Min(1,
                        Vector3D.DotProduct(f0, fwd)))) * 180 / Math.PI;
                }
                double swept = Math.Acos(Math.Max(-1, Math.Min(1,
                    Vector3D.DotProduct(f0, fwd)))) * 180 / Math.PI;
                bool gradual = half > 20 && half < swept - 10;

                inX = 0; inY = -1;
                for (int i = 0; i < 20; i++) Advance(1.0 / 60);
                int axes = 0;
                if (Math.Abs(fwd.X) > 0.08) axes++;
                if (Math.Abs(fwd.Y) > 0.08) axes++;
                if (Math.Abs(fwd.Z) > 0.08) axes++;
                bool diagonal = axes > 1;

                File.WriteAllText(Path.Combine(capDir, "world.txt"), string.Format(
                    "blocks within view {0}, rocks {1}, and {2} blocks somewhere else\r\n" +
                    "world unchanged after leaving and returning  : {3}\r\n" +
                    "eating one removes exactly that block         : {4}\r\n" +
                    "it is still gone after leaving and returning  : {5}\r\n" +
                    "block eaten by flying near, not onto, it      : {6}\r\n" +
                    "a held turn sweeps round gradually ({7:0} deg) : {8}\r\n" +
                    "heading settles between axes (diagonal)       : {9}",
                    inView, rockCount, elsewhere,
                    unchanged ? "PASS" : "FAIL",
                    collected ? "PASS" : "FAIL",
                    staysGone ? "PASS" : "FAIL",
                    grabbed ? "PASS" : "FAIL",
                    swept, gradual ? "PASS" : "FAIL",
                    diagonal ? "PASS" : "FAIL"));
                CaptureQuit();
                return;
            }

            // Keep the snake talking, so captures actually catch a popup.
            if (Environment.GetEnvironmentVariable("SNAKE3D_SAY") == "1" && t - lastSay > 0.9)
            {
                lastSay = t;
                Say(pops.Count % 3 == 0);
            }

            if (t < 1.2) return;
            int want = (int)((t - 1.2) * 9);
            if (want <= capFrame) return;
            capFrame = want;
            if (capFrame > 22) { CaptureQuit(); return; }

            int w = (int)ActualWidth, h = (int)ActualHeight;
            if (w <= 0 || h <= 0) return;
            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(this);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            string name = string.Format("cap{0:00}_s{1}.png", capFrame, score);
            using (FileStream fs = new FileStream(Path.Combine(capDir, name), FileMode.Create))
                enc.Save(fs);
        }
#endif

    }
}
