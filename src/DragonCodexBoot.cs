using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
[assembly: AssemblyVersion("0.2.7.0")]
[assembly: AssemblyFileVersion("0.2.7.0")]

namespace DragonCodexBoot {
 public sealed class Config {
  public Config() { AutoReplaceEntrypoints=true; ScanAllLocalDrives=true; WaitForContent=true; }
  public bool WaitForContent { get; set; }
  public bool AutoReplaceEntrypoints { get; set; }
  public bool ScanAllLocalDrives { get; set; }
  public string DisplayName { get; set; }
  public string Video { get; set; }
  public string AppLaunch { get; set; }
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
  public bool BindToAppWindow { get; set; }
  // Empty means primary display; never depend on cursor or Explorer location.
  public string TargetDisplay { get; set; }
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
  public static Bounds Fit(Bounds area,int width,int height) {
   if(area.Width<=0||area.Height<=0||width<=0||height<=0)throw new ArgumentException("Invalid placement area");
   double scale=Math.Min(1,Math.Min((double)area.Width/width,(double)area.Height/height));
   return Centered(area,Math.Max(1,(int)Math.Floor(width*scale)),Math.Max(1,(int)Math.Floor(height*scale)));
  }
  public static Bounds Contain(Bounds desired,Bounds area) {
   Bounds fitted=Fit(area,desired.Width,desired.Height);
   return new Bounds(Math.Max(area.Left,Math.Min(desired.Left,area.Right-fitted.Width)),
    Math.Max(area.Top,Math.Min(desired.Top,area.Bottom-fitted.Height)),fitted.Width,fitted.Height);
  }
  public static int BestMonitor(Bounds window,Bounds[] monitors,int primary) {
   long best=0;int selected=primary;
   for(int i=0;i<monitors.Length;i++) {
    long overlap=(long)Math.Max(0,Math.Min(window.Right,monitors[i].Right)-Math.Max(window.Left,monitors[i].Left))
     *Math.Max(0,Math.Min(window.Bottom,monitors[i].Bottom)-Math.Max(window.Top,monitors[i].Top));
    if(overlap>best) {best=overlap;selected=i;}
   }
   return selected;
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
 public sealed class ContentEvidence {
  public bool HasContent;
  public string Reason;
 }
 public static class ContentReadiness {
  // Reject a flat light/dark splash with its logo confined to the center.
  // This is rendering evidence, not an official application-ready signal.
  public static ContentEvidence Analyze(int[] rgb,int width,int height) {
   if(width<40||height<30||rgb==null||rgb.Length!=width*height)return new ContentEvidence {Reason="invalid-capture"};
   int[] backgrounds=new int[5];
   int[] xs={5,95,5,95,90},ys={12,12,90,90,80};
   for(int i=0;i<5;i++)backgrounds[i]=rgb[(height*ys[i]/100)*width+width*xs[i]/100];
   int[] channels=new int[3];
   for(int channel=0;channel<3;channel++) {
    int shift=channel*8;int[] values=backgrounds.Select(p=>(p>>shift)&255).OrderBy(v=>v).ToArray();channels[channel]=values[2];
   }
   int ink=0,total=0;int[] regions=new int[12];
   for(int y=height/10;y<height*95/100;y++)for(int x=width/25;x<width*96/100;x++) {
    if(x>=width*30/100&&x<=width*70/100&&y>=height/4&&y<=height*75/100)continue;
    int p=rgb[y*width+x];total++;
    int delta=Math.Max(Math.Abs((p&255)-channels[0]),Math.Max(Math.Abs(((p>>8)&255)-channels[1]),Math.Abs(((p>>16)&255)-channels[2])));
    if(delta>24) {ink++;regions[Math.Min(2,y*3/height)*4+Math.Min(3,x*4/width)]++;}
   }
   int active=regions.Count(n=>n>=8);double fraction=total==0?0:(double)ink/total;
   bool content=fraction>=.01&&active>=3;
   return new ContentEvidence {HasContent=content,Reason=(content?"distributed-rendered-content":"startup-logo-or-flat-frame")+" outer-ink="+fraction.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" regions="+active};
  }
 }
 public sealed class ContentGate {
  public bool Presented {get;private set;}
  public void MarkPresented() {Presented=true;}
  private int consecutive;
  private double last=-100;
  public void Reset() {consecutive=0;last=-100;Presented=false;}
  public void Observe(bool hasContent,double now) {
   if(!hasContent||now-last>1.2)consecutive=0;
   if(hasContent)consecutive++;last=now;
  }
  public bool Ready(double now) {return consecutive>=2&&now-last<=1.2;}
  public static bool AllowHandoff(bool verified,bool userRequested,double waiting) {
   return verified||userRequested||waiting>=3;
  }
 }
 internal static class Native {
  [StructLayout(LayoutKind.Sequential)] private struct Placement {
   public int Length,Flags,Show;public Point Min,Max;public Bounds Normal;
  }
  [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr h,ref Placement placement);
  [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
  public static ContentEvidence CaptureContent(IntPtr h) {
   try {
    SetThreadDpiAwarenessContext(new IntPtr(-4));
    Bounds b=ClientBounds(h);
    if(!IsWindow(h)||b.Width<40||b.Height<30||(long)b.Width*b.Height>33554432)return new ContentEvidence {Reason="invalid-window-size"};
    using(var bitmap=new System.Drawing.Bitmap(b.Width,b.Height,System.Drawing.Imaging.PixelFormat.Format32bppRgb)) {
     using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) {
      graphics.Clear(System.Drawing.Color.Black);IntPtr dc=graphics.GetHdc();bool ok;
      try {ok=PrintWindow(h,dc,3);} finally {graphics.ReleaseHdc(dc);}
      if(!ok)return new ContentEvidence {Reason="capture-unavailable"};
     }
     using(var small=new System.Drawing.Bitmap(bitmap,160,90)) {
      int[] pixels=new int[160*90];for(int y=0;y<90;y++)for(int x=0;x<160;x++)pixels[y*160+x]=small.GetPixel(x,y).ToArgb();
      return ContentReadiness.Analyze(pixels,160,90);
     }
    }
   } catch(Exception e) {return new ContentEvidence {Reason="capture-error "+e.GetType().Name};}
  }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct MonitorInfo {
   public int Size;public Bounds Monitor,Work;public uint Flags;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device;
  }
  public delegate bool MonitorProc(IntPtr monitor,IntPtr dc,IntPtr rect,IntPtr data);
  [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorProc proc,IntPtr data);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr h,ref MonitorInfo info);
  [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr h,int type,out uint x,out uint y);
  public static List<MonitorInfo> Monitors() {
   var result=new List<MonitorInfo>();
   EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr h,IntPtr dc,IntPtr r,IntPtr data) {
    var info=new MonitorInfo { Size=Marshal.SizeOf(typeof(MonitorInfo)) };
    if(GetMonitorInfo(h,ref info))result.Add(info);return true;
   },IntPtr.Zero);return result;
  }
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern int GetPackageFamilyName(IntPtr process,ref uint length,StringBuilder name);
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
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h,uint msg,IntPtr w,IntPtr l,uint flags,uint ms,out IntPtr result);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h,uint flags,StringBuilder path,ref int length);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest,IntPtr source,out IntPtr thumb);
  [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
  [DllImport("dwmapi.dll")] public static extern int DwmFlush();
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
  public static string Identity(int pid,Config cfg) {
   IntPtr process=OpenProcess(0x1000,false,pid);
   if(process==IntPtr.Zero)return null;
   try {
    uint length=512;StringBuilder family=new StringBuilder((int)length);
    string expected=cfg.AppLaunch.Substring("shell:AppsFolder\\".Length).Split('!')[0];
    if(GetPackageFamilyName(process,ref length,family)!=0||!String.Equals(family.ToString(),expected,StringComparison.OrdinalIgnoreCase))return null;
    StringBuilder path=new StringBuilder(4096);int size=path.Capacity;
    if(!QueryFullProcessImageName(process,0,path,ref size))return null;
    return family+" path="+path;
   } finally { CloseHandle(process); }
  }
  public static string Describe(IntPtr h,Config cfg) {
   uint pid;GetWindowThreadProcessId(h,out pid);Bounds b=ClientBounds(h);
   return "hwnd=0x"+h.ToInt64().ToString("X")+" pid="+pid+" identity="+Identity((int)pid,cfg)+" client="+b.Left+","+b.Top+","+b.Width+","+b.Height;
  }
  public static string WindowIdentity(IntPtr h,Config cfg) {
   if(h==IntPtr.Zero)return null;
   uint pid;GetWindowThreadProcessId(h,out pid);
   string identity=Identity((int)pid,cfg);if(identity==null)return null;
   try { using(var p=Process.GetProcessById((int)pid))return h.ToInt64()+":"+pid+":"+p.StartTime.ToUniversalTime().Ticks+":"+identity; }
   catch { return null; }
  }
  public static bool Eligible(IntPtr h,Config cfg) {
   if(h==IntPtr.Zero||!IsWindow(h)||!IsWindowVisible(h)||GetWindow(h,4)!=IntPtr.Zero||(GetWindowLong(h,-20)&0x08000080)!=0)return false;
   uint pid;GetWindowThreadProcessId(h,out pid);
   if(Identity((int)pid,cfg)==null)return false;
   int cloaked;if(DwmGetWindowAttribute(h,14,out cloaked,4)!=0||cloaked!=0)return false;
   if(IsIconic(h)) {
    var placement=new Placement {Length=Marshal.SizeOf(typeof(Placement))};
    return GetWindowPlacement(h,ref placement)&&placement.Normal.Width>=450&&placement.Normal.Height>=280;
   }
   Bounds b=ClientBounds(h);return b.Width>=450&&b.Height>=280;
  }
  public static IntPtr FindApp(Config cfg) {
   HashSet<int> pids=new HashSet<int>();
   int session;using(var current=Process.GetCurrentProcess())session=current.SessionId;
   foreach(string name in cfg.ProcessNames)foreach(Process p in Process.GetProcessesByName(name)) {
    try {
     if(p.SessionId==session&&Identity(p.Id,cfg)!=null)pids.Add(p.Id);
    } catch {} finally { p.Dispose(); }
   }
   IntPtr found=IntPtr.Zero;
   EnumWindows(delegate(IntPtr h,IntPtr unused) {
    uint pid;GetWindowThreadProcessId(h,out pid);
    if(!pids.Contains((int)pid)||!Eligible(h,cfg))return true;
    found=h;return false; // Frontmost eligible main window; no title or screenshot is recorded.
   },IntPtr.Zero);
   return found;
  }
 }
 // Monotonic clock supplied by the host; test without launching the desktop app.
 public sealed class WindowLifecycle {
  private string identity;
  private double since;
  public bool LaunchRequested { get; private set; }
  public bool Ready { get; private set; }
  private double lastRecovery=-100;
  private int recoveries;
  public bool RequestRecovery(bool existing,double now) {
   if(existing||!LaunchRequested||now<10||now-lastRecovery<10||recoveries>=2)return false;
   lastRecovery=now;recoveries++;return true;
  }
  public bool RequestLaunch(bool existing) {
   if(existing||LaunchRequested)return false;
   LaunchRequested=true;return true;
  }
  public void Observe(string candidate,bool responsive,double now) {
   if(candidate==null||!responsive) { identity=null;Ready=false;return; }
   if(candidate!=identity) { identity=candidate;since=now;Ready=false; }
   else Ready=now-since>=.6;
  }
  public static bool CanBind(bool enabled,bool stable) { return enabled&&stable; }
  public bool Expired(double now,double limit) { return now>=limit; }
  public static bool PrematureEnd(double duration,double elapsed,double holdAt) {
   return duration>1&&elapsed<Math.Min(duration,holdAt)-.5;
  }
 }
 public sealed class BootWindow:Window {
  private readonly Config cfg;
  private readonly Func<Config,IntPtr> findApp;
  private readonly Action<Config> launchOverride;
  private readonly Func<IntPtr,ContentEvidence> captureContent;
  private readonly Func<IntPtr,bool> activationOverride;
  private readonly string root;
  private readonly MediaElement media;
  private readonly TextBlock status;
  private readonly DispatcherTimer timer;
  private readonly Stopwatch wall=new Stopwatch();
  private IntPtr host,target,thumbnail,alignedHandle;
  private Bounds initialBounds;
  private double lastProbe=-1;
  private bool ready,opened,holding,closing,startedFade,thumbnailFailed;
  private double endedAt=-1;
  private readonly WindowLifecycle lifecycle=new WindowLifecycle();
  private bool handoffRequested,failed;
  private double failedAt;
  private string displayName;
  private string targetIdentity;
  private double lastPosition,progressAt;
  private double playbackStarted;
  private int playbackRetries;
  private int alignmentAttempts;
  private double lastAlignment=-1;
  private string autoDisplay;
  private double activationSince=-1,lastActivation=-1;
  private bool existingAtStart;
  private readonly ContentGate contentGate=new ContentGate();
  private Task<ContentEvidence> contentProbe;
  private string contentProbeIdentity;
  private double nextContentProbe;
  private readonly Button skip;
  private bool userHandoff,activationNeedsClick,advisoryLogged;
  private double handoffStarted=-1;
  private bool CanHandoff {get {return contentGate.Presented||ContentGate.AllowHandoff(ContentAllowed,userHandoff,handoffStarted<0?0:wall.Elapsed.TotalSeconds-handoffStarted);}}
  private bool ContentAllowed {get {return !cfg.WaitForContent||contentGate.Ready(wall.Elapsed.TotalSeconds);}}
  private void ProbeContent() {
   if(!cfg.WaitForContent||userHandoff||(handoffStarted>=0&&wall.Elapsed.TotalSeconds-handoffStarted>=3))return;
   double now=wall.Elapsed.TotalSeconds;
   if(contentProbe!=null) {
    if(!contentProbe.IsCompleted)return;
    ContentEvidence evidence;
    try {evidence=contentProbe.Result;} catch {evidence=new ContentEvidence {Reason="capture-task-failed"};}
    contentProbe=null;nextContentProbe=now+.35;
    if(contentProbeIdentity==targetIdentity) {
     contentGate.Observe(evidence.HasContent,now);
     Log("content-probe ready="+ContentAllowed+" "+evidence.Reason);
     if(!evidence.HasContent&&!contentGate.Presented) {DropThumbnail();startedFade=false;}
    }
   }
   if(!ready||target==IntPtr.Zero||now<nextContentProbe)return;
   IntPtr candidate=target;contentProbeIdentity=targetIdentity;
   // PrintWindow is synchronous; never run capture on the WPF dispatcher.
   contentProbe=Task.Factory.StartNew(delegate {return captureContent(candidate);});
  }
  private void BindWindow() {
   if(!WindowLifecycle.CanBind(cfg.BindToAppWindow,ready)||!Native.Eligible(target,cfg))return;
   if(!existingAtStart&&alignedHandle!=target) {
    if(!Native.AlignClient(target,initialBounds)) {Log("binding-failed cold-align");return;}
    alignedHandle=target;Log("cold-window-bound-to-player "+Native.Describe(target,cfg));
   }
   Bounds client=Native.ClientBounds(target);
   var screens=Native.Monitors();
   int index=Geometry.BestMonitor(client,screens.Select(s=>s.Monitor).ToArray(),screens.FindIndex(s=>(s.Flags&1)!=0));
   Bounds desired=Geometry.Contain(client,screens[index].Work);
   if(client.Left!=desired.Left||client.Top!=desired.Top||client.Width!=desired.Width||client.Height!=desired.Height) {
    if(!Native.AlignClient(target,desired)) {Log("binding-failed align-client "+Native.Describe(target,cfg));return;}
    client=Native.ClientBounds(target);
   }
   if(initialBounds.Left==client.Left&&initialBounds.Top==client.Top&&initialBounds.Width==client.Width&&initialBounds.Height==client.Height)return;
   initialBounds=client;
   Native.SetWindowPos(host,new IntPtr(-1),client.Left,client.Top,client.Width,client.Height,0x10);
   Log("bound-to-app-window "+Native.Describe(target,cfg));
  }
  private void Place() {
   if(closing)return;
   Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
   var screens=Native.Monitors();
   if(String.Equals(cfg.TargetDisplay,"auto",StringComparison.OrdinalIgnoreCase)&&autoDisplay==null) {
    int primary=screens.FindIndex(s=>(s.Flags&1)!=0);
    autoDisplay=screens[Geometry.BestMonitor(Native.ClientBounds(target),screens.Select(s=>s.Monitor).ToArray(),primary)].Device;
   }
   string requestedDisplay=autoDisplay??cfg.TargetDisplay;
   var screen=screens.FirstOrDefault(s=>String.Equals(s.Device,requestedDisplay,StringComparison.OrdinalIgnoreCase));
   if(screen.Device==null) {
    if(!String.IsNullOrWhiteSpace(cfg.TargetDisplay))Log("display-fallback reason=configured-display-unavailable requested="+cfg.TargetDisplay);
    screen=screens.First(s=>(s.Flags&1)!=0);
   }
   displayName=screen.Device;
   Bounds area=screen.Work;
   initialBounds=Geometry.Fit(area,cfg.PlayerWidth,cfg.PlayerHeight);
   if(cfg.BindToAppWindow&&existingAtStart&&Native.Eligible(target,cfg)) {
    Bounds client=Native.ClientBounds(target);
    int index=Geometry.BestMonitor(client,screens.Select(s=>s.Monitor).ToArray(),screens.FindIndex(s=>(s.Flags&1)!=0));
    initialBounds=Geometry.Contain(client,screens[index].Work);
   }
   foreach(var s in screens)Log("display="+s.Device+" primary="+((s.Flags&1)!=0)+" physical-bounds="+s.Monitor.Left+","+s.Monitor.Top+","+s.Monitor.Width+","+s.Monitor.Height+" physical-work="+s.Work.Left+","+s.Work.Top+","+s.Work.Width+","+s.Work.Height);
   Log("placement requested="+cfg.TargetDisplay+" selected="+displayName+" physical="+initialBounds.Left+","+initialBounds.Top+","+initialBounds.Width+","+initialBounds.Height);
   if(host!=IntPtr.Zero)Native.SetWindowPos(host,new IntPtr(-1),initialBounds.Left,initialBounds.Top,initialBounds.Width,initialBounds.Height,0x10);
  }
  public BootWindow(Config c,string baseDir):this(c,baseDir,Native.FindApp,null) {}
  internal BootWindow(Config c,string baseDir,Func<Config,IntPtr> finder,Action<Config> launcher,Func<IntPtr,ContentEvidence> capture=null,Func<IntPtr,bool> activation=null) {
   findApp=finder;launchOverride=launcher;
   captureContent=capture??Native.CaptureContent;
   activationOverride=activation;
   // Prefer software composition for the legacy WPF media pipeline on mixed-DPI desktops.
   RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
   cfg=c;root=baseDir;Title=String.IsNullOrWhiteSpace(c.DisplayName)?"Dragon Codex Boot":c.DisplayName;
   WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
   AllowsTransparency=false;Topmost=true;Background=Brushes.Black;
   target=findApp(cfg);
   if(target!=IntPtr.Zero&&Native.IsIconic(target))Native.ShowWindow(target,9);
   existingAtStart=target!=IntPtr.Zero;
   Place();
   Width=cfg.PlayerWidth;Height=cfg.PlayerHeight;WindowStartupLocation=WindowStartupLocation.Manual;
   Grid grid=new Grid();
   media=new MediaElement { LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,
    Stretch=Stretch.Uniform,ScrubbingEnabled=false,Volume=Geometry.Clamp(cfg.Volume) };
   grid.Children.Add(media);
   status=new TextBlock { HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,
    Margin=new Thickness(20,20,20,38),FontSize=16,Foreground=new SolidColorBrush(Color.FromRgb(115,91,144)),
    Visibility=Visibility.Collapsed };
   grid.Children.Add(status);
   skip=new Button { Content="跳过 / 进入 Codex  Esc",HorizontalAlignment=HorizontalAlignment.Right,
    VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(16),Padding=new Thickness(11,5,11,5),
    Background=Brushes.White,Foreground=new SolidColorBrush(Color.FromRgb(118,97,135)),Opacity=.45 };
   skip.Click+=delegate { EnterOrRecover(); };
   skip.MouseEnter+=delegate { skip.Opacity=1; };skip.MouseLeave+=delegate { skip.Opacity=.45; };
   grid.Children.Add(skip);Content=grid;
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape) { e.Handled=true;HandOff(true); } };
   SourceInitialized+=delegate {
    host=new WindowInteropHelper(this).Handle;
    Log("host-created hwnd=0x"+host.ToInt64().ToString("X")+" dpi="+Native.GetDpiForWindow(host));
    HwndSource.FromHwnd(host).AddHook(FixedBoundsHook);
    Native.SetWindowPos(host,new IntPtr(-1),initialBounds.Left,initialBounds.Top,initialBounds.Width,initialBounds.Height,0x10);
   };
   Loaded+=delegate {
    wall.Start();
    if(!Native.Eligible(target,cfg))target=findApp(cfg);
    if(lifecycle.RequestLaunch(target!=IntPtr.Zero))LaunchApp();
    else Log("existing-instance "+Native.Describe(target,cfg));
    if(failed) { timer.Start();return; }
    media.Source=new Uri(Path.GetFullPath(Path.Combine(root,cfg.Video)));
    media.Play();timer.Start();Log("started fixed-player="+initialBounds.Width+"x"+initialBounds.Height
     +" at "+initialBounds.Left+","+initialBounds.Top);
   };
   media.MediaOpened+=delegate {
    opened=true;Bounds actual=Native.ClientBounds(host);
    playbackStarted=wall.Elapsed.TotalSeconds;progressAt=playbackStarted;
    Log("media-opened player-client="+actual.Width+"x"+actual.Height+" at "+actual.Left+","+actual.Top);
    Log("media-duration="+(media.NaturalDuration.HasTimeSpan?media.NaturalDuration.TimeSpan.TotalSeconds.ToString():"unknown")+" dpi="+Native.GetDpiForWindow(host));
    Native.SetForegroundWindow(host);
   };
   media.MediaFailed+=delegate(object sender,ExceptionRoutedEventArgs e) {
    Log("media-failed: "+e.ErrorException.Message);status.Text="视频无法播放，正在转入Codex";status.Visibility=Visibility.Visible;
    HandOff();
   };
   media.MediaEnded+=delegate {
    if(closing||failed||handoffRequested)return;
    double elapsed=wall.Elapsed.TotalSeconds-playbackStarted;
    double duration=media.NaturalDuration.HasTimeSpan?media.NaturalDuration.TimeSpan.TotalSeconds:0;
    Log("media-ended position="+media.Position.TotalSeconds+" playback-elapsed="+elapsed);
    if(WindowLifecycle.PrematureEnd(duration,elapsed,cfg.HoldAt)) {
     if(playbackRetries++==0) { media.Close();opened=false;lastPosition=0;media.Source=new Uri(Path.GetFullPath(Path.Combine(root,cfg.Video)));media.Play();Log("early-media-end reopen-once");return; }
     Log("early-media-end repeated; requesting safe handoff");
    }
    endedAt=wall.Elapsed.TotalSeconds;HandOff();
   };
   timer=new DispatcherTimer(DispatcherPriority.Render);timer.Interval=TimeSpan.FromMilliseconds(16);
   timer.Tick+=Tick;
   Closed+=delegate { timer.Stop();media.Close();DropThumbnail(); };
  }
  private IntPtr FixedBoundsHook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
   if(message==0x007E)Dispatcher.BeginInvoke(new Action(Place));
   if(message==0x02E0) {
    Log("dpi-changed dpi="+(wParam.ToInt64()&65535));
    Dispatcher.BeginInvoke(new Action(Place));
    handled=true;return IntPtr.Zero;
   }
   if(message==0x0046&&!closing&&lParam!=IntPtr.Zero) {
    Native.WindowPos pos=(Native.WindowPos)Marshal.PtrToStructure(lParam,typeof(Native.WindowPos));
    pos.X=initialBounds.Left;pos.Y=initialBounds.Top;pos.Width=initialBounds.Width;pos.Height=initialBounds.Height;
    pos.Flags&=~(uint)0x3; // Permit the fixed coordinates even for a DPI or layout size request.
    Marshal.StructureToPtr(pos,lParam,false);handled=true;
   }
   return IntPtr.Zero;
  }
  private void AlignTarget() {
   if(cfg.BindToAppWindow||!cfg.MatchClientToPlayer||target==IntPtr.Zero||alignedHandle==target||alignmentAttempts>=3||wall.Elapsed.TotalSeconds-lastAlignment<.5)return;
   alignmentAttempts++;lastAlignment=wall.Elapsed.TotalSeconds;
   bool aligned=Native.AlignClient(target,initialBounds);if(aligned)alignedHandle=target;
   Log(aligned?"real-window-aligned-to-fixed-player":"real-window-alignment-inexact");
   Native.SetForegroundWindow(host);
  }
  private void LogAppSnapshot(string reason) {
   int session;using(var current=Process.GetCurrentProcess())session=current.SessionId;
   int count=0;
   foreach(string name in cfg.ProcessNames)foreach(Process p in Process.GetProcessesByName(name)) {
    try {
     if(p.SessionId!=session||Native.Identity(p.Id,cfg)==null)continue;
     count++;Log("process-snapshot reason="+reason+" pid="+p.Id+" started="+p.StartTime.ToUniversalTime().ToString("o"));
    } catch(Exception e) {Log("process-snapshot-unavailable reason="+reason+" error="+e.GetType().Name);} finally {p.Dispose();}
   }
   Log("process-snapshot-complete reason="+reason+" matching-processes="+count);
  }
  private void EnterOrRecover() {
   if(failed) {HandOff(true);return;}
   IntPtr candidate=Native.Eligible(target,cfg)?target:findApp(cfg);
   if(candidate!=IntPtr.Zero) {target=candidate;HandOff(true);return;}
   double now=wall.Elapsed.TotalSeconds;
   if(!lifecycle.RequestRecovery(false,now)) {
    Log("user-recovery-suppressed reason=cooldown-or-limit");
    status.Text="暂未找到 Codex 窗口；启动十秒后可重试，最多两次。Esc 退出。";status.Visibility=Visibility.Visible;return;
   }
   LogAppSnapshot("before-user-recovery");Log("user-requested-recovery no-usable-window=True");
   userHandoff=false;activationNeedsClick=false;activationSince=-1;lastActivation=-1;
   LaunchApp();status.Text="已重新请求打开 Codex，正在等待窗口；Esc 退出。";status.Visibility=Visibility.Visible;
  }
  private void LaunchApp() {
   try {
    // Shell launches the packaged desktop app; its CLI helper is never used as a UI executable.
    if(launchOverride!=null)launchOverride(cfg);
    else Process.Start(new ProcessStartInfo("explorer.exe",cfg.AppLaunch) { UseShellExecute=true });
    Log("app-launch-requested");
   } catch(Exception e) { Fail("app-launch-failed: "+e.Message); }
  }
  private void Probe() {
   double now=wall.Elapsed.TotalSeconds;
   if(now-lastProbe<.25)return;lastProbe=now;
   IntPtr candidate=Native.Eligible(target,cfg)?target:findApp(cfg);
   if(cfg.WaitForContent&&!ContentAllowed&&now>2) {
    IntPtr frontmost=findApp(cfg);if(frontmost!=IntPtr.Zero)candidate=frontmost;
   }
   string identity=Native.WindowIdentity(candidate,cfg);
   lifecycle.Observe(identity,candidate!=IntPtr.Zero&&Native.Responsive(candidate),now);
   ready=lifecycle.Ready;
   if(identity!=targetIdentity) {
    contentGate.Reset();
    if(targetIdentity!=null)Log("previous-target-lost-or-replaced "+Native.Describe(target,cfg)+" valid="+Native.IsWindow(target)+" visible="+Native.IsWindowVisible(target));
    if(targetIdentity!=null) {Log("lost-target-identity="+targetIdentity);LogAppSnapshot("target-lost");}
    DropThumbnail();thumbnailFailed=false;alignedHandle=IntPtr.Zero;alignmentAttempts=0;lastAlignment=-1;
    activationSince=-1;lastActivation=-1;activationNeedsClick=false;
    Log("candidate-changed "+Native.Describe(candidate,cfg));targetIdentity=identity;
   }
   target=candidate;
   if(candidate==IntPtr.Zero) {if(endedAt>=0)skip.Content="重试打开 Codex（Esc 退出）";return;}
   if(!activationNeedsClick)skip.Content="进入 Codex（Esc 退出）";
   if(Native.IsIconic(candidate))Native.ShowWindow(candidate,9);
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
   if(failed) { if(wall.Elapsed.TotalSeconds-failedAt>=8) { closing=true;Application.Current.Shutdown(2); } return; }
   Probe();
   BindWindow();
   ProbeContent();
   if(contentGate.Presented&&!activationNeedsClick)status.Visibility=Visibility.Collapsed;
   if(lifecycle.Expired(wall.Elapsed.TotalSeconds,cfg.MaxWaitSeconds)) { Fail("window-or-playback-timeout");return; }
   if(handoffRequested) { TryHandOff();return; }
   if(!opened) {
    if(wall.Elapsed.TotalSeconds>12) { Log("media-open-timeout");HandOff(); }
    return;
   }
   double t=media.Position.TotalSeconds;
   if(t>lastPosition+.02) { lastPosition=t;progressAt=wall.Elapsed.TotalSeconds; }
   if(!holding&&wall.Elapsed.TotalSeconds-progressAt>8) { Log("playback-stalled");HandOff();return; }
   if(!ready&&!startedFade&&t>=cfg.HoldAt) {
    if(!holding) { holding=true;Log("waiting-for-window animation-continues"); }
    status.Text="Codex正在启动…  Esc跳过";status.Visibility=Visibility.Visible;
   }
   if(holding&&ready) {
    holding=false;status.Visibility=Visibility.Collapsed;Log("window-available");
   }
   if(ready&&t>=cfg.HoldAt)AlignTarget();
   if(t>=cfg.TransitionStart&&ready&&(ContentAllowed||contentGate.Presented)) {
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
     else if(props.Opacity>0&&!contentGate.Presented) {contentGate.MarkPresented();status.Visibility=Visibility.Collapsed;Log("real-window-presented waiting-overlay-cleared");}
    }
    // Without a live thumbnail retain the animation until the actual target is activated.
   }
   if(endedAt>=0&&wall.Elapsed.TotalSeconds-endedAt>.25)HandOff();
  }
  private void HandOff(bool userRequested=false) {
   if(closing)return;
   if(userRequested) {
    userHandoff=true;activationNeedsClick=false;activationSince=-1;lastActivation=-1;
    if(!Native.Eligible(target,cfg))target=findApp(cfg);
    if(target==IntPtr.Zero||failed) {Log("user-dismissed-cover no-usable-window="+(target==IntPtr.Zero));closing=true;Application.Current.Shutdown(failed?2:0);return;}
    Log("user-requested-handoff content-check-bypassed");
   }
   if(failed) {closing=true;Application.Current.Shutdown(2);return;}
   if(handoffStarted<0)handoffStarted=wall.Elapsed.TotalSeconds;
   handoffRequested=true;media.Pause();
   Log("handoff-cover-retained media-visible="+(media.Visibility==Visibility.Visible)+" thumbnail="+(thumbnail!=IntPtr.Zero));
   if(!ready||!CanHandoff) {status.Text=ready?"正在准备切换 Codex，也可点击右上角直接进入。":"正在等待 Codex 窗口；Esc 退出动画。";status.Visibility=Visibility.Visible;if(!ready)skip.Content="重试打开 Codex（Esc 退出）";Log(ready?"handoff-waiting-for-rendered-content":"handoff-waiting-for-usable-window");}
   else status.Visibility=Visibility.Collapsed;
   TryHandOff();
  }
  private void TryHandOff() {
   if(activationNeedsClick&&!userHandoff)return;
   if(!ready||!CanHandoff||targetIdentity!=Native.WindowIdentity(target,cfg)||!Native.Eligible(target,cfg)||!Native.Responsive(target))return;
   if(!ContentAllowed&&!advisoryLogged) {advisoryLogged=true;Log("content-unverified-fallback reason=advisory-only");}
   double now=wall.Elapsed.TotalSeconds;
   if(now-lastActivation<.25)return;
   lastActivation=now;if(activationSince<0)activationSince=now;
   AlignTarget();
   if(Native.IsIconic(target))Native.ShowWindow(target,9);
   IntPtr foregroundBefore=Native.GetForegroundWindow();
   bool requested=activationOverride==null?Native.SetForegroundWindow(target):activationOverride(target);
   bool activated=activationOverride==null?Native.GetForegroundWindow()==target:requested;
   if(!Native.Eligible(target,cfg)) { ready=false;Topmost=true;Log("handoff-failed window-invalid");return; }
   Log("handoff window="+Native.Describe(target,cfg)+" foreground-before=0x"+foregroundBefore.ToInt64().ToString("X")+" host=0x"+host.ToInt64().ToString("X")+" foreground-request="+requested+" foreground-confirmed="+activated);
   if(!activated) {
    if(now-activationSince<1.5) {Log("foreground-activation-pending retry-within-1.5s");return;}
    activationNeedsClick=true;userHandoff=false;
    skip.Content="点击进入 Codex";
    status.Text="Codex 窗口已出现；Windows 阻止自动切换，请点击右上角进入。";status.Visibility=Visibility.Visible;
    Log("foreground-activation-denied awaiting-user-click");return;
   }
   Topmost=false;Native.DwmFlush();
   if(!Native.Eligible(target,cfg)||(activationOverride==null&&Native.GetForegroundWindow()!=target)) {ready=false;Log("handoff-delayed compositor-window-changed");return;}
   closing=true;timer.Stop();Close();Log("handed-off-to-real-window");
  }
  private void Fail(string reason) {
   if(failed)return;failed=true;failedAt=wall.Elapsed.TotalSeconds;
   DropThumbnail();media.Stop();media.Visibility=Visibility.Collapsed;Opacity=1;Topmost=false;
   status.Text="未能完成交接，请从任务栏打开 Codex。8 秒后退出，Esc 立即退出。";status.Visibility=Visibility.Visible;
   Log("handoff-failed reason="+reason+" "+Native.Describe(target,cfg));
  }
  private void Log(string s) {
   try {
    Directory.CreateDirectory(Path.Combine(root,"logs"));
    string line=DateTimeOffset.Now.ToString("o")+" launcher-pid="+Process.GetCurrentProcess().Id+" elapsed="+wall.Elapsed.TotalSeconds.ToString("F3")+" "+s+Environment.NewLine;
    File.AppendAllText(Path.Combine(root,"logs","launcher.log"),line);
   } catch {}
  }
 }
 public static class Entry {
  private static void Record(string root,string message) {
   try {Directory.CreateDirectory(Path.Combine(root,"logs"));File.AppendAllText(Path.Combine(root,"logs","launcher.log"),DateTimeOffset.Now.ToString("o")+" launcher-pid="+Process.GetCurrentProcess().Id+" "+message+Environment.NewLine);} catch {}
  }
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
    Bounds fixedPlayer=Geometry.Fit(monitor,c.PlayerWidth,c.PlayerHeight);
    Assert(fixedPlayer.Left>=monitor.Left&&fixedPlayer.Top>=monitor.Top&&fixedPlayer.Right<=monitor.Right&&fixedPlayer.Bottom<=monitor.Bottom,"contained physical video size");
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
    Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
    Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
    Record(root,"entry version=0.2.7.0");
    bool owns;using(Mutex mutex=new Mutex(true,"Local\\DragonCodexBoot",out owns)) {
     if(!owns) {Record(root,"launch-suppressed reason=launcher-already-running");return 0;}
     Config c=ReadConfig(root);
     if(!File.Exists(Path.GetFullPath(Path.Combine(root,c.Video))))throw new Exception("未找到启动视频。请先运行scripts/Import-Animation.ps1导入自己的MP4，或修改launcher.json的Video路径。");
     // Shortcut integration is explicit only; retain legacy configuration on disk.
     Application app=new Application();
     app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) {
      Record(root,"dispatcher-exception "+e.Exception);
      e.Handled=true;MessageBox.Show("启动器已退出："+e.Exception.Message,"Codex启动");app.Shutdown(1);
     };
     return app.Run(new BootWindow(c,root));
    }
   } catch(Exception e) {
    Record(root,"startup-exception "+e);
    if(args.Contains("--self-test")) { File.WriteAllText(Path.Combine(root,"self-test-error.txt"),e.ToString());return 1; }
    MessageBox.Show(e.Message,"Codex启动");return 1;
   }
  }
 }
}
