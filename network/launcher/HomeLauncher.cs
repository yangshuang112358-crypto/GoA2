using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// Standalone .NET Framework Windows entry. No SDK or project paths at runtime.
internal sealed class GoaHome : Form
{
    readonly string package = AppDomain.CurrentDomain.BaseDirectory;
    readonly Button tutorial, local, network;
    readonly Label status;
    readonly Timer timer = new Timer();
    Process child;
    string mode = "";

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var form = new GoaHome())
        {
            if (args.Length == 2 && args[0] == "--smoke")
            {
                var capture = new Timer { Interval = 1200 };
                capture.Tick += delegate {
                    capture.Stop();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                    capture.Dispose(); form.Close();
                };
                form.Shown += delegate { capture.Start(); };
            }
            Application.Run(form);
        }
    }

    GoaHome()
    {
        Text = "Goa2V1 · 开始游戏";
        ClientSize = new Size(680, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 11);
        BackColor = Color.FromArgb(20, 29, 37); ForeColor = Color.FromArgb(235, 231, 220);
        AddLabel("GUARDS OF ATLANTIS II", 32, 24, 616, 50, 24).ForeColor = Color.FromArgb(210, 183, 127);
        AddLabel("Goa2V1  ·  本地练习 / 四人邀请联机", 34, 79, 612, 32, 12);
        tutorial = AddButton("新手教程", 32, 137, "Tutorial");
        local = AddButton("本地对局", 352, 137, "Local");
        AddLabel("单人分章练习，可退出后继续", 34, 214, 292, 30, 11);
        AddLabel("同一电脑操作四个席位", 354, 214, 292, 30, 11);
        network = AddButton("好友联机", 32, 265, "Network");
        AddButton("使用说明", 352, 265, "Help");
        AddLabel("一人开房，三人导入个人邀请", 34, 342, 292, 30, 11);
        AddLabel("首次启动、操作、更新与排错", 354, 342, 292, 30, 11);
        status = AddLabel("", 34, 391, 612, 44, 11);
        status.ForeColor = Color.FromArgb(211, 183, 130);
        string version = "Windows x64 完整试玩包 · 音量与热键在游戏设置中";
        if (File.Exists(Path.Combine(package, "version.txt")))
            version = File.ReadAllLines(Path.Combine(package, "version.txt"))[0];
        AddLabel(version, 34, 457, 612, 30, 10).ForeColor = Color.FromArgb(159, 174, 184);
        timer.Interval = 500; timer.Tick += delegate { RefreshState(); }; timer.Start(); RefreshState();
        FormClosed += delegate { timer.Dispose(); if (child != null) child.Dispose(); };
    }

    Label AddLabel(string text, int x, int y, int width, int height, int size)
    {
        var label = new Label { Text = text, Bounds = new Rectangle(x, y, width, height), Font = new Font("Microsoft YaHei UI", size) };
        Controls.Add(label); return label;
    }
    Button AddButton(string text, int x, int y, string destination)
    {
        var button = new Button { Text = text, Bounds = new Rectangle(x, y, 296, 68), FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 48, 60), Font = new Font("Microsoft YaHei UI", 14) };
        button.FlatAppearance.BorderColor = Color.FromArgb(169, 145, 95);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(57, 73, 84);
        button.Click += delegate { Launch(destination); }; Controls.Add(button); return button;
    }
    bool Running { get { return child != null && !child.HasExited; } }
    void RefreshState()
    {
        bool running = Running;
        local.Enabled = tutorial.Enabled = network.Enabled = !running;
        status.Text = running ? (mode == "Network" ? "联机窗口已打开。开房与加入请在该窗口完成，并保持它运行。" : "游戏已打开。关闭游戏后，可在这里选择其他模式。")
            : "第一次玩？建议先完成新手教程，再邀请好友。";
    }
    void Launch(string destination)
    {
        try
        {
            if (destination != "Help" && Running) return;
            var start = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = package };
            if (destination == "Help")
            {
                Require("README.txt");
                start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32/notepad.exe");
                start.Arguments = Quote(Path.Combine(package, "README.txt"));
            }
            else if (destination == "Network")
            {
                Require("launcher/Launcher.ps1");
                start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32/WindowsPowerShell/v1.0/powershell.exe");
                start.Arguments = "-NoProfile -STA -File " + Quote(Path.Combine(package, "launcher/Launcher.ps1"));
                start.CreateNoWindow = true;
            }
            else
            {
                Require("player/Goa2V1.exe");
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Goa2V1/Logs");
                Directory.CreateDirectory(logDir);
                start.FileName = Path.Combine(package, "player/Goa2V1.exe");
                start.WorkingDirectory = Path.Combine(package, "player");
                start.Arguments = (destination == "Tutorial" ? "-goaTutorial " : "") +
                    "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile " +
                    Quote(Path.Combine(logDir, destination.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N") + ".log"));
            }
            var started = Process.Start(start);
            if (destination != "Help")
            {
                if (child != null) child.Dispose();
                child = started; mode = destination; RefreshState();
            }
            else if (started != null) started.Dispose();
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "启动未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    void Require(string relative)
    {
        if (!File.Exists(Path.Combine(package, relative))) throw new IOException("运行文件缺失。请完整解压整个游戏包，不要只复制入口程序。");
    }
    static string Quote(string value) { return "\"" + value + "\""; }
}
