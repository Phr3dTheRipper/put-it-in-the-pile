using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RedundantSoftware {
 public sealed class Quota {
  public double Used; public int Minutes; public long Reset;
  public double Remaining { get { return Math.Max(0, Math.Min(100, 100 - Used)); } }
  public string Label { get { return Minutes == 10080 ? "WEEKLY" : Minutes == 300 ? "5-HOUR" : Minutes == 1440 ? "DAILY" : Minutes > 0 ? (Minutes >= 60 && Minutes % 60 == 0 ? (Minutes / 60) + "-HOUR" : Minutes + "-MINUTE") : "QUOTA"; } }
 }
 public sealed class Usage {
  public Quota Primary, Secondary;
  public static Dictionary<string, object> Obj(object value) { return value as Dictionary<string, object>; }
  public static object Get(Dictionary<string, object> o, string k) { object v; return o != null && o.TryGetValue(k, out v) ? v : null; }
  static Quota ParseWindow(object v) {
   var o = Obj(v); if (o == null || Get(o, "usedPercent") == null) return null;
   double used; if (!double.TryParse(Convert.ToString(Get(o, "usedPercent"), CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out used) || double.IsNaN(used) || double.IsInfinity(used)) return null;
   int mins; int.TryParse(Convert.ToString(Get(o, "windowDurationMins")), out mins);
   long reset; long.TryParse(Convert.ToString(Get(o, "resetsAt")), out reset);
   return new Quota { Used = Math.Max(0, Math.Min(100, used)), Minutes = mins, Reset = reset };
  }
  public static Usage Parse(string json) {
   var root = Obj(new JavaScriptSerializer().DeserializeObject(json));
   if (Get(root, "error") != null) throw new InvalidOperationException("Codex could not read usage. Check your existing Codex sign-in, then refresh.");
   var result = Obj(Get(root, "result")) ?? root;
   var byId = Obj(Get(result, "rateLimitsByLimitId"));
   var bucket = Obj(Get(byId, "codex")) ?? Obj(Get(result, "rateLimits"));
   var p = ParseWindow(Get(bucket, "primary")); var s = ParseWindow(Get(bucket, "secondary"));
   if (p == null && s == null) throw new InvalidOperationException("No Codex quota windows returned. A ChatGPT-backed Codex sign-in is required.");
   return new Usage { Primary = p ?? s, Secondary = p == null ? null : s };
  }
  public static Usage Demo() { return new Usage { Primary = new Quota { Used = 32, Minutes = 300, Reset = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds() }, Secondary = new Quota { Used = 57, Minutes = 10080, Reset = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds() } }; }
 }
 public static class CodexSource {
  public static string FindExecutable() {
   string custom = Environment.GetEnvironmentVariable("PILE_CODEX_PATH");
   if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return Path.GetFullPath(custom);
   foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
    try { string file = Path.Combine(dir.Trim('"'), "codex.exe"); if (File.Exists(file)) return file; } catch { }
   }
   string bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
   if (Directory.Exists(bin)) {
    var files = Directory.GetFiles(bin, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc);
    if (files.Any()) return files.First();
   }
   throw new FileNotFoundException("Codex CLI not found. Install/sign in to Codex, or set PILE_CODEX_PATH to codex.exe. Demo mode is in the ••• menu.");
  }
  static string Receive(Process process, int id, DateTime deadline) {
   while (DateTime.UtcNow < deadline) {
    int ms = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
    var pending = process.StandardOutput.ReadLineAsync();
    if (!pending.Wait(ms)) throw new TimeoutException("Codex took too long. Try refreshing in a moment.");
    string line = pending.Result; if (line == null) throw new IOException("Codex closed the connection. Check your Codex sign-in.");
    Dictionary<string, object> row;
    try { row = Usage.Obj(new JavaScriptSerializer().DeserializeObject(line)); } catch { continue; }
    if (Convert.ToString(Usage.Get(row, "id")) == id.ToString(CultureInfo.InvariantCulture)) return line;
   }
   throw new TimeoutException("Codex took too long. Try refreshing in a moment.");
  }
  public static Usage Read() {
   using (var p = new Process()) {
    p.StartInfo = new ProcessStartInfo(FindExecutable(), "app-server") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
    p.ErrorDataReceived += delegate { }; // Drain diagnostics without storing account information.
    try {
     p.Start(); p.BeginErrorReadLine(); var deadline = DateTime.UtcNow.AddSeconds(25);
     p.StandardInput.WriteLine("{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"put_it_in_the_pile\",\"title\":\"Put It in the Pile\",\"version\":\"1.0.0\"}}}"); p.StandardInput.Flush();
     var hello = Usage.Obj(new JavaScriptSerializer().DeserializeObject(Receive(p, 1, deadline)));
     if (Usage.Get(hello, "error") != null) throw new InvalidOperationException("This Codex CLI could not initialize. Update Codex and retry.");
     p.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
     p.StandardInput.WriteLine("{\"method\":\"account/rateLimits/read\",\"id\":2}"); p.StandardInput.Flush();
     return Usage.Parse(Receive(p, 2, deadline));
    } finally { try { p.StandardInput.Close(); if (!p.WaitForExit(1500)) { p.Kill(); p.WaitForExit(1500); } } catch { } }
   }
  }
 }
 public sealed class PileApp {
  Window w; readonly string statePath; readonly bool preview; bool demo, compact, busy, closed, dataStale; long count = 1; Usage usage; DateTime fetched;
  DispatcherTimer poll, noticeTimer;
  readonly string[] names = { "CodexMeter", "TokenBar", "CodexMeter Pro", "UsageThing", "TokenBar 2", "CodexMeter FINAL", "this one has a tray icon", "Meter (but orange)" };
  readonly string[] jokes = { "The pile welcomes your contribution.", "This one has rounded corners. Revolutionary.", "Another problem already solved. Solved again.", "A bold new direction: the same direction.", "The progress bar community is healing.", "Finally. A usage meter with a button.", "Your contribution has been stacked accordingly.", "Now with 100% more existing functionality." };
  public PileApp(bool demoMode, string state, bool isPreview) {
   demo = demoMode; preview = isPreview; statePath = state;
   using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml")) w = (Window)XamlReader.Load(stream);
   if (preview) count = 6; else LoadState();
   Wire(); UpdateCount(); DrawPile(false); UpdatePin(); SetCompact(compact);
   w.Left = Math.Min(Math.Max(w.Left, SystemParameters.WorkArea.Left), Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - w.Width));
   w.Top = Math.Min(Math.Max(w.Top, SystemParameters.WorkArea.Top), Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - w.Height));
   if (demo) Apply(Usage.Demo());
  }
  T Find<T>(string name) where T : class { return w.FindName(name) as T; }
  TextBlock Text(string name) { return Find<TextBlock>(name); }
  void Click(string name, RoutedEventHandler action) { Find<Button>(name).Click += action; }
  void Wire() {
   Find<Border>("DragRegion").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (!(e.OriginalSource is FrameworkElement) || !InsideButton(e.OriginalSource as DependencyObject)) { w.DragMove(); } };
   Find<Grid>("CompactView").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (!InsideButton(e.OriginalSource as DependencyObject)) w.DragMove(); };
   Click("CloseButton", delegate { w.Close(); }); Click("CompactClose", delegate { w.Close(); });
   Click("CompactButton", delegate { SetCompact(true); }); Click("ExpandButton", delegate { SetCompact(false); });
   Click("TossButton", delegate { Toss(); }); Click("CompactToss", delegate { Toss(); });
   Click("RefreshButton", delegate { Refresh(); });
   Click("PinButton", delegate { w.Topmost = !w.Topmost; UpdatePin(); Save(); });
   Click("MenuButton", delegate { ShowMenu(); });
   w.KeyDown += delegate(object sender, KeyEventArgs e) {
    if (e.Key == Key.Escape) { SetCompact(!compact); e.Handled = true; }
    if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R) { Refresh(); e.Handled = true; }
    if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D) { ToggleDemo(); e.Handled = true; }
   };
   w.Closed += delegate { closed = true; Save(); if (poll != null) poll.Stop(); if (noticeTimer != null) noticeTimer.Stop(); };
   Find<Grid>("MeterTrack").SizeChanged += delegate { UpdateBar(); };
  }
  static bool InsideButton(DependencyObject o) { while (o != null) { if (o is Button) return true; o = VisualTreeHelper.GetParent(o); } return false; }
  void UpdatePin() { var b = Find<Button>("PinButton"); b.Content = w.Topmost ? "● ALWAYS ON TOP" : "○ SOMETIMES UNDER THINGS"; b.Foreground = Brush(w.Topmost ? "#53664C" : "#77776A"); }
  static SolidColorBrush Brush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
  public void Run() {
   w.Show();
   if (!demo) w.Dispatcher.BeginInvoke(new Action(Refresh));
   poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) }; poll.Tick += delegate { if (!demo) Refresh(); else UpdateTime(); }; poll.Start();
  }
  public void Render(string path) {
   w.Topmost = false; w.Show(); w.UpdateLayout();
   var bitmap = new RenderTargetBitmap(414, 744, 96, 96, PixelFormats.Pbgra32); bitmap.Render(w);
   var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
   using (var f = File.Create(path)) encoder.Save(f); w.Close();
  }
  void LoadState() {
   w.Left = Math.Max(0, SystemParameters.WorkArea.Right - w.Width - 35); w.Top = SystemParameters.WorkArea.Top + 45;
   try {
    if (!File.Exists(statePath)) return;
    var o = Usage.Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText(statePath)));
    long n; if (long.TryParse(Convert.ToString(Usage.Get(o, "pileCount")), out n)) count = Math.Max(1, Math.Min(999999, n));
    object pin = Usage.Get(o, "alwaysOnTop"); if (pin is bool) w.Topmost = (bool)pin;
    compact = object.Equals(Usage.Get(o, "compact"), true);
    double pos; if (double.TryParse(Convert.ToString(Usage.Get(o, "left")), out pos) && !double.IsNaN(pos) && !double.IsInfinity(pos)) w.Left = pos;
    if (double.TryParse(Convert.ToString(Usage.Get(o, "top")), out pos) && !double.IsNaN(pos) && !double.IsInfinity(pos)) w.Top = pos;
   } catch { }
  }
  void Save() {
   if (preview) return;
   try { File.WriteAllText(statePath, new JavaScriptSerializer().Serialize(new { pileCount = count, alwaysOnTop = w.Topmost, compact = compact, left = w.Left, top = w.Top }), Encoding.UTF8); }
   catch { Find<Border>("Shell").ToolTip = "Could not save settings. Keep the app in a writable folder to remember your pile."; }
  }
  void SetCompact(bool value) {
   compact = value; Find<Grid>("FullView").Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
   Find<Grid>("CompactView").Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
   w.Height = compact ? 85 : 744;
   if (w.Top + w.Height > SystemParameters.WorkArea.Bottom) w.Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - w.Height);
   if (w.IsVisible) Save();
  }
  void UpdateCount() { Text("EditionLabel").Text = "No. " + count.ToString("D4"); Text("PileCountLabel").Text = count.ToString("N0") + (count == 1 ? " METER. SAME IDEA." : " METERS. SAME IDEA."); }
  void Toss() {
   if (count < 999999) count++;
   UpdateCount(); DrawPile(true); Notice(jokes[(int)((count - 2) % jokes.Length)]); Save();
   if (compact) { Text("CompactValue").Text = count.ToString("N0") + " meters. Still one idea."; }
  }
  void Notice(string text) {
   Text("Punchline").Text = text;
   if (noticeTimer != null) noticeTimer.Stop();
   noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
   noticeTimer.Tick += delegate { noticeTimer.Stop(); Text("Punchline").Text = "A new meter. A familiar feeling."; UpdateCompact(); }; noticeTimer.Start();
  }
  void DrawPile(bool animate) {
   var canvas = Find<Canvas>("PileCanvas"); canvas.Children.Clear(); int visible = (int)Math.Min(count, 7);
   for (int i = 0; i < visible; i++) {
    long number = count - visible + i; bool newest = i == visible - 1;
    var b = new Border { Width = 171, Height = 56, Background = Brush(newest ? "#F7F5EA" : "#E2E0D3"), BorderBrush = Brush("#727364"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 6, 8, 6) };
    var panel = new StackPanel();
    var header = new Grid(); header.Children.Add(new TextBlock { Text = newest ? "Put It in the Pile" : names[(int)(number % names.Length)], FontFamily = new FontFamily("Consolas"), FontSize = 9, Foreground = Brush("#4E5146") });
    header.Children.Add(new TextBlock { Text = "×", HorizontalAlignment = HorizontalAlignment.Right, FontSize = 10, Foreground = Brush("#858575") }); panel.Children.Add(header);
    var bar = new Grid { Height = 6, Background = Brush("#CBCABD"), Margin = new Thickness(0, 6, 0, 4) }; bar.Children.Add(new Border { Background = Brush(newest ? "#D65328" : "#919785"), Width = 109, HorizontalAlignment = HorizontalAlignment.Left }); panel.Children.Add(bar);
    panel.Children.Add(new TextBlock { Text = "a progress bar. on your desktop.", FontFamily = new FontFamily("Consolas"), FontSize = 7, Foreground = Brush("#858575") }); b.Child = panel;
    double angle = newest ? -5 : ((number * 13) % 27) - 13;
    var transforms = new TransformGroup(); transforms.Children.Add(new RotateTransform(angle, 85, 28)); var translate = new TranslateTransform(); transforms.Children.Add(translate); b.RenderTransform = transforms;
    Canvas.SetLeft(b, newest ? 95 : 19 + ((number * 47) % 142)); Canvas.SetTop(b, newest ? 39 : 53 + ((number * 7) % 27)); canvas.Children.Add(b);
    if (animate && newest) { translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-135, 0, TimeSpan.FromMilliseconds(470)) { EasingFunction = new BounceEase { Bounces = 2, Bounciness = 4, EasingMode = EasingMode.EaseOut } }); }
   }
  }
  async void Refresh() {
   if (busy || closed) return;
   if (demo) { Apply(Usage.Demo()); Notice("Demo refreshed. Even the fake numbers need attention."); return; }
   busy = true; Find<Button>("RefreshButton").IsEnabled = false; Text("SourceLabel").Text = "CHECKING";
   try { var result = await Task.Run(() => CodexSource.Read()); if (!closed) Apply(result); }
   catch (Exception e) { if (!closed) {
    dataStale = true; Text("SourceLabel").Text = usage == null ? "UNAVAILABLE" : "STALE"; Find<System.Windows.Shapes.Ellipse>("StatusDot").Fill = Brush("#AE713E");
    Text("ResetLabel").Text = usage == null ? "Usage unavailable · open ••• for help or demo" : "Last updated " + fetched.ToString("t") + " · refresh failed";
    Find<Border>("Shell").ToolTip = e.Message;
    if (usage == null) { Text("RemainingValue").Text = "—"; Text("RemainingSuffix").Text = ""; } UpdateCompact();
   } } finally { busy = false; if (!closed) Find<Button>("RefreshButton").IsEnabled = true; }
  }
  void Apply(Usage value) {
   usage = value; dataStale = false; fetched = DateTime.Now; Find<Border>("Shell").ToolTip = demo ? "Fictional usage for the bit. Ctrl+D returns to live data." : "Read from your existing Codex CLI sign-in. Refreshes every 60 seconds.";
   Text("SourceLabel").Text = demo ? "DEMO DATA" : "LIVE DATA";
   Find<System.Windows.Shapes.Ellipse>("StatusDot").Fill = Brush(demo ? "#C5832B" : "#65805B");
   Text("RemainingValue").Text = usage.Primary.Remaining.ToString("0.#", CultureInfo.InvariantCulture); Text("RemainingSuffix").Text = "%"; Text("WindowLabel").Text = usage.Primary.Label;
   Find<Border>("SecondWindow").Visibility = usage.Secondary == null ? Visibility.Collapsed : Visibility.Visible;
   UpdateTime(); UpdateBar(); UpdateCompact();
  }
  string ResetText(Quota q) {
   if (q.Reset <= 0) return "Reset time unavailable";
   try { var at = DateTimeOffset.FromUnixTimeSeconds(q.Reset).ToLocalTime(); var span = at - DateTimeOffset.Now;
    if (span.TotalSeconds <= 0) return "Reset due · refresh to check";
    string duration = span.TotalDays >= 1 ? (int)span.TotalDays + "d " + span.Hours + "h" : span.TotalHours >= 1 ? (int)span.TotalHours + "h " + span.Minutes + "m" : Math.Max(1, (int)span.TotalMinutes) + "m";
    return "Resets in " + duration + " · " + at.ToString("ddd h:mm tt");
   } catch { return "Reset time unavailable"; }
  }
  void UpdateTime() { if (usage == null) return; Text("ResetLabel").Text = ResetText(usage.Primary); if (usage.Secondary != null) Text("SecondLabel").Text = usage.Secondary.Label + "  " + usage.Secondary.Remaining.ToString("0.#") + "% left · " + ResetText(usage.Secondary).Split('·')[0].Trim(); }
  void UpdateBar() { if (usage == null) return; var fill = Find<Border>("MeterFill"); double target = Math.Max(0, Find<Grid>("MeterTrack").ActualWidth * usage.Primary.Remaining / 100); fill.BeginAnimation(FrameworkElement.WidthProperty, null); fill.Width = target; }
  void UpdateCompact() { Text("CompactValue").Text = usage == null ? (dataStale ? "Usage unavailable · expand for help" : "Connecting…") : usage.Primary.Remaining.ToString("0.#") + "% left · " + usage.Primary.Label.ToLowerInvariant() + (demo ? " [demo]" : dataStale ? " [stale]" : ""); }
  void ToggleDemo() {
   if (busy) { Notice("Waiting for the current usage check to finish."); return; }
   demo = !demo; dataStale = false;
   if (demo) Apply(Usage.Demo()); else { usage = null; Text("RemainingValue").Text = "—"; Text("RemainingSuffix").Text = ""; Text("WindowLabel").Text = "QUOTA"; Text("ResetLabel").Text = "Looking for your existing Codex sign-in…"; Find<Border>("SecondWindow").Visibility = Visibility.Collapsed; Find<Border>("MeterFill").Width = 0; UpdateCompact(); Refresh(); }
  }
  void ShowMenu() {
   var menu = new ContextMenu { Background = Brush("#F5F3E9"), Foreground = Brush("#25251F"), FontFamily = new FontFamily("Segoe UI"), FontSize = 12, PlacementTarget = Find<Button>("MenuButton") };
   AddMenu(menu, demo ? "Use real usage data   Ctrl+D" : "Use demo data   Ctrl+D", ToggleDemo);
   AddMenu(menu, "Copy my groundbreaking launch post", CopyPost);
   AddMenu(menu, "Compact mode   Esc", () => SetCompact(true));
   AddMenu(menu, "About this breakthrough", About);
   menu.IsOpen = true;
  }
  static void AddMenu(ContextMenu menu, string title, Action action) { var item = new MenuItem { Header = title, Padding = new Thickness(10, 8, 10, 8) }; item.Click += delegate { action(); }; menu.Items.Add(item); }
  void CopyPost() {
   try { Clipboard.SetText("I couldn't find a Codex usage meter that truly understood my needs, so I built one.\n\nIntroducing PUT IT IN THE PILE: a revolutionary app that puts a number above your other windows.\n\nFeatures:\n- The same usage data, but in MY rectangle.\n- Always on top, because this is important.\n- A button that puts another meter in the pile.\n- 0% originality. 100% self-awareness.\n\nMy local pile now contains " + count + " meter(s). Finally, a solution to a solved problem.\n\nPut it in the pile."); Notice("Launch post copied. The subreddit can rest easy."); }
   catch { Notice("Clipboard busy. Your announcement can wait. Try again."); }
  }
  void About() {
   var dialog = new Window { Title = "About this breakthrough", Width = 355, Height = 325, Owner = w, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Brush("#F5F3E9"), Topmost = w.Topmost };
   var p = new StackPanel { Margin = new Thickness(23) };
   p.Children.Add(new TextBlock { Text = "A solution to a solved problem.", FontSize = 18, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
   p.Children.Add(new TextBlock { Text = "Put It in the Pile v1.0\n\nReads quota windows through your existing Codex CLI sign-in. Refreshes every 60 seconds. No prompts or inference requests.\n\nNeed live data? Install/sign in to Codex CLI, then refresh. PILE_CODEX_PATH can point to codex.exe.\n\nPile count is local. Originality is satire. Demo usage is labeled. Unofficial and unaffiliated with OpenAI.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), Foreground = Brush("#68695E") });
   var ok = new Button { Content = "A historic achievement.", Padding = new Thickness(8), Background = Brush("#D65328"), Foreground = Brushes.White }; ok.Click += delegate { dialog.Close(); }; p.Children.Add(ok); dialog.Content = p; dialog.ShowDialog();
  }
 }
 public static class Program {
  [STAThread] public static int Main(string[] args) {
   try {
    if (args.Length >= 2 && args[0] == "--self-test") { Tests.Run(args[1]); return 0; }
    if (args.Length >= 2 && args[0] == "--check-data") { var u = CodexSource.Read(); File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(new { primary = u.Primary, secondary = u.Secondary })); return 0; }
    bool render = args.Length >= 2 && args[0] == "--preview";
    string state = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pile-state.json");
    int si = Array.IndexOf(args, "--state"); if (si >= 0 && si + 1 < args.Length) state = Path.GetFullPath(args[si + 1]);
    var app = new Application(); var widget = new PileApp(render || args.Contains("--demo"), state, render);
    if (render) widget.Render(Path.GetFullPath(args[1])); else { widget.Run(); app.Run(); } return 0;
   } catch (Exception e) {
    int report = Array.IndexOf(args, "--self-test"); if (report >= 0 && report + 1 < args.Length) File.WriteAllText(args[report + 1], "FAIL: " + e.ToString());
    else if (args.Length >= 2 && args[0] == "--check-data") File.WriteAllText(args[1], "ERROR: " + e.Message);
    else MessageBox.Show(e.Message, "The pile encountered a problem"); return 1;
   }
  }
 }
 public static class Tests {
  static void Check(bool value, string name, List<string> results) { if (!value) throw new Exception(name); results.Add("PASS " + name); }
  public static void Run(string report) {
   var r = new List<string>();
   var weekly = Usage.Parse("{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":3,\"windowDurationMins\":10080,\"resetsAt\":1793797701},\"secondary\":null}}}");
   Check(weekly.Primary.Remaining == 97 && weekly.Primary.Label == "WEEKLY" && weekly.Secondary == null, "weekly-only account remains a single weekly window", r);
   var multi = Usage.Parse("{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":99}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":100,\"windowDurationMins\":10080}}}}}");
   Check(multi.Primary.Remaining == 100 && multi.Primary.Label == "5-HOUR" && multi.Secondary.Remaining == 0, "Codex bucket preferred; zero and fully used quotas handled", r);
   var secondary = Usage.Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":{\"usedPercent\":12,\"windowDurationMins\":1440}}}");
   Check(secondary.Primary.Remaining == 88 && secondary.Primary.Label == "DAILY" && secondary.Secondary == null, "secondary-only response handled", r);
   bool missing = false; try { Usage.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":null}}}"); } catch (InvalidOperationException) { missing = true; }
   Check(missing, "unavailable quota never displayed as zero usage", r);
   bool error = false; try { Usage.Parse("{\"error\":{\"message\":\"not signed in\"}}"); } catch (InvalidOperationException) { error = true; }
   Check(error, "server errors surfaced without fabricating usage", r);
   var clamp = Usage.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":150,\"windowDurationMins\":15}}}");
   Check(clamp.Primary.Remaining == 0 && clamp.Primary.Label == "15-MINUTE", "percent bounds and variable windows handled", r);
   File.WriteAllLines(report, r);
  }
 }
}
