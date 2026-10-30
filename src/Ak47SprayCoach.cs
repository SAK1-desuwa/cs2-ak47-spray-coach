// ============================================================
//  AK-47 压枪分段提醒器 · 桌面版 (WinForms / .NET Framework 4.x)
//  按住鼠标左键 = 开始扫射，松开 = 停止；每次开始语速 +1
//  编译: csc /target:winexe /codepage:65001 ...
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Ak47Spray
{
    // ---------- 数据模型 ----------
    internal sealed class Seg
    {
        public int From, To;
        public string Dir, Strength, Note;
        public Seg(int f, int t, string d, string s, string n)
        {
            From = f; To = t; Dir = d; Strength = s; Note = n;
        }
    }

    internal sealed class DirInfo
    {
        public string Glyph, Name, Verb, Word;
        public Color Col;
        public float Vx, Vy;     // 屏幕坐标：y 向下为正
        public DirInfo(string g, string n, string v, string w, Color c, float x, float y)
        {
            Glyph = g; Name = n; Verb = v; Word = w; Col = c; Vx = x; Vy = y;
        }
    }

    internal static class Engine
    {
        public const int RateHz = 10;          // 600 RPM
        public const double DT = 0.1;          // 每发 0.1s
        public const int N = 30;               // 弹容
        public const double Total = N * DT;    // 3.0s

        // 弹道骨架：x 向右为正，y 向上为正
        public static readonly double[][] Pattern = new double[][]
        {
            new double[]{  0.0,  0.0 }, new double[]{  0.3,  3.2 }, new double[]{  0.5,  6.4 },
            new double[]{  0.6,  9.6 }, new double[]{  0.4, 12.8 }, new double[]{  0.0, 16.0 },
            new double[]{ -0.8, 19.2 }, new double[]{ -1.8, 22.2 }, new double[]{ -2.6, 25.0 },
            new double[]{ -1.4, 27.6 }, new double[]{  1.5, 29.8 }, new double[]{  5.2, 31.6 },
            new double[]{  9.0, 32.8 }, new double[]{ 11.5, 34.2 }, new double[]{ 12.4, 35.8 },
            new double[]{ 11.0, 37.4 }, new double[]{  7.0, 38.6 }, new double[]{  2.0, 39.6 },
            new double[]{ -2.5, 40.6 }, new double[]{ -4.5, 41.8 }, new double[]{ -3.2, 43.0 },
            new double[]{  0.5, 44.0 }, new double[]{  5.0, 44.8 }, new double[]{  9.0, 45.6 },
            new double[]{ 11.0, 46.6 }, new double[]{ 10.0, 47.8 }, new double[]{  6.0, 48.8 },
            new double[]{  1.0, 49.6 }, new double[]{ -4.0, 50.2 }, new double[]{ -8.0, 50.8 }
        };

        public static readonly Seg[] Segs = new Seg[]
        {
            new Seg(1, 4, "down", "强", "首发射在准星上，第 2 发起枪口猛抬。这段垂直量最大，要快速、匀速地下压，别一下压到底。"),
            new Seg(5, 8, "downRight", "弱", "弹道开始轻微向左偏，右手留一点余量，为下一段的大幅左拉做过渡。"),
            new Seg(9, 14, "downLeft", "强", "弹道向右甩出整条弹道里最大的一段。只往下压必打空，这里必须主动往左拉。"),
            new Seg(15, 19, "downRight", "中", "弹道回摆向左，鼠标反向往右带，水平幅度比上一段小。"),
            new Seg(20, 24, "downLeft", "中", "第二次向右摆，幅度略小、节奏更急，手腕要跟上。"),
            new Seg(25, 30, "downRight", "中", "收尾向左上方飘且抖动最大，命中率最低；实战到这个距离建议停枪重新定位。")
        };

        public static readonly Dictionary<string, DirInfo> Dirs = new Dictionary<string, DirInfo>();

        static Engine()
        {
            Dirs["down"] = new DirInfo("↓", "正下方", "垂直下压", "往下", Color.FromArgb(245, 166, 35), 0f, 1f);
            Dirs["downRight"] = new DirInfo("↘", "右下方", "往右下带", "右下", Color.FromArgb(255, 107, 90), 0.45f, 1f);
            Dirs["downLeft"] = new DirInfo("↙", "左下方", "往左下拉", "左下", Color.FromArgb(79, 163, 255), -0.45f, 1f);
        }

        public static int Clamp(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }

        public static int ShotAt(double t)
        {
            int i = (int)Math.Floor(t / DT + 1e-9);
            return Clamp(i, 0, N - 1);
        }

        public static int SegOf(int shot)
        {
            for (int k = 0; k < Segs.Length; k++)
                if (shot >= Segs[k].From && shot <= Segs[k].To) return k;
            return Segs.Length - 1;
        }

        public static double SegStart(int k) { return (Segs[k].From - 1) * DT; }
        public static double SegEnd(int k) { return Segs[k].To * DT; }
        public static DirInfo DirOfSeg(int k) { return Dirs[Segs[k].Dir]; }
        public static string Word(int k) { return Dirs[Segs[k].Dir].Word; }
    }

    // ---------- 配色 / 字体 ----------
    internal static class Th
    {
        public static readonly Color Bg = Color.FromArgb(11, 14, 18);
        public static readonly Color Card = Color.FromArgb(18, 22, 28);
        public static readonly Color Card2 = Color.FromArgb(23, 28, 35);
        public static readonly Color Line = Color.FromArgb(38, 46, 56);
        public static readonly Color Text = Color.FromArgb(233, 238, 245);
        public static readonly Color Dim = Color.FromArgb(139, 151, 166);
        public static readonly Color Bright = Color.White;

        public static readonly Font FTitle = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        public static readonly Font FSub = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FBadge = new Font("Consolas", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FCueLbl = new Font("Microsoft YaHei UI", 24f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FCueDir = new Font("Microsoft YaHei UI", 31f, FontStyle.Bold, GraphicsUnit.Pixel);
        public static readonly Font FInfo = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FInfoB = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        public static readonly Font FSeg = new Font("Consolas", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        public static readonly Font FTick = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FTickN = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FTable = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FTableH = new Font("Consolas", 10.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        public static readonly Font FMono = new Font("Consolas", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
        public static readonly Font FStatus = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    // ---------- 设置 ----------
    internal sealed class Cfg
    {
        public string Voice = "";        // 语音名（空 = 自动）
        public int Pitch = 0;            // 音调 -50..60 (%)
        public int Rate = 0;             // 基础语速 0..6
        public bool ShotOn = true;       // 射击音效
        public string ShotWav = "";      // 自定义射击音效（空 = 用内置合成）
        public int ShotVol = 70;         // 射击音量 0..100
        public bool CueOn = true;        // 换段提示音
        public string CueWav = "";       // 自定义提示音 wav
        public int Lead = 80;            // 提前量 ms（0..3000，无上限菜单限制）
        public string Wallpaper = "";    // 壁纸路径
        public int Dim = 55;             // 壁纸暗化 0..90
        public bool TopMost = true;
        public bool Hook = true;
        public bool Progressive = true;
        public bool Sound = true;

        public void Reset()
        {
            Voice = ""; Pitch = 0; Rate = 0;
            ShotOn = true; ShotWav = ""; ShotVol = 70;
            CueOn = true; CueWav = "";
            Lead = 80; Wallpaper = ""; Dim = 55;
            TopMost = true; Hook = true; Progressive = true; Sound = true;
        }

        public Cfg Clone()
        {
            Cfg c = new Cfg();
            c.Voice = Voice; c.Pitch = Pitch; c.Rate = Rate;
            c.ShotOn = ShotOn; c.ShotWav = ShotWav; c.ShotVol = ShotVol;
            c.CueOn = CueOn; c.CueWav = CueWav;
            c.Lead = Lead; c.Wallpaper = Wallpaper; c.Dim = Dim;
            c.TopMost = TopMost; c.Hook = Hook; c.Progressive = Progressive; c.Sound = Sound;
            return c;
        }

        public void CopyFrom(Cfg o)
        {
            Voice = o.Voice; Pitch = o.Pitch; Rate = o.Rate;
            ShotOn = o.ShotOn; ShotWav = o.ShotWav; ShotVol = o.ShotVol;
            CueOn = o.CueOn; CueWav = o.CueWav;
            Lead = o.Lead; Wallpaper = o.Wallpaper; Dim = o.Dim;
            TopMost = o.TopMost; Hook = o.Hook; Progressive = o.Progressive; Sound = o.Sound;
        }

        public void Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i].Trim();
                    if (ln.Length == 0 || ln.StartsWith(";") || ln.StartsWith("#")) continue;
                    int eq = ln.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = ln.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = ln.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "voice": Voice = v; break;
                        case "pitch": Pitch = i4(v, 0); break;
                        case "rate": Rate = i4(v, 0); break;
                        case "shoton": ShotOn = b1(v); break;
                        case "shotwav": ShotWav = v; break;
                        case "shotvol": ShotVol = i4(v, 70); break;
                        case "cueon": CueOn = b1(v); break;
                        case "cuewav": CueWav = v; break;
                        case "lead": Lead = i4(v, 80); break;
                        case "wallpaper": Wallpaper = v; break;
                        case "dim": Dim = i4(v, 55); break;
                        case "topmost": TopMost = b1(v); break;
                        case "hook": Hook = b1(v); break;
                        case "progressive": Progressive = b1(v); break;
                        case "sound": Sound = b1(v); break;
                    }
                }
            }
            catch { }
            Sanitize();
        }

        public void Save(string path)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; AK-47 压枪提醒器 设置文件（可直接编辑）");
                sb.AppendLine("voice=" + Voice);
                sb.AppendLine("pitch=" + Pitch);
                sb.AppendLine("rate=" + Rate);
                sb.AppendLine("shoton=" + (ShotOn ? 1 : 0));
                sb.AppendLine("shotwav=" + ShotWav);
                sb.AppendLine("shotvol=" + ShotVol);
                sb.AppendLine("cueon=" + (CueOn ? 1 : 0));
                sb.AppendLine("cuewav=" + CueWav);
                sb.AppendLine("lead=" + Lead);
                sb.AppendLine("wallpaper=" + Wallpaper);
                sb.AppendLine("dim=" + Dim);
                sb.AppendLine("topmost=" + (TopMost ? 1 : 0));
                sb.AppendLine("hook=" + (Hook ? 1 : 0));
                sb.AppendLine("progressive=" + (Progressive ? 1 : 0));
                sb.AppendLine("sound=" + (Sound ? 1 : 0));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        public void Sanitize()
        {
            if (Pitch < -60) Pitch = -60;
            if (Pitch > 80) Pitch = 80;
            if (Rate < 0) Rate = 0;
            if (Rate > 6) Rate = 6;
            if (ShotVol < 0) ShotVol = 0;
            if (ShotVol > 100) ShotVol = 100;
            if (Lead < 0) Lead = 0;
            if (Lead > 3000) Lead = 3000;
            if (Dim < 0) Dim = 0;
            if (Dim > 92) Dim = 92;
        }

        private static int i4(string s, int d)
        {
            int r;
            if (int.TryParse(s, out r)) return r;
            return d;
        }
        private static bool b1(string s)
        {
            return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("on", StringComparison.OrdinalIgnoreCase);
        }
    }


    // ---------- 语音：双引擎（进程内 SAPI COM → 失败则改用 PowerShell 语音助手）+ SSML 变声 ----------
    internal sealed class VoiceOut
    {
        private sealed class Req
        {
            public int Rate;
            public int Pitch;
            public string Word;
            public Req(int r, int p, string w) { Rate = r; Pitch = p; Word = w; }
        }

        private readonly Thread th;
        private readonly Queue<Req> q = new Queue<Req>();
        private readonly object gate = new object();
        private volatile bool ready;
        private volatile bool failed;
        private volatile bool stop;

        private Type spType;
        private object sp;
        private volatile Process ps;

        public string VoiceName = "";
        public string LastError = "";
        public string Engine = "";
        public string Banner = "";
        public bool Fallback;
        public string[] Voices = new string[0];
        public string WantVoice = "";

        public bool Ready { get { return ready; } }
        public bool Failed { get { return failed; } }

        private static readonly string[] ZhLangs = new string[] { "804", "404", "c04", "1004", "7804" };

        // 语音助手：RATE / VOICE / X(ssml) 指令；同时把可用语音列表回给主程序
        private const string PsScript = @"Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$names = @()
$zh = $false
$picked = $false
foreach ($v in $s.GetInstalledVoices()) {
  $nm = $v.VoiceInfo.Name
  $names += $nm
  if (-not $picked -and $v.VoiceInfo.Culture.Name -like 'zh*') {
    $zh = $true
    try { $s.SelectVoice($nm) } catch {}
    $picked = $true
  }
}
[Console]::Out.WriteLine('READY ' + $(if ($zh) { 'zh' } else { 'en' }) + '|' + ($names -join '|'))
[Console]::Out.Flush()
while ($true) {
  $l = [Console]::In.ReadLine()
  if ($l -eq $null) { break }
  $l = $l.Trim()
  if ($l.Length -eq 0) { continue }
  if ($l.StartsWith('RATE ')) { try { $s.Rate = [int]$l.Substring(5) } catch {} ; continue }
  if ($l.StartsWith('VOICE ')) {
    try {
      $want = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($l.Substring(6)))
      $s.SelectVoice($want)
    } catch {}
    continue
  }
  if ($l.StartsWith('X ')) {
    try {
      $xml = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($l.Substring(2)))
      $s.SpeakAsyncCancelAll()
      try { $s.SpeakSsmlAsync($xml) }
      catch {
        $m = [regex]::Match($xml, '>([^<>]+)</prosody>')
        if ($m.Success) { $s.SpeakAsync($m.Groups[1].Value) }
      }
    } catch {}
    continue
  }
}
$s.Dispose()";

        public VoiceOut()
        {
            th = new Thread(Run);
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        public void Stop()
        {
            stop = true;
            lock (gate) { q.Clear(); }
            try
            {
                if (ps != null)
                {
                    try { ps.StandardInput.Close(); } catch { }
                    try { if (!ps.HasExited) ps.Kill(); } catch { }
                }
            }
            catch { }
        }

        private static string Ssml(string word, int pitch, bool zh)
        {
            string p = (pitch >= 0 ? "+" : "") + pitch + "%";
            string lang = zh ? "zh-CN" : "en-US";
            return "<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"" + lang + "\">" +
                   "<prosody pitch=\"" + p + "\" rate=\"+0%\">" + word + "</prosody></speak>";
        }

        private static string B64(string s)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        }

        /* ---------- 引擎 1：进程内 SAPI COM ---------- */
        private bool TryCom()
        {
            try
            {
                spType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (spType == null) return false;
                sp = Activator.CreateInstance(spType);

                object r0 = spType.InvokeMember("Rate", BindingFlags.GetProperty, null, sp, new object[0]);
                spType.InvokeMember("Rate", BindingFlags.SetProperty, null, sp, new object[] { Convert.ToInt32(r0) });
                spType.InvokeMember("Volume", BindingFlags.SetProperty, null, sp, new object[] { 100 });

                object toks = spType.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, sp, new object[] { "", "" });
                int n = Convert.ToInt32(toks.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, toks, new object[0]));
                List<string> names = new List<string>();
                object zhToken = null;
                string zhDesc = "";
                object wantToken = null;
                for (int i = 0; i < n; i++)
                {
                    object tok = toks.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, toks, new object[] { i });
                    string desc = "";
                    string lang = "";
                    try { desc = Convert.ToString(tok.GetType().InvokeMember("GetDescription", BindingFlags.InvokeMethod, null, tok, new object[] { 1 })); } catch { }
                    try { lang = Convert.ToString(tok.GetType().InvokeMember("GetAttribute", BindingFlags.InvokeMethod, null, tok, new object[] { "Language" })); } catch { }
                    if (desc != null && desc.Length > 0) names.Add(desc);
                    bool isZh = false;
                    string l = (lang == null) ? "" : lang.Trim().ToLowerInvariant();
                    for (int k = 0; k < ZhLangs.Length; k++) if (l == ZhLangs[k]) isZh = true;
                    if (!isZh && desc != null && (desc.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        desc.IndexOf("Huihui", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        desc.IndexOf("Yaoyao", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        desc.IndexOf("中文", StringComparison.Ordinal) >= 0))
                        isZh = true;
                    if (isZh && zhToken == null) { zhToken = tok; zhDesc = desc; }
                    if (WantVoice != null && WantVoice.Length > 0 && desc != null &&
                        desc.IndexOf(WantVoice, StringComparison.OrdinalIgnoreCase) >= 0)
                        wantToken = tok;
                }
                Voices = names.ToArray();
                object pick = (wantToken != null) ? wantToken : zhToken;
                if (pick != null)
                {
                    spType.InvokeMember("Voice", BindingFlags.SetProperty, null, sp, new object[] { pick });
                    Fallback = false;
                    VoiceName = (wantToken != null) ? WantVoice : zhDesc;
                }
                else
                {
                    Fallback = true;
                    if (names.Count > 0) VoiceName = names[0];
                }
                spType.InvokeMember("Rate", BindingFlags.SetProperty, null, sp, new object[] { 0 });
                Engine = "com";
                return true;
            }
            catch (Exception ex)
            {
                LastError = "COM: " + Innermost(ex).Message;
                sp = null;
                return false;
            }
        }

        /* ---------- 引擎 2：PowerShell 语音助手 ---------- */
        private bool TryPs()
        {
            try
            {
                string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(PsScript));
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                ps = Process.Start(psi);
                if (ps == null) return false;

                try
                {
                    System.Threading.Tasks.Task<string> t = ps.StandardOutput.ReadLineAsync();
                    if (t.Wait(6000)) Banner = t.Result ?? "";
                }
                catch { }
                if (ps.HasExited) { LastError = "PS: 助手退出 " + ps.ExitCode; return false; }

                string[] parts = Banner.Split('|');
                List<string> names = new List<string>();
                for (int i = 1; i < parts.Length; i++) if (parts[i].Trim().Length > 0) names.Add(parts[i].Trim());
                Voices = names.ToArray();

                Fallback = !parts[0].Contains("zh");
                VoiceName = Fallback ? "助理语音(en)" : "助理语音(zh)";
                if (WantVoice != null && WantVoice.Length > 0)
                {
                    ps.StandardInput.WriteLine("VOICE " + B64(WantVoice));
                }
                ps.StandardInput.WriteLine("RATE 0");
                ps.StandardInput.Flush();
                Engine = "ps";
                return true;
            }
            catch (Exception ex)
            {
                LastError = "PS: " + Innermost(ex).Message;
                return false;
            }
        }

        public void SelectVoice(string name)
        {
            WantVoice = name ?? "";
            try
            {
                if (Engine == "com" && sp != null)
                {
                    object toks = spType.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, sp, new object[] { "", "" });
                    int n = Convert.ToInt32(toks.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, toks, new object[0]));
                    for (int i = 0; i < n; i++)
                    {
                        object tok = toks.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, toks, new object[] { i });
                        string desc = "";
                        try { desc = Convert.ToString(tok.GetType().InvokeMember("GetDescription", BindingFlags.InvokeMethod, null, tok, new object[] { 1 })); } catch { }
                        if (desc != null && desc.IndexOf(WantVoice, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            spType.InvokeMember("Voice", BindingFlags.SetProperty, null, sp, new object[] { tok });
                            VoiceName = desc;
                            Fallback = false;
                            return;
                        }
                    }
                }
                else if (Engine == "ps" && ps != null && WantVoice.Length > 0)
                {
                    ps.StandardInput.WriteLine("VOICE " + B64(WantVoice));
                    ps.StandardInput.Flush();
                }
            }
            catch { }
        }

        private static Exception Innermost(Exception ex)
        {
            Exception e = ex;
            while (e.InnerException != null) e = e.InnerException;
            return e;
        }

        private void Run()
        {
            bool ok = TryCom();
            if (!ok) ok = TryPs();
            if (!ok) { failed = true; return; }
            ready = true;

            while (!stop)
            {
                Req r = null;
                lock (gate) { if (q.Count > 0) r = q.Dequeue(); }
                if (r == null) { Thread.Sleep(12); continue; }
                try
                {
                    if (Engine == "com")
                    {
                        spType.InvokeMember("Rate", BindingFlags.SetProperty, null, sp, new object[] { r.Rate });
                        try
                        {
                            spType.InvokeMember("Speak", BindingFlags.InvokeMethod, null, sp,
                                new object[] { Ssml(r.Word, r.Pitch, !Fallback), 11 });   // 1|2|8 = 异步+打断+XML
                        }
                        catch
                        {
                            spType.InvokeMember("Speak", BindingFlags.InvokeMethod, null, sp, new object[] { r.Word, 3 });
                        }
                    }
                    else if (ps != null && !ps.HasExited)
                    {
                        ps.StandardInput.WriteLine("RATE " + r.Rate);
                        ps.StandardInput.WriteLine("X " + B64(Ssml(r.Word, r.Pitch, !Fallback)));
                        ps.StandardInput.Flush();
                    }
                }
                catch (Exception ex) { LastError = "Speak: " + Innermost(ex).Message; }
            }
        }

        public void Say(string zhWord, int rateLevel, int pitch)
        {
            if (!ready || failed) return;
            string txt = zhWord;
            if (Fallback)
            {
                if (zhWord == "往下") txt = "down";
                else if (zhWord == "右下") txt = "right down";
                else if (zhWord == "左下") txt = "left down";
            }
            lock (gate)
            {
                q.Clear();
                q.Enqueue(new Req(rateLevel, pitch, txt));
            }
        }

        public bool SayNow(string text, int rateLevel, int waitMs)
        {
            int waited = 0;
            while (!ready && !failed && waited < 9000) { Thread.Sleep(50); waited += 50; }
            if (!ready || failed) return false;
            try
            {
                if (Engine == "com")
                {
                    spType.InvokeMember("Rate", BindingFlags.SetProperty, null, sp, new object[] { rateLevel });
                    spType.InvokeMember("Speak", BindingFlags.InvokeMethod, null, sp, new object[] { text, 0 });
                }
                else
                {
                    ps.StandardInput.WriteLine("RATE " + rateLevel);
                    ps.StandardInput.WriteLine("X " + B64(Ssml(text, 0, !Fallback)));
                    ps.StandardInput.Flush();
                }
                Thread.Sleep(waitMs);
                return true;
            }
            catch (Exception ex) { LastError = "SayNow: " + Innermost(ex).Message; return false; }
        }
    }

    // ---------- 设置窗口 ----------
    internal sealed class SettingsForm : Form
    {
        private readonly VoiceOut voice;
        private readonly Cfg working;

        private ComboBox cbVoice;
        private TrackBar tbPitch, tbRate, tbVol, tbDim;
        private Label lbPitch, lbRate, lbVol, lbDim, lbCue, lbWall, lbShot;
        private Button btnShotPick, btnShotClear, btnShotTest;
        private CheckBox ckShot, ckCue;
        private NumericUpDown numLead;
        private Button btnCuePick, btnCueClear, btnWallPick, btnWallClear;

        public Cfg Result { get { return working; } }

        public SettingsForm(VoiceOut v, Cfg cur)
        {
            voice = v;
            working = cur.Clone();

            Text = "设置 · AK-47 压枪提醒器";
            ClientSize = new Size(648, 642);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = C(18, 22, 28);
            ForeColor = C(232, 238, 246);
            Font = new Font("Microsoft YaHei UI", 9f);
            ShowInTaskbar = false;

            int x = 24, w = 592;
            int y = 12;

            Head("语音", x, ref y, w);
            RowLabel("语音音色", x, y);
            cbVoice = new ComboBox();
            cbVoice.DropDownStyle = ComboBoxStyle.DropDownList;
            cbVoice.SetBounds(150, y - 3, 340, 26);
            StyleInput(cbVoice);
            string[] vs = voice.Voices;
            cbVoice.Items.Add("自动（优先中文）");
            for (int i = 0; i < vs.Length; i++) cbVoice.Items.Add(vs[i]);
            int sel = 0;
            if (working.Voice.Length > 0)
            {
                for (int i = 0; i < vs.Length; i++) if (vs[i] == working.Voice) sel = i + 1;
            }
            cbVoice.SelectedIndex = sel;
            Controls.Add(cbVoice);

            Button btnTry = new Button();
            btnTry.Text = "试听";
            btnTry.SetBounds(500, y - 4, 90, 28);
            StyleBtn(btnTry);
            btnTry.Click += delegate { TrySpeak(); };
            Controls.Add(btnTry);
            y += 36;

            Label hint = new Label();
            hint.Text = "音色不用预设：直接拖下面的「音调 / 语速」，或用上面换一个系统语音";
            hint.SetBounds(x, y - 2, w, 22);
            hint.ForeColor = C(140, 152, 166);
            Controls.Add(hint);
            y += 28;

            RowLabel("音调偏移", x, y);
            tbPitch = Slider(150, y - 2, 380, -50, 60, working.Pitch);
            tbPitch.ValueChanged += delegate { UpdateLabels(); TrySpeak(); };
            Controls.Add(tbPitch);
            lbPitch = ValueLabel(540, y);
            y += 36;

            RowLabel("基础语速", x, y);
            tbRate = Slider(150, y - 2, 380, 0, 6, working.Rate);
            tbRate.ValueChanged += delegate { UpdateLabels(); TrySpeak(); };
            Controls.Add(tbRate);
            lbRate = ValueLabel(540, y);
            y += 42;

            Head("音效", x, ref y, w);
            ckShot = new CheckBox();
            ckShot.Text = "射击音效（每发一声）";
            ckShot.SetBounds(x, y, 300, 24);
            ckShot.ForeColor = C(232, 238, 246);
            ckShot.Checked = working.ShotOn;
            ckShot.CheckedChanged += delegate { working.ShotOn = ckShot.Checked; };
            Controls.Add(ckShot);
            y += 32;

            RowLabel("音效文件", x, y);
            btnShotPick = new Button();
            btnShotPick.SetBounds(150, y - 4, 120, 28);
            btnShotPick.Text = "选择 WAV…";
            StyleBtn(btnShotPick);
            btnShotPick.Click += delegate
            {
                OpenFileDialog d = new OpenFileDialog();
                d.Filter = "音频文件|*.wav;*.mp3;*.ogg|所有文件|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) { working.ShotWav = d.FileName; UpdateLabels(); PreviewShot(); }
            };
            Controls.Add(btnShotPick);

            btnShotClear = new Button();
            btnShotClear.SetBounds(278, y - 4, 80, 28);
            btnShotClear.Text = "用内置";
            StyleBtn(btnShotClear);
            btnShotClear.Click += delegate { working.ShotWav = ""; UpdateLabels(); PreviewShot(); };
            Controls.Add(btnShotClear);

            btnShotTest = new Button();
            btnShotTest.SetBounds(366, y - 4, 60, 28);
            btnShotTest.Text = "试听";
            StyleBtn(btnShotTest);
            btnShotTest.Click += delegate { PreviewShot(); };
            Controls.Add(btnShotTest);

            lbShot = new Label();
            lbShot.SetBounds(436, y, 172, 22);
            lbShot.ForeColor = C(140, 152, 166);
            Controls.Add(lbShot);
            y += 40;

            RowLabel("音量", x, y);
            tbVol = Slider(150, y - 2, 380, 0, 100, working.ShotVol);
            tbVol.ValueChanged += delegate { UpdateLabels(); };
            Controls.Add(tbVol);
            lbVol = ValueLabel(540, y);
            y += 40;

            ckCue = new CheckBox();
            ckCue.Text = "换段提示音";
            ckCue.SetBounds(x, y, 200, 24);
            ckCue.ForeColor = C(232, 238, 246);
            ckCue.Checked = working.CueOn;
            ckCue.CheckedChanged += delegate { working.CueOn = ckCue.Checked; };
            Controls.Add(ckCue);
            y += 30;

            RowLabel("自定义提示音", x, y);
            btnCuePick = new Button();
            btnCuePick.SetBounds(150, y - 4, 110, 28);
            btnCuePick.Text = "选择 WAV…";
            StyleBtn(btnCuePick);
            btnCuePick.Click += delegate
            {
                OpenFileDialog d = new OpenFileDialog();
                d.Filter = "WAV 音频|*.wav";
                if (d.ShowDialog(this) == DialogResult.OK) { working.CueWav = d.FileName; UpdateLabels(); }
            };
            Controls.Add(btnCuePick);

            btnCueClear = new Button();
            btnCueClear.SetBounds(268, y - 4, 80, 28);
            btnCueClear.Text = "清除";
            StyleBtn(btnCueClear);
            btnCueClear.Click += delegate { working.CueWav = ""; UpdateLabels(); };
            Controls.Add(btnCueClear);

            lbCue = new Label();
            lbCue.SetBounds(356, y, 250, 22);
            lbCue.ForeColor = C(140, 152, 166);
            Controls.Add(lbCue);
            y += 40;

            Head("节奏", x, ref y, w);
            RowLabel("提前量 (ms)", x, y);
            numLead = new NumericUpDown();
            numLead.SetBounds(150, y - 3, 110, 26);
            numLead.Minimum = 0;
            numLead.Maximum = 3000;
            numLead.Increment = 10;
            numLead.Value = Math.Min(3000, Math.Max(0, working.Lead));
            numLead.BackColor = C(30, 36, 44);
            numLead.ForeColor = C(232, 238, 246);
            numLead.BorderStyle = BorderStyle.FixedSingle;
            numLead.ValueChanged += delegate { working.Lead = (int)numLead.Value; };
            Controls.Add(numLead);
            Label h2 = new Label();
            h2.Text = "0 ~ 3000，不设上限档位，想提前多久就提前多久";
            h2.SetBounds(270, y, 340, 22);
            h2.ForeColor = C(140, 152, 166);
            Controls.Add(h2);
            y += 42;

            Head("外观", x, ref y, w);
            RowLabel("壁纸", x, y);
            btnWallPick = new Button();
            btnWallPick.SetBounds(150, y - 4, 120, 28);
            btnWallPick.Text = "选择图片…";
            StyleBtn(btnWallPick);
            btnWallPick.Click += delegate
            {
                OpenFileDialog d = new OpenFileDialog();
                d.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                if (d.ShowDialog(this) == DialogResult.OK) { working.Wallpaper = d.FileName; UpdateLabels(); }
            };
            Controls.Add(btnWallPick);

            btnWallClear = new Button();
            btnWallClear.SetBounds(278, y - 4, 80, 28);
            btnWallClear.Text = "清除";
            StyleBtn(btnWallClear);
            btnWallClear.Click += delegate { working.Wallpaper = ""; UpdateLabels(); };
            Controls.Add(btnWallClear);

            lbWall = new Label();
            lbWall.SetBounds(366, y, 240, 22);
            lbWall.ForeColor = C(140, 152, 166);
            Controls.Add(lbWall);
            y += 38;

            RowLabel("暗化", x, y);
            tbDim = Slider(150, y - 2, 380, 0, 92, working.Dim);
            tbDim.ValueChanged += delegate { UpdateLabels(); };
            Controls.Add(tbDim);
            lbDim = ValueLabel(540, y);
            y += 44;

            Button btnDef = new Button();
            btnDef.Text = "恢复默认";
            btnDef.SetBounds(x, y, 110, 32);
            StyleBtn(btnDef);
            btnDef.Click += delegate
            {
                working.Reset();
                UpdateLabels();
                cbVoice.SelectedIndex = 0;
                tbPitch.Value = working.Pitch;
                tbRate.Value = working.Rate;
                tbVol.Value = working.ShotVol;
                tbDim.Value = working.Dim;
                numLead.Value = working.Lead;
                ckShot.Checked = working.ShotOn;
                ckCue.Checked = working.CueOn;
                UpdateLabels();
            };
            Controls.Add(btnDef);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.SetBounds(430, y, 90, 32);
            StyleBtn(btnCancel);
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            Button btnOk = new Button();
            btnOk.Text = "保存";
            btnOk.SetBounds(530, y, 90, 32);
            StyleBtn(btnOk);
            btnOk.BackColor = C(245, 166, 35);
            btnOk.ForeColor = Color.FromArgb(32, 20, 0);
            btnOk.FlatAppearance.BorderColor = Color.FromArgb(245, 166, 35);
            btnOk.Click += delegate
            {
                working.Voice = (cbVoice.SelectedIndex <= 0) ? "" : (string)cbVoice.Items[cbVoice.SelectedIndex];
                working.ShotOn = ckShot.Checked;
                working.CueOn = ckCue.Checked;
                working.ShotVol = tbVol.Value;
                working.Lead = (int)numLead.Value;
                working.Dim = tbDim.Value;
                working.Pitch = tbPitch.Value;
                working.Rate = tbRate.Value;
                working.Sanitize();
                DialogResult = DialogResult.OK;
            };
            Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            UpdateLabels();
        }

        private void PreviewShot()
        {
            try
            {
                SoundPlayer sp = MainForm.BuildShot(working.ShotWav, tbVol.Value);
                if (sp != null) sp.Play();
            }
            catch { }
        }

        private void TrySpeak()
        {
            if (voice == null) return;
            int pitch = ClampInt(tbPitch.Value, -60, 80);
            int rate = ClampInt(tbRate.Value, 0, 10);
            string w = voice.Fallback ? "left down" : "左下";
            voice.Say(w, rate, pitch);
        }

        private static int ClampInt(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }

        private void UpdateLabels()
        {
            lbPitch.Text = (tbPitch.Value > 0 ? "+" : "") + tbPitch.Value + "%";
            lbRate.Text = tbRate.Value.ToString();
            lbVol.Text = tbVol.Value + "%";
            lbDim.Text = tbDim.Value + "%";
            lbCue.Text = working.CueWav.Length > 0 ? Path.GetFileName(working.CueWav) : "默认双音";
            lbShot.Text = (working.ShotWav != null && working.ShotWav.Length > 0)
                ? Path.GetFileName(working.ShotWav) : "内置合成 AK";
            lbWall.Text = working.Wallpaper.Length > 0 ? Path.GetFileName(working.Wallpaper) : "无（纯色）";
        }

        private static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        private void Head(string text, int x, ref int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, 20);
            l.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            l.ForeColor = Color.FromArgb(245, 166, 35);
            Controls.Add(l);
            y += 26;
        }

        private Label RowLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, 126, 22);
            l.ForeColor = Color.FromArgb(200, 210, 222);
            Controls.Add(l);
            return l;
        }

        private Label ValueLabel(int x, int y)
        {
            Label l = new Label();
            l.SetBounds(x, y, 90, 22);
            l.ForeColor = Color.FromArgb(232, 238, 246);
            l.Font = new Font("Consolas", 9f, FontStyle.Bold);
            Controls.Add(l);
            return l;
        }

        private TrackBar Slider(int x, int y, int w, int min, int max, int val)
        {
            TrackBar t = new TrackBar();
            t.SetBounds(x, y, w, 32);
            t.Minimum = min;
            t.Maximum = max;
            t.TickStyle = TickStyle.None;
            t.SmallChange = 1;
            t.LargeChange = Math.Max(1, (max - min) / 10);
            t.Value = ClampInt(val, min, max);
            t.BackColor = Color.FromArgb(18, 22, 28);
            return t;
        }

        private void StyleInput(Control c)
        {
            c.BackColor = Color.FromArgb(30, 36, 44);
            c.ForeColor = Color.FromArgb(232, 238, 246);
        }

        private void StyleBtn(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(36, 43, 53);
            b.ForeColor = Color.FromArgb(232, 238, 246);
            b.FlatAppearance.BorderColor = Color.FromArgb(60, 70, 82);
            b.Cursor = Cursors.Hand;
        }
    }

    // ---------- 主窗口 ----------
    internal sealed class MainForm : Form
    {
        // 状态
        private bool spraying;
        private readonly Stopwatch sw = new Stopwatch();
        private readonly System.Windows.Forms.Timer timer;
        private double mockT = -1;

        private int rounds;
        private int lastShots;
        private double lastDuration;
        private int speedLevel;          // 0..6
        private double lead = 0.08;
        private bool hookOn = true;
        private bool soundOn = true;
        private bool progressive = true;
        private int displaySeg;

        private int lastTick = -1;
        private int lastSeg = -1;

        private VoiceOut voice;
        private SoundPlayer tickSound, segSound;
        private SoundPlayer shotSound;
        private SoundPlayer cueCustom;

        private Cfg cfg = new Cfg();
        private string cfgPath = "";
        private Image wallpaper;
        private static int cardAlpha = 255;

        private ContextMenuStrip menu;

        // 布局
        private Rectangle headerRect, leftRect, rightRect, tableRect, statusRect;
        private Rectangle cueCard, arrowBox, timelineBar, ticksBox;

        // 拖动窗口
        private bool dragging;
        private Point dragOffset;

        public MainForm()
        {
            Text = "AK-47 压枪分段提醒器";
            ClientSize = new Size(1060, 720);
            MinimumSize = new Size(920, 660);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Th.Bg;
            ForeColor = Th.Text;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            TopMost = true;
            AllowDrop = false;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 10;
            timer.Tick += OnTimerTick;

            cfgPath = SettingsPath();
            cfg.Load(cfgPath);
            lead = cfg.Lead / 1000.0;
            progressive = cfg.Progressive;
            soundOn = cfg.Sound;

            BuildMenu();
            voice = new VoiceOut();
            InitSound();

            // 全局鼠标监听
            hookProc = new LowLevelMouseProc(HookCallback);
            hookHandle = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle(null), 0);
            if (hookHandle == IntPtr.Zero) hookOn = false;
            else hookOn = cfg.Hook;
            TopMost = cfg.TopMost;

            ApplyLook();
        }

        public static string SettingsPath()
        {
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
            try
            {
                if (File.Exists(p)) return p;
                File.AppendAllText(p, "", Encoding.UTF8);
                return p;
            }
            catch
            {
                try
                {
                    string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AK47SprayCoach");
                    Directory.CreateDirectory(d);
                    return Path.Combine(d, "settings.ini");
                }
                catch { return p; }
            }
        }

        public bool HookInstalled { get { return hookHandle != IntPtr.Zero; } }

        // ======================= 计时 =======================
        private double Now()
        {
            if (mockT >= 0) return mockT;
            return sw.Elapsed.TotalSeconds;
        }

        private void StartSpray()
        {
            if (spraying) return;
            spraying = true;
            sw.Reset(); sw.Start();
            lastTick = -1; lastSeg = -1;
            timer.Start();
            Invalidate();
        }

        private void StopSpray(bool completed)
        {
            if (!spraying) return;
            double t = Math.Min(Now(), Engine.Total);
            spraying = false;
            timer.Stop();
            sw.Stop();

            int shots = (t >= Engine.Total - 1e-9) ? Engine.N : Engine.Clamp((int)Math.Floor(t / Engine.DT) + 1, 1, Engine.N);
            lastShots = shots;
            lastDuration = t;
            rounds++;
            if (progressive && shots >= 2 && speedLevel < 6) speedLevel++;
            Invalidate();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (!spraying) { timer.Stop(); return; }
            double t = Now();
            UpdateState(t);
            Invalidate();
            if (t >= Engine.Total)
            {
                UpdateState(Engine.Total);
                StopSpray(true);
                Invalidate();
            }
        }

        private void UpdateState(double t)
        {
            double cueT = Math.Min(t + lead, Engine.Total);
            int cueShot = Engine.ShotAt(cueT);
            int seg = Engine.SegOf(cueShot + 1);
            displaySeg = seg;

            int tickIdx = (int)Math.Floor(t / Engine.DT + 1e-9);
            if (tickIdx != lastTick && tickIdx < Engine.N)
            {
                lastTick = tickIdx;
                PlayShot();
            }
            if (seg != lastSeg)
            {
                lastSeg = seg;
                if (soundOn && cfg.CueOn)
                {
                    try
                    {
                        if (cueCustom != null) cueCustom.Play();
                        else if (segSound != null) segSound.Play();
                    }
                    catch { }
                }
                Speak(Engine.Word(seg));
            }
        }

        // ======================= 声音 / 语音 =======================
        private void InitSound()
        {
            BuildSounds();
        }

        // 重新生成/加载所有音效（设置变更后调用）
        private void BuildSounds()
        {
            try
            {
                tickSound = new SoundPlayer(MakeWav(new double[][] { new double[] { 1180, 34 } }, 0.10));
                tickSound.Load();
                segSound = new SoundPlayer(MakeWav(new double[][] { new double[] { 720, 70 }, new double[] { 1080, 95 } }, 0.16));
                segSound.Load();
            }
            catch { tickSound = null; segSound = null; }

            try
            {
                shotSound = BuildShot(cfg.ShotWav, cfg.ShotVol);
            }
            catch { shotSound = null; }

            try
            {
                if (cfg.CueWav != null && cfg.CueWav.Length > 0 && File.Exists(cfg.CueWav))
                {
                    cueCustom = new SoundPlayer(cfg.CueWav);
                    cueCustom.Load();
                }
                else cueCustom = null;
            }
            catch { cueCustom = null; }
        }

        // 自定义 wav 优先，没有就用内置合成
        public static SoundPlayer BuildShot(string wav, int vol)
        {
            try
            {
                if (wav != null && wav.Length > 0 && File.Exists(wav))
                {
                    SoundPlayer sp = new SoundPlayer(wav);
                    sp.Load();
                    return sp;
                }
            }
            catch { }
            try
            {
                double v = Math.Max(0.05, Math.Min(1.0, vol / 100.0));
                SoundPlayer s2 = new SoundPlayer(MakeGunshot(0, v));
                s2.Load();
                return s2;
            }
            catch { return null; }
        }

        // 程序化合成枪声：噪声爆 + 低频撞击 + 起音咔哒
        public static Stream MakeGunshot(int style, double vol)
        {
            const int rate = 44100;
            Random rnd = new Random(1337 + style * 77);
            double dur = style == 1 ? 0.20 : (style == 2 ? 0.10 : 0.15);
            int n = (int)(rate * dur);
            double noiseTau = style == 2 ? 0.020 : (style == 1 ? 0.055 : 0.036);
            double thumpF = style == 1 ? 70 : (style == 2 ? 140 : 100);
            double thumpTau = style == 1 ? 0.075 : 0.045;
            double bodyF = style == 1 ? 320 : (style == 2 ? 900 : 560);
            double bodyTau = style == 2 ? 0.012 : 0.022;
            double lp = 0;
            List<short> data = new List<short>(n);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double noise = rnd.NextDouble() * 2 - 1;
                lp += (noise - lp) * (style == 1 ? 0.20 : 0.55);     // 简易低通，改变噪声明亮度
                double envNoise = Math.Exp(-t / noiseTau);
                double envThump = Math.Exp(-t / thumpTau);
                double envBody = Math.Exp(-t / bodyTau);
                double click = t < 0.0022 ? (rnd.NextDouble() * 2 - 1) * (1 - t / 0.0022) * 0.9 : 0;
                double v = lp * envNoise * 0.85
                         + Math.Sin(2 * Math.PI * thumpF * t) * envThump * 0.75
                         + Math.Sin(2 * Math.PI * bodyF * t) * envBody * 0.35
                         + click;
                v = Math.Tanh(v * 1.6) * vol;
                if (v > 1) v = 1; if (v < -1) v = -1;
                data.Add((short)(v * 32000));
            }
            return WavFrom(data, rate);
        }

        private static Stream WavFrom(List<short> data, int rate)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(ms);
            int dataLen = data.Count * 2;
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataLen);
            bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataLen);
            for (int i = 0; i < data.Count; i++) bw.Write(data[i]);
            bw.Flush();
            ms.Position = 0;
            return ms;
        }

        public static Stream MakeWav(double[][] parts, double amp)
        {
            const int rate = 44100;
            MemoryStream ms = new MemoryStream();
            List<short> data = new List<short>();
            for (int p = 0; p < parts.Length; p++)
            {
                int n = (int)(rate * parts[p][1] / 1000.0);
                for (int i = 0; i < n; i++)
                {
                    double tt = (double)i / rate;
                    double atk = Math.Min(1.0, i / (0.004 * rate));
                    double rel = Math.Min(1.0, (n - i) / (0.012 * rate));
                    double v = Math.Sin(2 * Math.PI * parts[p][0] * tt) * atk * rel * amp;
                    data.Add((short)(v * 32000));
                }
            }
            BinaryWriter bw = new BinaryWriter(ms);
            int dataLen = data.Count * 2;
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataLen);
            bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(rate); bw.Write(rate * 2); bw.Write((short)2); bw.Write((short)16);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataLen);
            for (int i = 0; i < data.Count; i++) bw.Write(data[i]);
            bw.Flush();
            ms.Position = 0;
            return ms;
        }

        private void Speak(string word)
        {
            if (!soundOn || voice == null || !spraying) return;
            voice.Say(word, VoiceRate(), VoicePitch());
        }

        private int VoiceRate()
        {
            return Engine.Clamp(speedLevel + cfg.Rate, -10, 10);
        }

        private int VoicePitch()
        {
            return Engine.Clamp(cfg.Pitch, -60, 80);
        }

        private void PlayShot()
        {
            if (!soundOn || !cfg.ShotOn) return;
            try
            {
                if (shotSound != null) { shotSound.Play(); return; }
                if (tickSound != null) tickSound.Play();
            }
            catch { }
        }

        // ======================= 全局鼠标钩子 =======================
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public int x;
            public int y;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private LowLevelMouseProc hookProc;
        private IntPtr hookHandle = IntPtr.Zero;
        private bool hookOwnsSpray;
        private bool hookMuted;

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && hookOn && !hookMuted && (menu == null || !menu.Visible))
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP)
                    {
                        MSLLHOOKSTRUCT info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                        Point pt = new Point(info.x, info.y);
                        bool inside = RectangleToScreen(ClientRectangle).Contains(pt);
                        if (!inside)
                        {
                            if (msg == WM_LBUTTONDOWN)
                            {
                                if (!spraying) { hookOwnsSpray = true; StartSpray(); }
                            }
                            else
                            {
                                if (hookOwnsSpray || spraying) { hookOwnsSpray = false; StopSpray(false); }
                            }
                        }
                    }
                }
            }
            catch { }
            return CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        // ======================= 菜单 =======================
        private void BuildMenu()
        {
            if (menu != null) { menu.Dispose(); menu = null; }
            menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            ToolStripMenuItem miSet = new ToolStripMenuItem("设置…    F2   （音效 / 音色 / 提前量 / 壁纸）");
            miSet.Font = new Font(menu.Font, FontStyle.Bold);
            miSet.Click += delegate { OpenSettings(); };
            menu.Items.Add(miSet);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miProg = new ToolStripMenuItem("语速递进：每按住一次 +1");
            miProg.CheckOnClick = true;
            miProg.Checked = cfg.Progressive;
            miProg.CheckedChanged += delegate
            {
                progressive = miProg.Checked;
                cfg.Progressive = miProg.Checked;
                cfg.Save(cfgPath);
                Invalidate();
            };
            menu.Items.Add(miProg);

            ToolStripMenuItem miHook = new ToolStripMenuItem("全局监听（游戏内也生效）");
            miHook.CheckOnClick = true;
            miHook.Checked = hookOn;
            miHook.CheckedChanged += delegate
            {
                hookOn = miHook.Checked;
                cfg.Hook = miHook.Checked;
                cfg.Save(cfgPath);
                Invalidate();
            };
            menu.Items.Add(miHook);

            ToolStripMenuItem miSound = new ToolStripMenuItem("音效 + 语音");
            miSound.CheckOnClick = true;
            miSound.Checked = soundOn;
            miSound.CheckedChanged += delegate
            {
                soundOn = miSound.Checked;
                cfg.Sound = miSound.Checked;
                cfg.Save(cfgPath);
                if (!soundOn && voice != null) voice.Stop();
                Invalidate();
            };
            menu.Items.Add(miSound);

            ToolStripMenuItem miTop = new ToolStripMenuItem("窗口置顶");
            miTop.CheckOnClick = true;
            miTop.Checked = TopMost;
            miTop.CheckedChanged += delegate
            {
                TopMost = miTop.Checked;
                cfg.TopMost = miTop.Checked;
                cfg.Save(cfgPath);
            };
            menu.Items.Add(miTop);

            ToolStripMenuItem miLead = new ToolStripMenuItem("提前量（提示比子弹早）");
            int[] leads = new int[] { 0, 50, 80, 120, 160 };
            for (int i = 0; i < leads.Length; i++)
            {
                int ms = leads[i];
                ToolStripMenuItem it = new ToolStripMenuItem(ms + " ms");
                it.Checked = (Math.Abs(cfg.Lead - ms) < 1);
                it.Click += delegate
                {
                    cfg.Lead = ms;
                    lead = ms / 1000.0;
                    cfg.Save(cfgPath);
                    foreach (ToolStripItem x in miLead.DropDownItems) ((ToolStripMenuItem)x).Checked = false;
                    it.Checked = true;
                    Invalidate();
                };
                miLead.DropDownItems.Add(it);
            }
            ToolStripMenuItem itMore = new ToolStripMenuItem("自定义（不设上限）…");
            itMore.Click += delegate { OpenSettings(); };
            miLead.DropDownItems.Add(new ToolStripSeparator());
            miLead.DropDownItems.Add(itMore);
            menu.Items.Add(miLead);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem miRst = new ToolStripMenuItem("重置语速 / 次数");
            miRst.Click += delegate
            {
                speedLevel = 0; rounds = 0; lastShots = 0; lastDuration = 0;
                Invalidate();
            };
            menu.Items.Add(miRst);

            ToolStripMenuItem miHelp = new ToolStripMenuItem("使用说明");
            miHelp.Click += delegate
            {
                MessageBox.Show(this,
                    "1) 按住鼠标左键 = 开始扫射；松开 = 停止。\n" +
                    "2) 开着「全局监听」时，在游戏里按住开火键，本窗口会同步提示（只监听，不注入、不读内存）。\n" +
                    "3) 每按住一次，语音语速 +1（最多 +6），越快越考验反应。\n" +
                    "4) F2 / 右键「设置」：换语音音色、射击音效、自定义提前量（0~3000ms）、换壁纸。\n" +
                    "5) 拖动标题栏 = 移动窗口；Esc = 停止。",
                    "AK-47 压枪分段提醒器", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            menu.Items.Add(miHelp);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { Close(); };
            menu.Items.Add(miExit);
        }

        // ======================= 设置应用 =======================
        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(voice, cfg))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                cfg.CopyFrom(f.Result);
            }
            cfg.Save(cfgPath);
            lead = cfg.Lead / 1000.0;
            progressive = cfg.Progressive;
            soundOn = cfg.Sound;
            hookOn = cfg.Hook;
            TopMost = cfg.TopMost;
            ApplyLook();
            BuildMenu();
            Invalidate();
        }

        private void ApplyLook()
        {
            if (voice != null)
            {
                voice.WantVoice = cfg.Voice;
                if (voice.Ready) voice.SelectVoice(cfg.Voice);
            }
            BuildSounds();
            LoadWallpaper();
        }

        private void LoadWallpaper()
        {
            if (wallpaper != null) { try { wallpaper.Dispose(); } catch { } wallpaper = null; }
            try
            {
                if (cfg.Wallpaper != null && cfg.Wallpaper.Length > 0 && File.Exists(cfg.Wallpaper))
                {
                    using (Image img = Image.FromFile(cfg.Wallpaper))
                    {
                        wallpaper = new Bitmap(img);
                    }
                }
            }
            catch { wallpaper = null; }
            cardAlpha = (wallpaper != null) ? 236 : 255;
        }

        public void SetWallpaperForTest(string path)
        {
            cfg.Wallpaper = path;
            LoadWallpaper();
            Invalidate();
        }

        // 供 --sounds 自检：把合成出来的音效写到文件
        public string DumpSounds(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Cfg save = cfg.Clone();
                StringBuilder sb = new StringBuilder();
                for (int s = 0; s < 3; s++)
                {
                    cfg.ShotVol = 80;
                    Stream st = MakeGunshot(0, 0.8);
                    string f = Path.Combine(dir, "shot" + s + ".wav");
                    using (FileStream fs = File.Create(f)) { st.CopyTo(fs); }
                    sb.AppendLine("shot" + s + ".wav " + new FileInfo(f).Length + " bytes");
                }
                Stream tk = MakeWav(new double[][] { new double[] { 1180, 34 } }, 0.10);
                string tf = Path.Combine(dir, "tick.wav");
                using (FileStream fs = File.Create(tf)) { tk.CopyTo(fs); }
                sb.AppendLine("tick.wav " + new FileInfo(tf).Length + " bytes");
                Stream sg = MakeWav(new double[][] { new double[] { 720, 70 }, new double[] { 1080, 95 } }, 0.16);
                string sf = Path.Combine(dir, "cue.wav");
                using (FileStream fs = File.Create(sf)) { sg.CopyTo(fs); }
                sb.AppendLine("cue.wav " + new FileInfo(sf).Length + " bytes");
                cfg.CopyFrom(save);
                return sb.ToString();
            }
            catch (Exception ex) { return "ERR " + ex.Message; }
        }

        // ======================= 交互 =======================
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            if (headerRect.Contains(new Point(e.X, e.Y)))
            {
                dragging = true;
                dragOffset = new Point(e.X, e.Y);
                return;
            }
            StartSpray();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                Point p = PointToScreen(new Point(e.X, e.Y));
                Location = new Point(p.X - dragOffset.X, p.Y - dragOffset.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                if (dragging) dragging = false;
                else StopSpray(false);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) StopSpray(false);
            else if (e.KeyCode == Keys.Space && !spraying) StartSpray();   // 备用：按住空格也能练
            else if (e.KeyCode == Keys.F2) OpenSettings();
            else if (e.KeyCode == Keys.F1) { menu.Show(this, new Point(20, headerRect.Bottom)); }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) StopSpray(false);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (voice != null) voice.Stop();
            if (hookHandle != IntPtr.Zero) { try { UnhookWindowsHookEx(hookHandle); } catch { } }
            base.OnFormClosing(e);
        }

        // ======================= 布局 =======================
        private void DoLayout()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            int pad = 14;
            headerRect = new Rectangle(pad, 8, W - pad * 2, 44);

            int statusH = 26;
            int tableH = 34 + Engine.Segs.Length * 26;
            int bodyTop = headerRect.Bottom + 8;
            int bodyH = H - statusH - tableH - 14 - bodyTop;
            if (bodyH < 210) bodyH = 210;
            Rectangle body = new Rectangle(pad, bodyTop, W - pad * 2, bodyH);

            int leftW = (int)(body.Width * 0.455);
            leftRect = new Rectangle(body.X, body.Y, leftW, body.Height);
            rightRect = new Rectangle(leftRect.Right + 12, body.Y, body.Width - leftW - 12, body.Height);

            int ticksH = 66;
            cueCard = new Rectangle(leftRect.X, leftRect.Y, leftRect.Width, leftRect.Height - ticksH - 10);
            if (cueCard.Height < 150) cueCard.Height = 150;
            timelineBar = new Rectangle(cueCard.X + 14, cueCard.Bottom - 48, cueCard.Width - 28, 30);
            int gTop = cueCard.Y + 14;
            int gBottom = timelineBar.Y - 34;
            int gMid = (gTop + gBottom) / 2;
            int ah = Math.Min(200, Math.Max(120, gBottom - gTop));
            arrowBox = new Rectangle(cueCard.X + 6, gMid - ah / 2, 158, ah);
            ticksBox = new Rectangle(leftRect.X, cueCard.Bottom + 10, leftRect.Width, ticksH);
            tableRect = new Rectangle(pad, body.Bottom + 12, W - pad * 2, tableH);
            statusRect = new Rectangle(pad, H - statusH + 4, W - pad * 2, statusH - 6);
        }

        // ======================= 绘制 =======================
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Th.Bg);
            PaintWallpaper(g);
            DoLayout();

            PaintHeader(g);
            PaintCue(g);
            PaintTimeline(g);
            PaintTicks(g);
            PaintPattern(g);
            PaintTable(g);
            PaintStatus(g);
        }

        private static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static void Card(Graphics g, Rectangle r)
        {
            using (GraphicsPath p = Round(r, 12))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(cardAlpha, Th.Card))) g.FillPath(b, p);
                using (Pen pen = new Pen(Th.Line)) g.DrawPath(pen, p);
            }
        }

        // 自定义壁纸：等比铺满 + 暗化
        private void PaintWallpaper(Graphics g)
        {
            if (wallpaper == null) return;
            try
            {
                int W = ClientSize.Width, H = ClientSize.Height;
                double s = Math.Max((double)W / wallpaper.Width, (double)H / wallpaper.Height);
                int w = (int)Math.Ceiling(wallpaper.Width * s);
                int h = (int)Math.Ceiling(wallpaper.Height * s);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(wallpaper, (W - w) / 2, (H - h) / 2, w, h);
                int a = (int)Math.Round(cfg.Dim / 100.0 * 255.0);
                if (a > 0)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 0, 0, 0)))
                        g.FillRectangle(b, 0, 0, W, H);
                }
                // 顶部/底部压暗一点，保证标题与状态栏文字可读
                using (LinearGradientBrush tb = new LinearGradientBrush(new Rectangle(0, 0, 1, 96),
                    Color.FromArgb(150, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(tb, 0, 0, W, 96);
                using (LinearGradientBrush bb = new LinearGradientBrush(new Rectangle(0, 0, 1, 64),
                    Color.FromArgb(0, 0, 0, 0), Color.FromArgb(170, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(bb, 0, H - 64, W, 64);
            }
            catch { }
        }

        private static void FillRound(Graphics g, Rectangle r, int rad, Color c)
        {
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        private static void Txt(Graphics g, string s, Font f, Color c, Rectangle r, StringAlignment va, StringAlignment ha)
        {
            using (StringFormat sf = new StringFormat())
            {
                sf.LineAlignment = va;
                sf.Alignment = ha;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush b = new SolidBrush(c))
                    g.DrawString(s, f, b, r, sf);
            }
        }

        private static void TxtN(Graphics g, string s, Font f, Color c, float x, float y)
        {
            using (SolidBrush b = new SolidBrush(c))
            {
                StringFormat sf = new StringFormat();
                sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s, f, b, x, y, sf);
                sf.Dispose();
            }
        }

        private void PaintHeader(Graphics g)
        {
            Rectangle r = headerRect;
            float x = r.X + 2;
            float y = r.Y + 2;

            SizeF a = g.MeasureString("AK-47", Th.FTitle);
            TxtN(g, "AK-47", Th.FTitle, Engine.Dirs["down"].Col, x, y);
            x += a.Width + 6;
            TxtN(g, "压枪分段提醒器", Th.FTitle, Th.Text, x, y);
            x += g.MeasureString("压枪分段提醒器", Th.FTitle).Width + 8;
            TxtN(g, "按住左键开始 · 松开停止 · 右键设置", Th.FSub, Th.Dim, x, y + 5);

            // 右侧参数胶囊
            string[] txt = new string[] { "600 RPM", "0.1 s/发", "30 发", "全扫 3.0 s" };
            float rx = r.Right - 2;
            for (int i = txt.Length - 1; i >= 0; i--)
            {
                SizeF sz = g.MeasureString(txt[i], Th.FBadge);
                int w = (int)sz.Width + 18;
                Rectangle pill = new Rectangle((int)(rx - w), r.Y + 8, w, 24);
                using (GraphicsPath p = Round(pill, 12))
                {
                    using (SolidBrush b = new SolidBrush(Th.Card2)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Th.Line)) g.DrawPath(pen, p);
                }
                Txt(g, txt[i], Th.FBadge, Th.Dim, pill, StringAlignment.Center, StringAlignment.Center);
                rx -= w + 8;
            }
        }

        private void PaintCue(Graphics g)
        {
            Card(g, cueCard);

            int seg = displaySeg;
            DirInfo d = Engine.DirOfSeg(seg);
            int cueShot = Engine.ShotAt(Math.Min(Now() + lead, Engine.Total));

            if (!spraying)
            {
                if (lastShots == 0) cueShot = 0;
                else cueShot = Engine.Clamp(lastShots - 1, 0, Engine.N - 1);
            }

            DrawBigArrow(g, arrowBox, d);

            float tx = arrowBox.Right + 10;
            float ty = (arrowBox.Y + arrowBox.Height / 2) - 46;

            if (spraying || lastShots > 0)
            {
                SizeF w1 = g.MeasureString("鼠标往 ", Th.FCueLbl);
                TxtN(g, "鼠标往 ", Th.FCueLbl, Th.Text, tx, ty);
                TxtN(g, d.Name, Th.FCueDir, d.Col, tx + w1.Width - 4, ty - 6);
                float w2 = g.MeasureString(d.Name, Th.FCueDir).Width;
                string verb = Engine.Segs[seg].Dir == "down" ? " 压" : " 拉";
                TxtN(g, verb, Th.FCueLbl, Th.Text, tx + w1.Width + w2 - 4, ty);

                string info = string.Format("第 {0} 发 · 第 {1}/{2} 段 · 强度 {3}",
                    cueShot + 1, seg + 1, Engine.Segs.Length, Engine.Segs[seg].Strength);
                TxtN(g, info, Th.FInfo, Th.Dim, tx, ty + 44);
                string time = string.Format("{0:0.00}s – {1:0.00}s · 提前 {2}ms",
                    Engine.SegStart(seg), Engine.SegEnd(seg), (int)(lead * 1000));
                TxtN(g, time, Th.FInfo, Th.Dim, tx, ty + 64);
            }
            else
            {
                TxtN(g, "按住鼠标左键", Th.FCueDir, Th.Text, tx, ty - 6);
                TxtN(g, "开始扫射，松开即停", Th.FInfo, Th.Dim, tx, ty + 38);
                TxtN(g, "（第一次先慢速跟一遍方向）", Th.FInfo, Th.Dim, tx, ty + 58);
            }

            // 底部一行：结果 / 语速
            string foot;
            if (spraying)
            {
                foot = string.Format("本次已打 {0} 发 / 30 · {1:0.00}s", Engine.ShotAt(Now()) + 1, Now());
            }
            else if (lastShots > 0)
            {
                foot = string.Format("上次：{0} 发 · 用时 {1:0.00}s · 共 {2} 次", lastShots, lastDuration, rounds);
            }
            else
            {
                foot = "松手即结束，重点练 0.8–1.4s 的「左下拉」";
            }
            TxtN(g, foot, Th.FInfo, Color.FromArgb(170, 185, 200), tx, timelineBar.Y - 26);
        }

        private void DrawBigArrow(Graphics g, Rectangle box, DirInfo d)
        {
            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;
            float len = Math.Min(box.Width, box.Height) * 0.74f;
            float ux = d.Vx, uy = d.Vy;
            float m = (float)Math.Sqrt(ux * ux + uy * uy);
            ux /= m; uy /= m;

            float x0 = cx - ux * len * 0.42f, y0 = cy - uy * len * 0.42f;
            float x1 = cx + ux * len * 0.48f, y1 = cy + uy * len * 0.48f;

            using (Pen glow = new Pen(Color.FromArgb(28, d.Col), 22f))
            {
                glow.StartCap = LineCap.Round; glow.EndCap = LineCap.Round;
                g.DrawLine(glow, x0, y0, x1, y1);
            }
            using (Pen pen = new Pen(d.Col, 10f))
            {
                pen.StartCap = LineCap.Round;
                g.DrawLine(pen, x0, y0, x1, y1);
            }
            float ang = (float)Math.Atan2(uy, ux);
            float hl = len * 0.30f;
            PointF tip = new PointF(x1 + ux * 12f, y1 + uy * 12f);
            PointF a = new PointF(x1 - (float)Math.Cos(ang - 0.62) * hl, y1 - (float)Math.Sin(ang - 0.62) * hl);
            PointF b = new PointF(x1 - (float)Math.Cos(ang + 0.62) * hl, y1 - (float)Math.Sin(ang + 0.62) * hl);
            using (SolidBrush br = new SolidBrush(d.Col))
                g.FillPolygon(br, new PointF[] { tip, a, b });
        }

        private void PaintTimeline(Graphics g)
        {
            Rectangle bar = timelineBar;
            float x = bar.X;
            int cur = displaySeg;
            double t = spraying ? Math.Min(Now(), Engine.Total) : (lastShots > 0 ? Math.Min(lastDuration, Engine.Total) : 0);

            using (GraphicsPath p = Round(bar, 8))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(14, 18, 24)))
                g.FillPath(b, p);

            for (int k = 0; k < Engine.Segs.Length; k++)
            {
                int shots = Engine.Segs[k].To - Engine.Segs[k].From + 1;
                float w = (float)bar.Width * shots / Engine.N;
                Rectangle cell = new Rectangle((int)Math.Round(x), bar.Y, (int)Math.Round(w), bar.Height);
                DirInfo d = Engine.DirOfSeg(k);
                int alpha = (k == cur) ? 95 : 45;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, d.Col)))
                    g.FillRectangle(b, cell);
                if (k == cur)
                {
                    using (Pen pen = new Pen(Color.FromArgb(170, d.Col), 1.4f))
                        g.DrawRectangle(pen, cell.X + 1, cell.Y + 1, cell.Width - 2, cell.Height - 2);
                }
                string label = string.Format("{0}-{1} {2}", Engine.Segs[k].From, Engine.Segs[k].To, d.Glyph);
                Txt(g, label, Th.FSeg, (k == cur) ? Color.White : Color.FromArgb(200, 210, 222), cell, StringAlignment.Center, StringAlignment.Center);
                x += w;
            }
            using (GraphicsPath p = Round(bar, 8))
            using (Pen pen = new Pen(Th.Line))
                g.DrawPath(pen, p);

            // 播放头
            if (spraying || lastShots > 0)
            {
                float px = bar.X + bar.Width * (float)(t / Engine.Total);
                using (Pen pen = new Pen(Color.White, 2f))
                    g.DrawLine(pen, px, bar.Y - 3, px, bar.Bottom + 3);
            }
        }

        private void PaintTicks(Graphics g)
        {
            Rectangle box = ticksBox;
            int cols = 15;
            int cw = box.Width / cols;
            int ch = 30;

            int curShot = spraying ? Engine.ShotAt(Math.Min(Now() + lead, Engine.Total))
                                   : (lastShots > 0 ? Engine.Clamp(lastShots - 1, 0, Engine.N - 1) : 0);
            int doneTo = spraying ? Engine.ShotAt(Now()) : (lastShots > 0 ? lastShots - 1 : -1);

            for (int i = 0; i < Engine.N; i++)
            {
                int row = i / cols, col = i % cols;
                Rectangle cell = new Rectangle(box.X + col * cw, box.Y + row * (ch + 4), cw - 3, ch);
                DirInfo d = Engine.DirOfSeg(Engine.SegOf(i + 1));
                bool isCur = (i == curShot);
                bool isDone = (i <= doneTo);

                Color bg = isCur ? Color.FromArgb(230, 240, 248) : (isDone ? Color.FromArgb(26, d.Col) : Th.Card2);
                FillRound(g, cell, 6, bg);

                Color fg = isCur ? Color.FromArgb(16, 22, 30) : d.Col;
                if (!isCur && !isDone) fg = Color.FromArgb(150, fg);
                Txt(g, d.Glyph, Th.FTick, fg, new Rectangle(cell.X, cell.Y + 2, cell.Width, 14), StringAlignment.Center, StringAlignment.Center);
                Txt(g, (i + 1).ToString(), Th.FTickN, isCur ? Color.FromArgb(30, 40, 50) : Color.FromArgb(120, 135, 150),
                    new Rectangle(cell.X, cell.Y + 15, cell.Width, 13), StringAlignment.Center, StringAlignment.Center);
            }
        }

        private void PaintPattern(Graphics g)
        {
            Card(g, rightRect);
            Rectangle box = new Rectangle(rightRect.X + 10, rightRect.Y + 36, rightRect.Width - 20, rightRect.Height - 46);
            Txt(g, "弹道示意 · 红点=当前发，箭头=鼠标应拉方向", Th.FInfo, Th.Dim,
                new Rectangle(rightRect.X + 14, rightRect.Y + 8, rightRect.Width - 28, 22), StringAlignment.Near, StringAlignment.Near);

            // 网格
            using (Pen gp = new Pen(Color.FromArgb(20, 255, 255, 255)))
            {
                for (int x = box.X; x < box.Right; x += 34) g.DrawLine(gp, x, box.Y, x, box.Bottom);
                for (int y = box.Y; y < box.Bottom; y += 34) g.DrawLine(gp, box.X, y, box.Right, y);
            }

            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < Engine.N; i++)
            {
                if (Engine.Pattern[i][0] < minX) minX = Engine.Pattern[i][0];
                if (Engine.Pattern[i][0] > maxX) maxX = Engine.Pattern[i][0];
                if (Engine.Pattern[i][1] < minY) minY = Engine.Pattern[i][1];
                if (Engine.Pattern[i][1] > maxY) maxY = Engine.Pattern[i][1];
            }
            double spanX = Math.Max(1e-6, maxX - minX), spanY = Math.Max(1e-6, maxY - minY);
            double padX = 46, padY = 56;
            double s = Math.Min((box.Width - padX * 2) / spanX, (box.Height - padY * 2) / spanY);
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            Func<double, float> Fx = delegate(double v) { return (float)(box.X + box.Width / 2.0 + (v - cx) * s); };
            Func<double, float> Fy = delegate(double v) { return (float)(box.Y + box.Height / 2.0 - (v - cy) * s); };

            int shot = spraying ? Engine.ShotAt(Math.Min(Now() + lead, Engine.Total))
                                : (lastShots > 0 ? Engine.Clamp(lastShots - 1, 0, Engine.N - 1) : 0);

            // 连线
            using (Pen pen = new Pen(Color.FromArgb(42, 255, 255, 255), 1.5f))
            {
                for (int i = 1; i < Engine.N; i++)
                    g.DrawLine(pen, Fx(Engine.Pattern[i - 1][0]), Fy(Engine.Pattern[i - 1][1]),
                                    Fx(Engine.Pattern[i][0]), Fy(Engine.Pattern[i][1]));
            }

            // 弹孔
            for (int i = 0; i < Engine.N; i++)
            {
                float px = Fx(Engine.Pattern[i][0]), py = Fy(Engine.Pattern[i][1]);
                bool isCur = (i == shot);
                bool done = (i < shot);
                if (isCur)
                {
                    using (Pen ring = new Pen(Color.FromArgb(70, 245, 166, 35), 12f))
                        g.DrawEllipse(ring, px - 8, py - 8, 16, 16);
                }
                float r = isCur ? 5.5f : (done ? 3.6f : 2.8f);
                Color c = isCur ? Color.White : (done ? Color.FromArgb(240, 245, 166, 35) : Color.FromArgb(80, 255, 255, 255));
                using (SolidBrush b = new SolidBrush(c))
                    g.FillEllipse(b, px - r, py - r, r * 2, r * 2);

                if (isCur || i == 0 || (i + 1) % 5 == 0)
                {
                    TxtN(g, (i + 1).ToString(), isCur ? Th.FInfoB : Th.FTickN,
                        isCur ? Color.White : Color.FromArgb(105, 255, 255, 255), px + 8, py - 7);
                }
            }

            // 鼠标方向箭头
            DirInfo d = Engine.DirOfSeg(Engine.SegOf(shot + 1));
            float ax = Fx(Engine.Pattern[shot][0]), ay = Fy(Engine.Pattern[shot][1]);
            float m = (float)Math.Sqrt(d.Vx * d.Vx + d.Vy * d.Vy);
            float ux = d.Vx / m, uy = d.Vy / m;
            float L = 44;
            float bx = ax + ux * L, by = ay + uy * L;

            using (Pen glow = new Pen(Color.FromArgb(45, d.Col), 12f))
            {
                glow.StartCap = LineCap.Round; glow.EndCap = LineCap.Round;
                g.DrawLine(glow, ax, ay, bx, by);
            }
            using (Pen pen = new Pen(d.Col, 3.4f))
            {
                pen.StartCap = LineCap.Round;
                g.DrawLine(pen, ax, ay, bx, by);
            }
            float ang = (float)Math.Atan2(uy, ux);
            PointF tip = new PointF(bx + ux * 9f, by + uy * 9f);
            PointF p1 = new PointF(bx + (float)Math.Cos(ang + 2.55) * 11f, by + (float)Math.Sin(ang + 2.55) * 11f);
            PointF p2 = new PointF(bx + (float)Math.Cos(ang - 2.55) * 11f, by + (float)Math.Sin(ang - 2.55) * 11f);
            using (SolidBrush br = new SolidBrush(d.Col))
                g.FillPolygon(br, new PointF[] { tip, p1, p2 });

            // 标签
            string label = "鼠标" + d.Name;
            SizeF ls = g.MeasureString(label, Th.FInfoB);
            float lx = bx + ux * 14 + 8, ly = by + uy * 14 + 4;
            if (lx + ls.Width + 10 > box.Right) lx = box.Right - ls.Width - 12;
            if (ly + ls.Height > box.Bottom) ly = box.Bottom - ls.Height - 2;
            Rectangle lb = new Rectangle((int)lx - 6, (int)ly - 2, (int)ls.Width + 12, (int)ls.Height + 4);
            FillRound(g, lb, 5, Color.FromArgb(215, 9, 12, 17));
            TxtN(g, label, Th.FInfoB, d.Col, lb.X + 6, lb.Y + 2);
        }

        private void PaintTable(Graphics g)
        {
            Card(g, tableRect);
            int x0 = tableRect.X + 14;
            int[] cw = new int[] { 74, 128, 116, 54 };
            int y = tableRect.Y + 8;

            string[] head = new string[] { "发数", "时间", "鼠标方向", "强度" };
            int hx = x0;
            for (int i = 0; i < head.Length; i++)
            {
                Txt(g, head[i], Th.FTableH, Th.Dim, new Rectangle(hx, y, cw[i], 20), StringAlignment.Center, StringAlignment.Near);
                hx += cw[i];
            }
            Txt(g, "要点", Th.FTableH, Th.Dim, new Rectangle(hx, y, tableRect.Right - hx - 14, 20), StringAlignment.Center, StringAlignment.Near);

            for (int k = 0; k < Engine.Segs.Length; k++)
            {
                Seg sg = Engine.Segs[k];
                DirInfo d = Engine.DirOfSeg(k);
                Rectangle row = new Rectangle(x0 - 8, y + 20 + k * 26, tableRect.Width - 20, 26);
                if (k == displaySeg)
                {
                    FillRound(g, row, 6, Color.FromArgb(22, 245, 166, 35));
                }
                int rx = x0;
                Txt(g, sg.From + " – " + sg.To, Th.FMono, Color.FromArgb(207, 216, 227), new Rectangle(rx, row.Y, cw[0], 26), StringAlignment.Center, StringAlignment.Near);
                rx += cw[0];
                Txt(g, string.Format("{0:0.00} – {1:0.00}s", Engine.SegStart(k), Engine.SegEnd(k)),
                    Th.FMono, Color.FromArgb(207, 216, 227), new Rectangle(rx, row.Y, cw[1], 26), StringAlignment.Center, StringAlignment.Near);
                rx += cw[1];
                Txt(g, d.Glyph + " " + d.Name + (sg.Dir == "down" ? "" : ""), Th.FTable, d.Col,
                    new Rectangle(rx, row.Y, cw[2], 26), StringAlignment.Center, StringAlignment.Near);
                rx += cw[2];
                Txt(g, sg.Strength, Th.FTable, Th.Dim, new Rectangle(rx, row.Y, cw[3], 26), StringAlignment.Center, StringAlignment.Near);
                rx += cw[3];
                Txt(g, sg.Note, Th.FTable, Color.FromArgb(170, 184, 197),
                    new Rectangle(rx, row.Y, tableRect.Right - rx - 14, 26), StringAlignment.Center, StringAlignment.Near);
            }
        }

        private static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "已就绪";
            s = s.Replace("Microsoft ", "").Replace(" Desktop", "");
            return s.Length > 18 ? s.Substring(0, 18) : s;
        }

        private string VoiceText()
        {
            if (voice == null) return "语音：启动中";
            if (voice.Failed) return "语音：不可用 " + voice.LastError;
            if (!voice.Ready) return "语音：启动中";
            if (voice.Fallback) return "语音：英文回退（" + Short(voice.VoiceName) + "）";
            return "语音：" + Short(voice.VoiceName);
        }

        private void PaintStatus(Graphics g)
        {
            Rectangle r = statusRect;
            string left = string.Format("语速 {0}{1}  ·  已完成 {2} 次  ·  提前量 {3}ms",
                speedLevel > 0 ? "+" + speedLevel : "0", progressive ? "（每次 +1）" : "（固定）", rounds, (int)(lead * 1000));
            string right = string.Format("{0}  ·  {1}  ·  {2}",
                hookOn ? (HookInstalled ? "全局监听 开" : "全局监听 失败") : "全局监听 关",
                soundOn ? "声音 开" : "声音 关",
                VoiceText());

            Txt(g, left, Th.FStatus, Th.Dim, new Rectangle(r.X + 2, r.Y, r.Width / 2, r.Height), StringAlignment.Center, StringAlignment.Near);
            Txt(g, right, Th.FStatus, Th.Dim, new Rectangle(r.X + r.Width / 2, r.Y, r.Width / 2 - 2, r.Height), StringAlignment.Center, StringAlignment.Far);
        }

        // ======================= 供自检 / 截图使用 =======================
        public void SetMock(double t)
        {
            mockT = t;
            spraying = true;
            rounds = 1;
            lastSeg = -1; lastTick = -1;
            UpdateState(t);
            Invalidate();
        }

        public void SetMockIdle()
        {
            mockT = -1;
            spraying = false;
            Invalidate();
        }

        // 无鼠标操作的自检：跑一遍真实的计时/提示/语音流程
        public void DisableHook() { hookMuted = true; }

        public string Demo(int ms)
        {
            StartSpray();
            Application.DoEvents();
            Stopwatch w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms)
            {
                Application.DoEvents();
                Thread.Sleep(4);
            }
            int midSeg = lastSeg;
            int midShot = Engine.ShotAt(Math.Min(sw.Elapsed.TotalSeconds, Engine.Total)) + 1;
            StopSpray(false);
            Application.DoEvents();
            return string.Format(
                "midShot={0} midSeg={1} | lastShots={2} duration={3:0.00}s segAtStop={4} rounds={5} speedLevel={6} | voice ready={7} engine={8} banner={9}",
                midShot, midSeg, lastShots, lastDuration, lastSeg, rounds, speedLevel,
                voice.Ready, voice.Engine, voice.Banner);
        }
    }

    // ---------- 入口 ----------
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint uMode);

        private static string logPath;

        private static void L(string s)
        {
            if (logPath == null) return;
            try { File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n", Encoding.UTF8); }
            catch { }
        }

        private static void Watchdog(int ms)
        {
            Thread t = new Thread(delegate ()
            {
                Thread.Sleep(ms);
                L("WATCHDOG 超时退出");
                Environment.Exit(4);
            });
            t.IsBackground = true;
            t.Start();
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string selftest = Arg(args, "--selftest");
            string shot = Arg(args, "--shot");
            string at = Arg(args, "--at");
            string say = Arg(args, "--say");
            string diag = Arg(args, "--diag");

            if (selftest != null) return SelfTest(selftest);

            // ---- 音效自检：把合成出来的 wav 全部导出 ----
            string snd = Arg(args, "--sounds");
            if (snd != null)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm sf = new MainForm();
                sf.TopMost = false;
                sf.DisableHook();
                string rep = sf.DumpSounds(snd);
                try { File.WriteAllText(Path.Combine(snd, "sounds.txt"), rep, Encoding.UTF8); } catch { }
                sf.Close();
                return 0;
            }

            // ---- 设置窗口自检：把设置界面渲染成 png ----
            string shotSet = Arg(args, "--shot-settings");
            if (shotSet != null)
            {
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    VoiceOut vv = new VoiceOut();
                    Thread.Sleep(1800);
                    Cfg cc = new Cfg();
                    cc.Load(MainForm.SettingsPath());
                    using (SettingsForm form = new SettingsForm(vv, cc))
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(0, 0);
                        form.Show();
                        Application.DoEvents();
                        Thread.Sleep(300);
                        Application.DoEvents();
                        using (Bitmap bmp = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                            bmp.Save(shotSet, ImageFormat.Png);
                        }
                        form.Close();
                    }
                    vv.Stop();
                    return 0;
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(shotSet + ".err.txt", ex.ToString(), Encoding.UTF8); } catch { }
                    return 3;
                }
            }

            // ---- 无鼠标自检：跑 1.5 秒的真实扫射流程 ----
            if (Arg(args, "--demo") != null)
            {
                string dp = (diag != null && diag.Length > 0) ? diag : "demo.log";
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm df = new MainForm();
                df.TopMost = false;
                df.DisableHook();
                df.StartPosition = FormStartPosition.Manual;
                df.Location = new Point(-32000, -32000);
                df.Show();
                Application.DoEvents();
                Thread.Sleep(1200);                 // 等语音引擎就绪
                Application.DoEvents();
                int ms = 1500;
                try { int.TryParse(Arg(args, "--demo"), out ms); } catch { }
                if (ms < 300) ms = 1500;
                string report = df.Demo(ms);
                try { File.WriteAllText(dp, report + "\r\n", Encoding.UTF8); } catch { }
                df.Close();
                return 0;
            }

            // ---- 语音自检模式 ----
            if (say != null)
            {
                string dp = (diag != null && diag.Length > 0) ? diag : "say.log";
                VoiceOut v = new VoiceOut();
                bool ok = v.SayNow(say, 0, 1400);
                string info = string.Format("ok={0}\r\nengine={1}\r\nvoice={2}\r\nbanner={3}\r\nfallback={4}\r\nerr={5}\r\n",
                    ok, v.Engine, v.VoiceName, v.Banner, v.Fallback, v.LastError);
                try { File.WriteAllText(dp, info, Encoding.UTF8); } catch { }
                return ok ? 0 : 1;
            }
            if (shot != null) logPath = shot + ".log";

            try
            {
                SetErrorMode(3);

                // ---- 自检渲染模式（不进入消息循环）----
                if (shot != null)
                {
                    Watchdog(20000);
                    L("begin");
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    L("styles ok");
                    MainForm mf = new MainForm();
                    L("form created");
                    mf.TopMost = false;
                    mf.DisableHook();
                    mf.StartPosition = FormStartPosition.Manual;
                    mf.Location = new Point(0, 0);
                    mf.ClientSize = new Size(1060, 720);
                    L("show");
                    mf.Show();
                    Application.DoEvents();
                    L("shown");
                    double t = 1.35;
                    if (at != null) double.TryParse(at, out t);
                    if (t > 0) mf.SetMock(t); else mf.SetMockIdle();
                    Thread.Sleep(2200);
                    Application.DoEvents();
                    L("mock " + t);
                    using (Bitmap bmp = new Bitmap(mf.Width, mf.Height))
                    {
                        mf.DrawToBitmap(bmp, new Rectangle(0, 0, mf.Width, mf.Height));
                        L("drawn");
                        bmp.Save(shot, ImageFormat.Png);
                        L("saved");
                    }
                    mf.Close();
                    L("end");
                    return 0;
                }

                // ---- 正常模式 ----
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                L("EX " + ex);
                try { File.WriteAllText((shot != null ? shot : "crash") + ".err.txt", ex.ToString(), Encoding.UTF8); }
                catch { }
                return 3;
            }
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return (i + 1 < args.Length) ? args[i + 1] : "";
            return null;
        }

        private static int SelfTest(string path)
        {
            StringBuilder sb = new StringBuilder();
            int bad = 0;
            Action<string> log = delegate(string s) { sb.AppendLine(s); };

            if (Engine.Pattern.Length != Engine.N) { log("✗ 弹道点数 " + Engine.Pattern.Length); bad++; }
            if (Engine.Segs.Length != 6) { log("✗ 段数不是 6"); bad++; }
            if (Engine.Segs[0].From != 1) { log("✗ 首段不是从第 1 发开始"); bad++; }
            if (Engine.Segs[Engine.Segs.Length - 1].To != Engine.N) { log("✗ 末段不是到第 30 发结束"); bad++; }
            for (int k = 1; k < Engine.Segs.Length; k++)
                if (Engine.Segs[k].From != Engine.Segs[k - 1].To + 1)
                { log("✗ 分段不连续：第 " + k + " 段"); bad++; }

            log("段 | 发数  | 时间(s)     | 弹道 dx,dy     | 期望方向  | 表中方向");
            for (int k = 0; k < Engine.Segs.Length; k++)
            {
                Seg s = Engine.Segs[k];
                double[] a = Engine.Pattern[s.From - 1];
                double[] b = Engine.Pattern[s.To - 1];
                double dx = b[0] - a[0], dy = b[1] - a[1];
                double ratio = Math.Abs(dx) / Math.Abs(dy);
                string expect = "down";
                if (ratio >= 0.15) expect = dx > 0 ? "downLeft" : "downRight";
                bool ok = expect == s.Dir;
                if (!ok) { log(string.Format("✗ 第 {0}-{1} 发：dx={2:0.0} 期望 {3} 表中 {4}", s.From, s.To, dx, expect, s.Dir)); bad++; }
                log(string.Format("{0,2} | {1,-5} | {2:0.00}-{3:0.00} | dx={4,5:0.0},dy={5,5:0.0} | {6,-9} | {7}{8}",
                    k + 1, s.From + "-" + s.To, Engine.SegStart(k), Engine.SegEnd(k), dx, dy, expect, s.Dir, ok ? " ✓" : " ✗"));
            }

            // 时间→发数
            int[][] cases = new int[][] {
                new int[]{ 0, 0 }, new int[]{ 9, 9 }, new int[]{ 10, 10 }, new int[]{ 14, 14 },
                new int[]{ 15, 15 }, new int[]{ 26, 26 }, new int[]{ 29, 29 } };
            for (int i = 0; i < cases.Length; i++)
            {
                int shot = Engine.ShotAt(cases[i][0] * 0.1 + 0.001);
                if (shot != cases[i][1]) { log("✗ ShotAt(" + cases[i][0] + "*0.1) = " + shot + "，应为 " + cases[i][1]); bad++; }
            }
            if (Engine.ShotAt(3.0) != 29) { log("✗ ShotAt(3.0) 应夹到 29"); bad++; }
            if (Engine.SegOf(1) != 0 || Engine.SegOf(30) != 5) { log("✗ SegOf 边界错误"); bad++; }
            if (Math.Abs(Engine.SegEnd(5) - 3.0) > 1e-9) { log("✗ 末段结束时间不是 3.0s"); bad++; }
            // 每段起始时间连续
            for (int k = 1; k < Engine.Segs.Length; k++)
                if (Math.Abs(Engine.SegStart(k) - Engine.SegEnd(k - 1)) > 1e-9) { log("✗ 时间不连续：第 " + k + " 段"); bad++; }
            // 方向表齐全
            for (int k = 0; k < Engine.Segs.Length; k++)
                if (!Engine.Dirs.ContainsKey(Engine.Segs[k].Dir)) { log("✗ 缺少方向定义 " + Engine.Segs[k].Dir); bad++; }

            log("");
            log("总时长 " + Engine.Total.ToString("0.0") + "s，共 " + Engine.N + " 发，每发 " + Engine.DT.ToString("0.0") + "s");
            log(bad == 0 ? "全部检查通过 ✓" : "发现 " + bad + " 处问题 ✗");
            try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); } catch { }
            try { Console.WriteLine(sb.ToString()); } catch { }
            return bad == 0 ? 0 : 1;
        }
    }
}
