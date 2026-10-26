using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

[assembly: AssemblyTitle("Dragon Codex Boot")]
[assembly: AssemblyProduct("Dragon Codex Boot")]
[assembly: AssemblyDescription("Community animation launcher for the Codex desktop client")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

namespace DragonCodexBoot {
 public sealed class Config {
  public Config() { AutoReplaceEntrypoints=true; ScanAllLocalDrives=true; }
  public bool AutoReplaceEntrypoints { get; set; }
  public bool ScanAllLocalDrives { get; set; }
  public string DisplayName { get; set; }
  public string Video { get; set; }
  public string AppLaunch { get; set; }
  public bool UseImageWallpaper { get; set; }
  public string ProcessPathContains { get; set; }
  public string[] ProcessNames { get; set; }
  public double HoldAt { get; set; }
  public double TransitionStart { get; set; }
  public double TransitionEnd { get; set; }
  public double MaxWaitSeconds { get; set; }
  public double Volume { get; set; }
  public int PlayerWidth { get; set; }
  public int PlayerHeight { get; set; }
  public bool MatchClientToPlayer { get; set; }
  public List<ScreenFrame> ScreenFrames { get; set; }
 }
 public sealed class ScreenFrame {
  public double Time { get; set; }
  public double X { get; set; }
  public double Y { get; set; }
  public double Width { get; set; }
  public double Height { get; set; }
 }
 [StructLayout(LayoutKind.Sequential)]
 public struct Bounds {
  public int Left,Top,Right,Bottom;
  public int Width { get { return Right-Left; } }
  public int Height { get { return Bottom-Top; } }
  public Bounds(int x,int y,int w,int h) { Left=x;Top=y;Right=x+w;Bottom=y+h; }
 }
 public static class Geometry {
  public static double Clamp(double x) { return Math.Max(0,Math.Min(1,x)); }
  public static double Ease(double x) { x=Clamp(x);return x*x*(3-2*x); }
  public static Bounds Lerp(Bounds a,Bounds b,double t) {
   return new Bounds((int)Math.Round(a.Left+(b.Left-a.Left)*t),(int)Math.Round(a.Top+(b.Top-a.Top)*t),
    (int)Math.Round(a.Width+(b.Width-a.Width)*t),(int)Math.Round(a.Height+(b.Height-a.Height)*t));
  }
  public static Bounds Centered(Bounds monitor,int width,int height) {
   // These are physical pixels, independent of Windows display scaling.
   return new Bounds(monitor.Left+(monitor.Width-width)/2,monitor.Top+(monitor.Height-height)/2,width,height);
  }
  public static Bounds WindowForClient(Bounds window,Bounds client,Bounds desired) {
   return new Bounds(desired.Left-(client.Left-window.Left),desired.Top-(client.Top-window.Top),
    desired.Width+window.Width-client.Width,desired.Height+window.Height-client.Height);
  }
  public static ScreenFrame At(List<ScreenFrame> frames,double t) {
   if(t<=frames[0].Time)return frames[0];
   for(int i=1;i<frames.Count;i++) {
    if(t>frames[i].Time)continue;
    ScreenFrame a=frames[i-1],b=frames[i];
    double u=Clamp((t-a.Time)/(b.Time-a.Time));
    return new ScreenFrame { Time=t,X=a.X+(b.X-a.X)*u,Y=a.Y+(b.Y-a.Y)*u,
     Width=a.Width+(b.Width-a.Width)*u,Height=a.Height+(b.Height-a.Height)*u };
   }
   return frames[frames.Count-1];
  }
  public static Bounds Dest(List<ScreenFrame> frames,double time,int w,int h,double fade) {
   ScreenFrame r=At(frames,time);
   // The video is fitted without distorting its 16:9 character drawing.
   double fit=Math.Min(w/1920.0,h/1080.0);
   double vw=1920*fit,vh=1080*fit,ox=(w-vw)/2,oy=(h-vh)/2;
   Bounds inVideo=new Bounds((int)Math.Round(ox+r.X*vw),(int)Math.Round(oy+r.Y*vh),
    Math.Max(1,(int)Math.Round(r.Width*vw)),Math.Max(1,(int)Math.Round(r.Height*vh)));
   // Remove any letterboxing as the actual software fills the target window.
   return Lerp(inVideo,new Bounds(0,0,w,h),Ease(fade));
  }
 }
 internal static class Native {
  public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
  [StructLayout(LayoutKind.Sequential)] public struct WindowPos {
   public IntPtr Window,InsertAfter; public int X,Y,Width,Height; public uint Flags;
  }
  [StructLayout(LayoutKind.Sequential)] public struct ThumbProperties {
   public uint Flags; public Bounds Destination; public Bounds Source;
   public byte Opacity;
   [MarshalAs(UnmanagedType.Bool)] public bool Visible;
   [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
  }
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr param);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Bounds b);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Bounds b);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Point p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h,uint cmd);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int index);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int how);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h,uint msg,IntPtr w,IntPtr l,uint flags,uint ms,out IntPtr result);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h,uint flags,StringBuilder path,ref int length);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest,IntPtr source,out IntPtr thumb);
  [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
  [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumb,ref ThumbProperties props);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int val,int size);
  public static Bounds ClientBounds(IntPtr h) {
   Bounds r;Point p=new Point();
   if(!GetClientRect(h,out r)||!ClientToScreen(h,ref p))return new Bounds();
   return new Bounds(p.X,p.Y,r.Width,r.Height);
  }
  public static bool Responsive(IntPtr h) {
   IntPtr result;
   return h!=IntPtr.Zero&&SendMessageTimeout(h,0,IntPtr.Zero,IntPtr.Zero,2,80,out result)!=IntPtr.Zero;
  }
  public static bool AlignClient(IntPtr h,Bounds desired) {
   if(IsIconic(h)||IsZoomed(h))ShowWindow(h,9);
   // Measure again after resizing: a restored Chromium window can change frame insets.
   for(int attempt=0;attempt<2;attempt++) {
    Bounds window,client=ClientBounds(h);
    if(!GetWindowRect(h,out window)||client.Width<=0||client.Height<=0)return false;
    Bounds outer=Geometry.WindowForClient(window,client,desired);
    if(!SetWindowPos(h,IntPtr.Zero,outer.Left,outer.Top,outer.Width,outer.Height,0x4|0x10|0x200))return false;
   }
   Bounds actual=ClientBounds(h);
   return Math.Abs(actual.Left-desired.Left)<=2&&Math.Abs(actual.Top-desired.Top)<=2
    &&Math.Abs(actual.Width-desired.Width)<=2&&Math.Abs(actual.Height-desired.Height)<=2;
  }
  public static IntPtr FindApp(Config cfg) {
   HashSet<int> pids=new HashSet<int>();
   foreach(string name in cfg.ProcessNames)foreach(Process p in Process.GetProcessesByName(name)) {
    try {
     string path="";
     IntPtr process=OpenProcess(0x1000,false,p.Id);
     if(process!=IntPtr.Zero) {
      try { StringBuilder buffer=new StringBuilder(4096);int length=buffer.Capacity;if(QueryFullProcessImageName(process,0,buffer,ref length))path=buffer.ToString(); }
      finally { CloseHandle(process); }
     }
     if(path.IndexOf(cfg.ProcessPathContains,StringComparison.OrdinalIgnoreCase)>=0)pids.Add(p.Id);
    } catch {} finally { p.Dispose(); }
   }
   IntPtr found=IntPtr.Zero;
   EnumWindows(delegate(IntPtr h,IntPtr unused) {
    uint pid;GetWindowThreadProcessId(h,out pid);
    if(!pids.Contains((int)pid)||!IsWindowVisible(h)||GetWindow(h,4)!=IntPtr.Zero||(GetWindowLong(h,-20)&0x80)!=0)return true;
    int cloaked;
    if(DwmGetWindowAttribute(h,14,out cloaked,4)==0&&cloaked!=0)return true;
    Bounds b=ClientBounds(h);
    if(b.Width<450||b.Height<280)return true;
    found=h;return false; // Frontmost eligible main window; no title or screenshot is recorded.
   },IntPtr.Zero);
   return found;
  }
 }
 public sealed class BootWindow:Window {
  private readonly Config cfg;
  private readonly string root;
  private readonly MediaElement media;
  private readonly TextBlock status;
  private readonly Button retry;
  private readonly DispatcherTimer timer;
  private readonly Stopwatch wall=new Stopwatch();
  private IntPtr host,target,thumbnail,stableHandle,alignedHandle;
  private Bounds initialBounds;
  private double stableSince,lastProbe=-1;
  private bool ready,opened,holding,closing,startedFade,thumbnailFailed;
  private bool timeoutReported;
  private Process wallpaperProcess;
  private readonly object logSync=new object();
  private bool wallpaperAttempted,wallpaperResolved,wallpaperFallback;
  private int launchAttempts;
  private double lastLaunch;
  private double endedAt=-1;
  public BootWindow(Config c,string baseDir) {
   cfg=c;root=baseDir;Title=String.IsNullOrWhiteSpace(c.DisplayName)?"Dragon Codex Boot":c.DisplayName;
   WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
   AllowsTransparency=false;Topmost=true;Background=new SolidColorBrush(Color.FromRgb(250,247,253));
   target=Native.FindApp(cfg);
   System.Windows.Forms.Screen screen=target!=IntPtr.Zero?System.Windows.Forms.Screen.FromHandle(target)
    :System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
   System.Drawing.Rectangle monitor=screen.Bounds;
   initialBounds=Geometry.Centered(new Bounds(monitor.Left,monitor.Top,monitor.Width,monitor.Height),cfg.PlayerWidth,cfg.PlayerHeight);
   Width=cfg.PlayerWidth;Height=cfg.PlayerHeight;WindowStartupLocation=WindowStartupLocation.Manual;
   Grid grid=new Grid();
   media=new MediaElement { LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,
    Stretch=Stretch.Uniform,ScrubbingEnabled=true,Volume=Geometry.Clamp(cfg.Volume) };
   grid.Children.Add(media);
   status=new TextBlock { HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,
    Margin=new Thickness(20,20,20,38),FontSize=16,Foreground=new SolidColorBrush(Color.FromRgb(115,91,144)),
    Visibility=Visibility.Collapsed };
   grid.Children.Add(status);
   Button skip=new Button { Content="跳过  Esc",HorizontalAlignment=HorizontalAlignment.Right,
    VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(16),Padding=new Thickness(11,5,11,5),
    Background=Brushes.White,Foreground=new SolidColorBrush(Color.FromRgb(118,97,135)),Opacity=.45 };
   skip.Click+=delegate { HandOff(); };
   skip.MouseEnter+=delegate { skip.Opacity=1; };skip.MouseLeave+=delegate { skip.Opacity=.45; };
   grid.Children.Add(skip);Content=grid;
   retry=new Button { Content="重新打开 Codex",HorizontalAlignment=HorizontalAlignment.Center,
    VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(20,20,20,72),Padding=new Thickness(18,8,18,8),
    Background=Brushes.White,Foreground=new SolidColorBrush(Color.FromRgb(118,97,135)),Visibility=Visibility.Collapsed };
   retry.Click+=delegate {
    if(cfg.UseImageWallpaper) { wallpaperAttempted=false;wallpaperResolved=false;wallpaperFallback=false;launchAttempts=0; }
    LaunchApp();
   };
   grid.Children.Add(retry);
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape) { e.Handled=true;HandOff(); } };
   SourceInitialized+=delegate {
    host=new WindowInteropHelper(this).Handle;
    HwndSource.FromHwnd(host).AddHook(FixedBoundsHook);
    Native.SetWindowPos(host,new IntPtr(-1),initialBounds.Left,initialBounds.Top,initialBounds.Width,initialBounds.Height,0x10);
   };
   Loaded+=delegate {
    wall.Start();
    if(target==IntPtr.Zero||cfg.UseImageWallpaper)LaunchApp();
    media.Source=new Uri(Path.GetFullPath(Path.Combine(root,cfg.Video)));
    media.Play();timer.Start();Log("started fixed-player="+initialBounds.Width+"x"+initialBounds.Height
     +" at "+initialBounds.Left+","+initialBounds.Top);
   };
   media.MediaOpened+=delegate {
    opened=true;Bounds actual=Native.ClientBounds(host);
    Log("media-opened player-client="+actual.Width+"x"+actual.Height+" at "+actual.Left+","+actual.Top);
    Native.SetForegroundWindow(host);
   };
   media.MediaFailed+=delegate(object sender,ExceptionRoutedEventArgs e) {
    Log("media-failed: "+e.ErrorException.Message);status.Text="视频无法播放，正在转入Codex";status.Visibility=Visibility.Visible;
    HandOff();
   };
   media.MediaEnded+=delegate { endedAt=wall.Elapsed.TotalSeconds;HandOff(); };
   timer=new DispatcherTimer(DispatcherPriority.Render);timer.Interval=TimeSpan.FromMilliseconds(16);
   timer.Tick+=Tick;
   Closed+=delegate { timer.Stop();media.Stop();DropThumbnail(); };
  }
  private IntPtr FixedBoundsHook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
   if(message==0x0046&&!closing&&lParam!=IntPtr.Zero) {
    Native.WindowPos pos=(Native.WindowPos)Marshal.PtrToStructure(lParam,typeof(Native.WindowPos));
    pos.X=initialBounds.Left;pos.Y=initialBounds.Top;pos.Width=initialBounds.Width;pos.Height=initialBounds.Height;
    pos.Flags&=~(uint)0x3; // Permit the fixed coordinates even for a DPI or layout size request.
    Marshal.StructureToPtr(pos,lParam,false);handled=true;
   }
   return IntPtr.Zero;
  }
  private void AlignTarget() {
   if(!cfg.MatchClientToPlayer||target==IntPtr.Zero||alignedHandle==target)return;
   bool aligned=Native.AlignClient(target,initialBounds);alignedHandle=target;
   Log(aligned?"real-window-aligned-to-fixed-player":"real-window-alignment-inexact");
   Native.SetForegroundWindow(host);
  }
  private void LaunchApp() {
   try {
    if(cfg.UseImageWallpaper&&!wallpaperAttempted) {
     wallpaperAttempted=true;
     string script=Path.Combine(root,"skin-runtime","scripts","start-dream-skin.ps1");
     if(!File.Exists(script)) { Log("wallpaper-runtime-missing");throw new FileNotFoundException("动态壁纸运行组件不存在",script); }
     else {
      string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
      wallpaperProcess=Process.Start(new ProcessStartInfo(powershell,"-NoProfile -ExecutionPolicy RemoteSigned -File \""+script+"\" -RestartExisting") {
       UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(script),WindowStyle=ProcessWindowStyle.Hidden,
       RedirectStandardOutput=true,RedirectStandardError=true
      });
      wallpaperProcess.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e) { if(e.Data!=null)Log("wallpaper-out: "+e.Data); };
      wallpaperProcess.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e) { if(e.Data!=null)Log("wallpaper-error: "+e.Data); };
      wallpaperProcess.BeginOutputReadLine();wallpaperProcess.BeginErrorReadLine();
      lastLaunch=wall.Elapsed.TotalSeconds;launchAttempts++;
      Log("wallpaper-start-requested");
      return;
     }
    }
    // Shell launches the packaged desktop app; its CLI helper is never used as a UI executable.
    Process.Start(new ProcessStartInfo("explorer.exe",cfg.AppLaunch) { UseShellExecute=true });
    lastLaunch=wall.Elapsed.TotalSeconds;launchAttempts++;
    Log("app-launch-requested attempt="+launchAttempts);
    if(launchAttempts>1) { status.Text="已再次请求打开 Codex，正在等待窗口…";status.Visibility=Visibility.Visible; }
   } catch(Exception e) {
    Log("app-launch-failed: "+e.Message);
    wallpaperResolved=true;wallpaperFallback=true;
    status.Text=cfg.UseImageWallpaper?"动态壁纸启动失败，请检查组件并重试":"无法自动打开 Codex，请检查安装后重试";
    status.Visibility=Visibility.Visible;retry.Visibility=Visibility.Visible;
   }
  }
  private void Probe() {
   double now=wall.Elapsed.TotalSeconds;
   if(now-lastProbe<.25)return;lastProbe=now;
   if(cfg.UseImageWallpaper&&!wallpaperResolved) {
    if(wallpaperProcess!=null&&wallpaperProcess.HasExited) {
     int code=wallpaperProcess.ExitCode;
     wallpaperProcess.Dispose();wallpaperProcess=null;wallpaperResolved=true;
     Log("wallpaper-start-exit="+code);
     if(code!=0) {
      wallpaperFallback=true;
      status.Text="动态壁纸启动失败（代码 "+code+"），请重试或检查运行日志";
      status.Visibility=Visibility.Visible;retry.Visibility=Visibility.Visible;
     }
    } else { ready=false;return; }
   }
   if(cfg.UseImageWallpaper&&wallpaperFallback) { ready=false;return; }
   IntPtr candidate=Native.FindApp(cfg);
   if(candidate==IntPtr.Zero||!Native.Responsive(candidate)) { ready=false;stableHandle=IntPtr.Zero;return; }
   if(Native.IsIconic(candidate))Native.ShowWindow(candidate,9);
   if(candidate!=stableHandle) { stableHandle=candidate;stableSince=now;ready=false; }
   else ready=now-stableSince>=.6;
   target=candidate;
   // Visible/responsive/stable is a window heuristic, not an official Codex content-ready event.
  }
  private void RegisterThumbnail() {
   if(thumbnail!=IntPtr.Zero||thumbnailFailed)return;
   int hr=Native.DwmRegisterThumbnail(host,target,out thumbnail);
   if(hr!=0) { thumbnail=IntPtr.Zero;thumbnailFailed=true;Log("dwm-unavailable: "+hr); }
   else Log("live-window-connected");
  }
  private void DropThumbnail() {
   if(thumbnail!=IntPtr.Zero) { Native.DwmUnregisterThumbnail(thumbnail);thumbnail=IntPtr.Zero; }
  }
  private void Tick(object sender,EventArgs e) {
   if(closing)return;
   Probe();
   if(!cfg.UseImageWallpaper&&!ready&&launchAttempts>0&&launchAttempts<3
    &&wall.Elapsed.TotalSeconds-lastLaunch>=10)LaunchApp();
   if(!ready&&wall.Elapsed.TotalSeconds>cfg.MaxWaitSeconds+20&&!timeoutReported) {
    timeoutReported=true;Log("wait-timeout-no-window");
    status.Text="未检测到 Codex 窗口。可重试启动，或关闭此窗口查看日志。";
    status.Visibility=Visibility.Visible;retry.Visibility=Visibility.Visible;
   }
   if(!opened) {
    if(wall.Elapsed.TotalSeconds>12&&!timeoutReported) { timeoutReported=true;Log("media-open-timeout");status.Text="视频无法播放，正在等待 Codex 窗口…";status.Visibility=Visibility.Visible;retry.Visibility=Visibility.Visible; }
    return;
   }
   double t=media.Position.TotalSeconds;
   if(!ready&&!startedFade&&t>=cfg.HoldAt) {
    if(!holding) { media.Pause();media.Position=TimeSpan.FromSeconds(cfg.HoldAt);holding=true;Log("waiting-for-window"); }
    if(!timeoutReported)status.Text="Codex 正在启动…  Esc 可在窗口出现后跳过动画";
    status.Visibility=Visibility.Visible;
    return;
   }
   if(holding&&ready) {
    holding=false;status.Visibility=Visibility.Collapsed;retry.Visibility=Visibility.Collapsed;media.Play();Log("window-available");
   }
   if(ready&&t>=cfg.HoldAt)AlignTarget();
   if(t>=cfg.TransitionStart&&ready) {
    startedFade=true;RegisterThumbnail();
    double a=Geometry.Ease((t-cfg.TransitionStart)/(cfg.TransitionEnd-cfg.TransitionStart));
    Bounds targetBounds=Native.ClientBounds(target);
    if(targetBounds.Width<450) { HandOff();return; }
    Bounds hostBounds=initialBounds;
    if(thumbnail!=IntPtr.Zero) {
     Native.ThumbProperties props=new Native.ThumbProperties {
      Flags=1|4|8|16,Destination=Geometry.Dest(cfg.ScreenFrames,t,hostBounds.Width,hostBounds.Height,a),
      Opacity=(byte)Math.Round(255*a),Visible=true,ClientOnly=true
     };
     int hr=Native.DwmUpdateThumbnailProperties(thumbnail,ref props);
     if(hr!=0) { Log("dwm-update-failed: "+hr);DropThumbnail();thumbnailFailed=true; }
    }
    // Safe fallback gives a crossfade to the actual window if live thumbnails are unavailable.
    if(thumbnailFailed)Opacity=1-a;
   }
   if(endedAt>=0&&wall.Elapsed.TotalSeconds-endedAt>.25)HandOff();
  }
  private void HandOff() {
   if(closing)return;
   if(cfg.UseImageWallpaper&&(wallpaperFallback||!wallpaperResolved)) { Log("handoff-blocked-wallpaper-not-ready");return; }
   IntPtr app=Native.FindApp(cfg);
   if(app!=IntPtr.Zero) {
    closing=true;timer.Stop();DropThumbnail();
    if(Native.IsIconic(app))Native.ShowWindow(app,9);
    // Removes the covering window and hands input to the real application.
    Close();Native.SetForegroundWindow(app);Log("handed-off-to-real-window");
   } else {
    Log("handoff-requested-without-window");
    status.Text="Codex 窗口尚未出现，正在重试启动…";status.Visibility=Visibility.Visible;
    retry.Visibility=Visibility.Visible;
    if(wall.Elapsed.TotalSeconds-lastLaunch>=3)LaunchApp();
   }
  }
  private void Log(string s) {
   try {
    lock(logSync) {
     Directory.CreateDirectory(Path.Combine(root,"logs"));
     string line=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+s+Environment.NewLine;
     File.AppendAllText(Path.Combine(root,"logs","launcher.log"),line);
    }
   } catch {}
  }
 }
 public static class Entry {
  private static int Integrate(string root,string action,bool wait) {
   string script=Path.Combine(root,"scripts","integrate-entrypoints.ps1");
   if(!File.Exists(script))throw new FileNotFoundException("启动入口脚本缺失，请完整解压程序包。",script);
   string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
   // Paths are local Windows filenames (quotes cannot occur); never concatenate configuration into shell commands.
   ProcessStartInfo info=new ProcessStartInfo(powershell,"-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" -Action "+action+" -AppRoot \""+root.TrimEnd('\\','/')+"\"") {
    UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,WindowStyle=ProcessWindowStyle.Hidden
   };
   using(Process process=Process.Start(info)) {
    if(wait) { process.WaitForExit();return process.ExitCode; }
   }
   return 0;
  }
  public static Config ReadConfig(string root) {
   Config c=new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(Path.Combine(root,"launcher.json")));
   ValidateConfig(c);
   return c;
  }
  private static bool Finite(double n) { return !Double.IsNaN(n)&&!Double.IsInfinity(n); }
  public static void ValidateConfig(Config c) {
   if(c==null||String.IsNullOrEmpty(c.Video)||String.IsNullOrEmpty(c.AppLaunch)||String.IsNullOrEmpty(c.ProcessPathContains)
    ||c.ProcessNames==null||c.ProcessNames.Length==0||c.ScreenFrames==null||c.ScreenFrames.Count<2)throw new ArgumentException("启动器配置不完整");
   string appPrefix="shell:AppsFolder\\";
   if(!c.AppLaunch.StartsWith(appPrefix,StringComparison.OrdinalIgnoreCase)
    ||!System.Text.RegularExpressions.Regex.IsMatch(c.AppLaunch.Substring(appPrefix.Length),@"^[A-Za-z0-9_.-]+![A-Za-z0-9_.-]+$"))
    throw new ArgumentException("AppLaunch必须是已安装应用的shell:AppsFolder标识。");
   if(String.IsNullOrWhiteSpace(c.Video)||String.IsNullOrWhiteSpace(c.ProcessPathContains)
    ||c.ProcessNames.Any(n=>String.IsNullOrWhiteSpace(n)||!System.Text.RegularExpressions.Regex.IsMatch(n,@"^[A-Za-z0-9_.-]+$")))
    throw new ArgumentException("视频路径或进程匹配配置无效。");
   if(!Finite(c.HoldAt)||!Finite(c.TransitionStart)||!Finite(c.TransitionEnd)||!Finite(c.MaxWaitSeconds)||!Finite(c.Volume)
    ||c.Volume<0||c.Volume>1)throw new ArgumentException("时间和音量必须是有限数值，音量范围为0至1。");
   if(c.HoldAt<=0||c.TransitionStart<=c.HoldAt||c.TransitionEnd<=c.TransitionStart||c.MaxWaitSeconds<10)throw new Exception("启动时间配置不正确");
   if(c.PlayerWidth<450||c.PlayerHeight<280||c.PlayerWidth>7680||c.PlayerHeight>4320)throw new Exception("播放窗口尺寸配置不正确");
   for(int i=0;i<c.ScreenFrames.Count;i++) {
    ScreenFrame f=c.ScreenFrames[i];
    if(f==null||!Finite(f.Time)||!Finite(f.X)||!Finite(f.Y)||!Finite(f.Width)||!Finite(f.Height)||f.Time<0)
     throw new ArgumentException("巨幕关键帧必须使用非负时间和有限坐标。");
    if(f.Width<=0||f.Height<=0||f.X<0||f.Y<0||f.X+f.Width>1.001||f.Y+f.Height>1.001
     ||(i>0&&f.Time<=c.ScreenFrames[i-1].Time))throw new Exception("巨幕位置配置不正确");
   }
  }
  private static void Assert(bool b,string why) { if(!b)throw new Exception("Self-test: "+why); }
  private static int SelfTest(string root) {
   Config c=ReadConfig(root);
   Assert(Geometry.Ease(0)==0&&Geometry.Ease(1)==1,"fade endpoints");
   int cases=0;
   foreach(int[] size in new int[][] {new int[]{1920,1080},new int[]{1536,864},new int[]{1200,900},new int[]{2560,1440},new int[]{900,1400}}) {
    for(int n=0;n<=120;n++) {
     double t=c.TransitionStart+(c.TransitionEnd-c.TransitionStart)*n/120.0,a=Geometry.Ease(n/120.0);
     Bounds b=Geometry.Dest(c.ScreenFrames,t,size[0],size[1],a);
     Assert(b.Width>0&&b.Height>0&&b.Left>=0&&b.Top>=0&&b.Right<=size[0]+1&&b.Bottom<=size[1]+1,"viewport "+n);cases++;
    }
    Bounds last=Geometry.Dest(c.ScreenFrames,c.TransitionEnd,size[0],size[1],1);
    Assert(last.Left==0&&last.Top==0&&last.Right==size[0]&&last.Bottom==size[1],"pixel-aligned final frame");
   }
   Bounds negativeMonitor=new Bounds(-1920,0,1920,1080),target=new Bounds(-1500,100,1200,800);
   Bounds end=Geometry.Lerp(negativeMonitor,target,1);Assert(end.Left==target.Left&&end.Right==target.Right,"negative monitor");
   int placementCases=0;
   foreach(Bounds monitor in new Bounds[] {new Bounds(0,0,1920,1080),new Bounds(0,0,2560,1440),
    new Bounds(0,0,3840,2160),new Bounds(-2560,0,2560,1440),new Bounds(1920,-1440,2560,1440),new Bounds(0,0,1366,768)}) {
    Bounds fixedPlayer=Geometry.Centered(monitor,c.PlayerWidth,c.PlayerHeight);
    Assert(fixedPlayer.Width==c.PlayerWidth&&fixedPlayer.Height==c.PlayerHeight,"fixed physical video size");
    Assert(Math.Abs(fixedPlayer.Left*2+fixedPlayer.Width-monitor.Left*2-monitor.Width)<=1
     &&Math.Abs(fixedPlayer.Top*2+fixedPlayer.Height-monitor.Top*2-monitor.Height)<=1,"centered monitor");placementCases++;
    Bounds oldWindow=new Bounds(monitor.Left+10,monitor.Top+20,1216,816),oldClient=new Bounds(monitor.Left+18,monitor.Top+28,1200,800);
    Bounds aligned=Geometry.WindowForClient(oldWindow,oldClient,fixedPlayer);
    Assert(aligned.Left+8==fixedPlayer.Left&&aligned.Top+8==fixedPlayer.Top
     &&aligned.Width-16==fixedPlayer.Width&&aligned.Height-16==fixedPlayer.Height,"client frame insets");placementCases++;
   }
   Directory.CreateDirectory(Path.Combine(root,"qa"));
   File.WriteAllText(Path.Combine(root,"qa","self-test.json"),new JavaScriptSerializer().Serialize(new {
    ok=true,geometryCases=cases,fixedPlacementCases=placementCases,playerWidth=c.PlayerWidth,playerHeight=c.PlayerHeight,
    videoExists=File.Exists(Path.GetFullPath(Path.Combine(root,c.Video))),nativeUiTested=false,
    scope="Configuration and geometry only. No desktop window or media is opened."
   }));
   return 0;
  }
  [STAThread]
  public static int Main(string[] args) {
   string root=AppDomain.CurrentDomain.BaseDirectory;
   try {
    // Headless checks exit before any desktop API or media window is constructed.
    if(args.Contains("--self-test"))return SelfTest(root);
    if(args.Contains("--restore-entrypoints"))return Integrate(root,"Restore",true);
    if(args.Contains("--integrate-entrypoints"))return Integrate(root,"Install",true);
    bool owns;using(Mutex mutex=new Mutex(true,"Local\\DragonCodexBoot",out owns)) {
     if(!owns)return 0;
     Config c=ReadConfig(root);
     if(!File.Exists(Path.GetFullPath(Path.Combine(root,c.Video))))throw new Exception("未找到启动视频。请先运行scripts/Import-Animation.ps1导入自己的MP4，或修改launcher.json的Video路径。");
     if(c.AutoReplaceEntrypoints) {
      try { Integrate(root,"Auto",false); }
      catch(Exception e) {
       try { Directory.CreateDirectory(Path.Combine(root,"logs"));File.AppendAllText(Path.Combine(root,"logs","launcher.log"),"entrypoint-integration: "+e.Message+Environment.NewLine); } catch {}
      }
     }
     Application app=new Application();
     app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) {
      e.Handled=true;MessageBox.Show("启动器已退出："+e.Exception.Message,"Codex启动");app.Shutdown(1);
     };
     return app.Run(new BootWindow(c,root));
    }
   } catch(Exception e) {
    if(args.Contains("--self-test")) { File.WriteAllText(Path.Combine(root,"self-test-error.txt"),e.ToString());return 1; }
    MessageBox.Show(e.Message,"Codex启动");return 1;
   }
  }
 }
}
