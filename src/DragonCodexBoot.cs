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

[assembly: AssemblyTitle("ChatGPT")]
[assembly: AssemblyProduct("Dragon Codex Boot")]
[assembly: AssemblyDescription("Community animation launcher for the Codex desktop client")]
[assembly: AssemblyVersion("0.2.10.0")]
[assembly: AssemblyFileVersion("0.2.10.0")]

namespace DragonCodexBoot {
 public sealed class Config {
  public Config() { AutoReplaceEntrypoints=true; ScanAllLocalDrives=true; }
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
  public static Bounds FitInside(Bounds area,int width,int height) {
   double scale=Math.Min(area.Width/(double)Math.Max(1,width),area.Height/(double)Math.Max(1,height));
   return Centered(area,Math.Max(1,(int)Math.Round(width*scale)),Math.Max(1,(int)Math.Round(height*scale)));
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
 public sealed class WindowStability {
  private IntPtr window;
  private Bounds bounds;
  private double since;
  public void Reset() { window=IntPtr.Zero; }
  public static bool Same(Bounds a,Bounds b) {
   return a.Left==b.Left&&a.Top==b.Top&&a.Width==b.Width&&a.Height==b.Height;
  }
  public bool Observe(IntPtr candidate,Bounds current,double now) {
   if(candidate==IntPtr.Zero||current.Width<=0||current.Height<=0) { Reset();return false; }
   if(candidate!=window||!Same(bounds,current)||now<since) {
    window=candidate;bounds=current;since=now;return false;
   }
   return now-since>=.6;
  }
 }
 public static class BackgroundLaunch {
  public static ProcessStartInfo StartInfo(string executable) {
   var info=new ProcessStartInfo(executable) {UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable),WindowStyle=ProcessWindowStyle.Hidden};
   info.EnvironmentVariables["CODEX_ELECTRON_START_IN_BACKGROUND"]="1";
   return info;
  }
  public static string Resolve(Config cfg) {
   string family=cfg.AppLaunch.Substring("shell:AppsFolder\\".Length).Split('!')[0];
   uint count=0,length=0;
   int hr=Native.GetPackagesByPackageFamily(family,ref count,IntPtr.Zero,ref length,IntPtr.Zero);
   if(hr!=122||count==0)return null;
   IntPtr names=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size)),buffer=Marshal.AllocHGlobal(checked((int)length*2));
   try {
    if(Native.GetPackagesByPackageFamily(family,ref count,names,ref length,buffer)!=0)return null;
    for(int i=0;i<count;i++) {
     string fullName=Marshal.PtrToStringUni(Marshal.ReadIntPtr(names,i*IntPtr.Size));
     uint pathLength=0;
     if(Native.GetPackagePathByFullName(fullName,ref pathLength,null)!=122)continue;
     StringBuilder path=new StringBuilder((int)pathLength);
     if(Native.GetPackagePathByFullName(fullName,ref pathLength,path)!=0)continue;
     foreach(string name in cfg.ProcessNames) {
      string exe=Path.Combine(path.ToString(),"app",name+".exe");
      if(File.Exists(exe))return exe;
     }
    }
   } finally { Marshal.FreeHGlobal(names);Marshal.FreeHGlobal(buffer); }
   return null;
  }
 }
 public sealed class ArrivalPlacement {
  private readonly Func<bool> allowed;
  private readonly Func<IntPtr,bool> candidate;
  private readonly Func<IntPtr,bool> stage;
  public ArrivalPlacement(Func<bool> allowed,Func<IntPtr,bool> candidate,Func<IntPtr,bool> stage) {
   this.allowed=allowed;this.candidate=candidate;this.stage=stage;
  }
  public bool Observe(IntPtr window,int objectId,int childId) {
   if(window==IntPtr.Zero||objectId!=0||childId!=0||!allowed()||!candidate(window)||!allowed())return false;
   return stage(window);
  }
 }
 public sealed class WindowChoice {
  private readonly IntPtr preferred;
  private long area;
  public IntPtr Handle { get; private set; }
  public bool Visible { get; private set; }
  public WindowChoice(IntPtr preferred) { this.preferred=preferred; }
  public bool Consider(IntPtr window,bool visible,Bounds bounds) {
   long size=(long)bounds.Width*bounds.Height;
   if(Handle==IntPtr.Zero||(visible&&!Visible)||(visible==Visible&&(window==preferred||(Handle!=preferred&&size>area)))) {
    Handle=window;area=size;Visible=visible;
   }
   // Hidden preferred handles must not terminate enumeration before a visible main window.
   return visible&&window==preferred;
  }
 }
 public interface IHandoffDesktop {
  bool CanActivate { get; }
  IntPtr FindWindow(IntPtr preferred);
  bool IsResponsive(IntPtr window);
  bool IsMinimized(IntPtr window);
  void Restore(IntPtr window);
  bool Align(IntPtr window);
  bool MatchesBounds(IntPtr window);
  Bounds GetBounds(IntPtr window);
  bool IsForeground(IntPtr window);
  bool FlushPresentation(IntPtr window);
  bool PrepareReveal(IntPtr host,IntPtr window);
  bool RevealCovered(IntPtr host,IntPtr window);
  bool Prewarm();
  bool Activate(IntPtr window);
  bool Launch();
 }
 public enum HandoffResult { Waiting,Complete,Background,Failed }
 public sealed class HandoffController {
  private readonly IHandoffDesktop desktop;
  private readonly double started,timeout;
  private double lastLaunch=-100,lastActivate=-100;
  private double foregroundSince=-1;
  private IntPtr presentedWindow;
  private readonly WindowStability geometry;
  private int launchAttempts;
  public IntPtr Window { get; private set; }
  public HandoffController(IHandoffDesktop desktop,double started,double timeout,WindowStability geometry=null) {
   this.desktop=desktop;this.started=started;this.timeout=timeout;this.geometry=geometry??new WindowStability();
  }
  public HandoffResult Tick(double now,bool allowActivation,IntPtr preferred) {
   Window=desktop.FindWindow(preferred);
   if(Window!=presentedWindow) { presentedWindow=Window;foregroundSince=-1; }
   if(Window!=IntPtr.Zero&&desktop.IsResponsive(Window)) {
    if(!allowActivation||!desktop.CanActivate)return HandoffResult.Background;
    if(desktop.IsMinimized(Window)) {
     if(!desktop.CanActivate)return HandoffResult.Background;
     desktop.Restore(Window);
     geometry.Reset();foregroundSince=-1;
    }
    if(!desktop.MatchesBounds(Window)) {
     geometry.Reset();foregroundSince=-1;
     if(!desktop.CanActivate)return HandoffResult.Background;
     if(!desktop.Align(Window)||!desktop.MatchesBounds(Window))return Pending(now);
    }
    if(!geometry.Observe(Window,desktop.GetBounds(Window),now)) { foregroundSince=-1;return Pending(now); }
    bool foreground=desktop.IsForeground(Window);
    if(!foreground)foregroundSince=-1;
    if(!foreground&&now-lastActivate>=.5) {
     if(!desktop.CanActivate)return HandoffResult.Background;
     lastActivate=now;desktop.Activate(Window);
     // A successful API return alone does not prove the window became foreground.
     foreground=desktop.IsForeground(Window);
    }
    if(foreground) {
     if(foregroundSince<0)foregroundSince=now;
     // Foreground ownership precedes DWM presentation. Keep the last animation
     // frame for multiple refreshes and require a compositor barrier before close.
     if(now-foregroundSince>=.3&&desktop.FlushPresentation(Window)&&desktop.IsForeground(Window)) {
      if(desktop.MatchesBounds(Window)&&geometry.Observe(Window,desktop.GetBounds(Window),now))return HandoffResult.Complete;
      geometry.Reset();foregroundSince=-1;
     }
    }
   } else {
    foregroundSince=-1;geometry.Reset();
    if(Window==IntPtr.Zero&&allowActivation&&desktop.CanActivate&&launchAttempts<3&&now-lastLaunch>=3) {
     lastLaunch=now;launchAttempts++;desktop.Launch();
    }
   }
   return Pending(now);
  }
  private HandoffResult Pending(double now) { return now-started>=timeout?HandoffResult.Failed:HandoffResult.Waiting; }
 }
 internal sealed class ClientDesktop:IHandoffDesktop {
  private readonly Config config;
  private readonly Action<string> log;
  private readonly Func<bool> canActivate;
  private readonly Func<Bounds> playerBounds;
  private int launching;
  private bool prewarmAttempted,backgroundStarted,interactiveRetry;
  public ClientDesktop(Config config,Action<string> log,Func<bool> canActivate,Func<Bounds> playerBounds) {
   this.config=config;this.log=log;this.canActivate=canActivate;this.playerBounds=playerBounds;
  }
  public bool CanActivate { get { return canActivate(); } }
  public IntPtr FindWindow(IntPtr preferred) { return Native.FindApp(config,preferred); }
  public bool IsResponsive(IntPtr window) { return Native.Responsive(window); }
  public bool IsMinimized(IntPtr window) { return Native.IsIconic(window); }
  public void Restore(IntPtr window) { if(CanActivate) { Native.ShowWindow(window,4);log("client-restored-without-activation-at-handoff"); } }
  public Bounds GetBounds(IntPtr window) { Bounds bounds;return Native.GetWindowRect(window,out bounds)?bounds:new Bounds(); }
  public bool MatchesBounds(IntPtr window) {
   return !Native.IsIconic(window)&&!Native.IsZoomed(window)&&WindowStability.Same(GetBounds(window),playerBounds());
  }
  public bool Align(IntPtr window) {
   if(!CanActivate||Native.IsIconic(window))return false;
   if(MatchesBounds(window))return true;
   // SW_SHOWNOACTIVATE restores a maximized client without taking foreground.
   if(Native.IsZoomed(window)) { Native.ShowWindow(window,4);if(!CanActivate)return false; }
   Bounds current;
   if(!Native.GetWindowRect(window,out current)||current.Width<=0||current.Height<=0)return false;
   Bounds desired=playerBounds();
   if(!CanActivate)return false;
   // Match the physical animation rectangle while preserving Z order and focus.
   bool result=Native.SetWindowPos(window,IntPtr.Zero,desired.Left,desired.Top,desired.Width,desired.Height,0x4|0x10);
   Bounds actual;
   result=result&&Native.GetWindowRect(window,out actual)&&actual.Left==desired.Left&&actual.Top==desired.Top
    &&actual.Width==desired.Width&&actual.Height==desired.Height;
   if(result)log("client-aligned-to-animation at="+desired.Left+","+desired.Top+" size="+desired.Width+"x"+desired.Height);
   return result;
  }
  public bool IsForeground(IntPtr window) { return Native.ForegroundBelongsTo(window); }
  public bool PrepareReveal(IntPtr host,IntPtr window) { return CanActivate&&Native.PrepareReveal(host,window); }
  public bool RevealCovered(IntPtr host,IntPtr window) {
   return CanActivate&&Native.IsWindowVisible(window)&&!Native.IsIconic(window)
    &&Native.Covers(host,window);
  }
  public void EnableInteractiveRetry() { interactiveRetry=true; }
  public bool Prewarm() {
   if(prewarmAttempted)return true;
   if(!CanActivate)return false;
   prewarmAttempted=true;
   // A second instance may focus an existing app, so never prewarm a running client.
   if(Native.AppRunning(config)) { log("prewarm-skipped client-process-already-running=true");return false; }
   try {
    string exe=BackgroundLaunch.Resolve(config);
    if(exe==null)throw new FileNotFoundException("Cannot resolve packaged client executable");
    if(!CanActivate)return false;
    using(Process p=Process.Start(BackgroundLaunch.StartInfo(exe)))log("background-prewarm-started pid="+p.Id+" activate=false");
    backgroundStarted=true;
    return true;
   } catch(Exception e) { log("background-prewarm-failed: "+e.Message);return false; }
  }
  public bool FlushPresentation(IntPtr window) {
   if(!CanActivate||!Native.IsWindowVisible(window)||Native.IsIconic(window)||!IsForeground(window))return false;
   int cloaked;
   if(Native.DwmGetWindowAttribute(window,14,out cloaked,4)!=0||cloaked!=0)return false;
   int result=Native.DwmFlush();
   bool confirmed=result==0&&CanActivate&&IsForeground(window);
   if(confirmed)log("handoff-composition-settled launcher-flush=true stable-foreground-ms=300");
   return confirmed;
  }
  public bool Activate(IntPtr window) {
   if(!CanActivate)return false;
   bool result=Native.SetForegroundWindow(window);log("foreground-request result="+result);return result;
  }
  public bool Launch() {
   if(!CanActivate)return false;
   // Do not turn a pending background startup into foreground shell activation.
   // The recovery button explicitly authorizes an interactive retry if necessary.
   if(!interactiveRetry) { if(!prewarmAttempted)Prewarm();if(backgroundStarted)return true; }
   if(Interlocked.CompareExchange(ref launching,1,0)!=0)return true;
   // Shell activation may block while a packaged application starts. Keep the UI responsive.
   Thread launch=new Thread(delegate() {
    Native.IApplicationActivationManager manager=null;
    try {
     Guid clsid=new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"),iid=typeof(Native.IApplicationActivationManager).GUID;
     int hr=Native.CoCreateInstance(ref clsid,IntPtr.Zero,4,ref iid,out manager);
     Marshal.ThrowExceptionForHR(hr);
     if(!CanActivate) { log("package-activation-cancelled focus-changed=true");return; }
     uint pid;
     hr=manager.ActivateApplication(config.AppLaunch.Substring("shell:AppsFolder\\".Length),null,2,out pid);
     Marshal.ThrowExceptionForHR(hr);log("package-activation-confirmed pid="+pid);
    } catch(Exception e) {
     log("package-activation-failed: "+e.Message);
     try {
      if(!CanActivate) { log("shell-fallback-cancelled focus-changed=true");return; }
      Process.Start(new ProcessStartInfo("explorer.exe",config.AppLaunch) { UseShellExecute=true });
      log("shell-fallback-requested");
     } catch(Exception fallback) { log("app-launch-failed: "+fallback.Message); }
    } finally {
     if(manager!=null)Marshal.ReleaseComObject(manager);
     Interlocked.Exchange(ref launching,0);
    }
   });
   launch.IsBackground=true;launch.SetApartmentState(ApartmentState.STA);launch.Start();
   log("app-launch-requested");return true;
  }
 }
 // WinEvent is deliberately out-of-process: no DLL injection or client patching.
 internal sealed class ClientArrivalMonitor:IDisposable {
  private readonly Config config;
  private readonly Func<bool> allowed;
  private readonly Func<Bounds> bounds;
  private readonly IntPtr host;
  private readonly Action<string> log;
  private readonly ArrivalPlacement placement;
  private readonly Native.WinEventProc callback;
  private readonly List<IntPtr> hooks=new List<IntPtr>();
  private readonly HashSet<IntPtr> transitions=new HashSet<IntPtr>();
  private readonly HashSet<IntPtr> reported=new HashSet<IntPtr>();
  private bool disposed,staging;
  public ClientArrivalMonitor(Config config,IntPtr host,Func<bool> allowed,Func<Bounds> bounds,Action<string> log) {
   this.config=config;this.host=host;this.allowed=allowed;this.bounds=bounds;this.log=log;
   placement=new ArrivalPlacement(allowed,IsCandidate,Stage);
   callback=delegate(IntPtr hook,uint evt,IntPtr window,int objectId,int childId,uint thread,uint time) {
    if(disposed||staging)return;
    try { placement.Observe(window,objectId,childId); }
    catch(Exception e) { log("client-arrival-observer-error: "+e.Message); }
   };
   // CREATE, SHOW and LOCATIONCHANGE cover both early creation and later
   // Electron saved-bounds restoration; install before prewarming the client.
   foreach(uint evt in new uint[] {0x8000,0x8002,0x800B}) {
    IntPtr hook=Native.SetWinEventHook(evt,evt,IntPtr.Zero,callback,0,0,2);
    if(hook!=IntPtr.Zero)hooks.Add(hook);
   }
   log("client-arrival-observer-installed hooks="+hooks.Count+" before-prewarm=true");
  }
  private bool IsCandidate(IntPtr window) {
   if(window==host||Native.GetAncestor(window,2)!=window||Native.GetWindow(window,4)!=IntPtr.Zero
    ||(Native.GetWindowLong(window,-20)&0x80)!=0||Native.IsIconic(window))return false;
   StringBuilder name=new StringBuilder(128);
   if(Native.GetClassName(window,name,name.Capacity)==0||name.ToString()!="Chrome_WidgetWin_1")return false;
   Bounds current;
   return Native.GetWindowRect(window,out current)&&current.Width>=450&&current.Height>=280&&Native.AppWindow(config,window);
  }
  private bool Stage(IntPtr window) {
   if(disposed||!allowed())return false;
   staging=true;
   try {
    bool visible=Native.IsWindowVisible(window);
    // Only suppress transitions on newly discovered hidden client windows;
    // never change system-wide animation settings or existing visible apps.
    if(!visible&&!transitions.Contains(window)) {
     int disable=1;
     if(Native.DwmSetWindowAttribute(window,3,ref disable,4)==0)transitions.Add(window);
    }
    Bounds current,desired=bounds();
    if(!Native.GetWindowRect(window,out current)||!allowed())return false;
    bool covered=Native.GetForegroundWindow()==host;
    if(WindowStability.Same(current,desired)&&(!visible||!covered||Native.Covers(host,window)))return true;
    // One nonactivating request sets geometry AND places this client behind
    // the animation. ASYNCWINDOWPOS avoids blocking on unfinished Electron UI.
    uint flags=0x10|0x200|0x4000;
    if(!covered)flags|=0x4;
    bool result=Native.SetWindowPos(window,covered?host:IntPtr.Zero,desired.Left,desired.Top,desired.Width,desired.Height,flags);
    if(result&&reported.Add(window))log("client-arrival-placement-requested handle="+window+" visible="+visible
     +" at="+desired.Left+","+desired.Top+" size="+desired.Width+"x"+desired.Height+" activate=false");
    return result;
   } finally { staging=false; }
  }
  public void Dispose() {
   if(disposed)return;disposed=true;
   foreach(IntPtr hook in hooks)Native.UnhookWinEvent(hook);
   foreach(IntPtr window in transitions)if(Native.IsWindow(window)&&Native.AppWindow(config,window)) { int enable=0;Native.DwmSetWindowAttribute(window,3,ref enable,4); }
   hooks.Clear();transitions.Clear();
  }
 }
 internal static class Native {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
  public static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint length,IntPtr buffer);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
  public static extern int GetPackagePathByFullName(string name,ref uint length,StringBuilder path);
  [ComImport,Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IApplicationActivationManager {
   [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string appID,[MarshalAs(UnmanagedType.LPWStr)]string arguments,uint options,out uint processId);
  }
  [DllImport("ole32.dll",ExactSpelling=true)]
  public static extern int CoCreateInstance(ref Guid clsid,IntPtr outer,uint context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IApplicationActivationManager manager);
  public delegate void WinEventProc(IntPtr hook,uint evt,IntPtr window,int objectId,int childId,uint thread,uint time);
  [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc callback,uint pid,uint thread,uint flags);
  [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window,StringBuilder name,int count);
  [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr window,int attr,ref int value,int size);
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
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h,uint msg,IntPtr w,IntPtr l,uint flags,uint ms,out IntPtr result);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h,uint flags,StringBuilder path,ref int length);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest,IntPtr source,out IntPtr thumb);
  [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
  [DllImport("dwmapi.dll")] public static extern int DwmFlush();
  public static bool PrepareReveal(IntPtr host,IntPtr client) {
   // Place only our client immediately behind the active animation. No activation,
   // geometry changes, TOPMOST, or raising the animation above another application.
   if(client==IntPtr.Zero||IsIconic(client)||!Responsive(client))return false;
   if(GetForegroundWindow()!=host)return false;
   if(!SetWindowPos(client,host,0,0,0,0,0x1|0x2|0x10))return false;
   DwmFlush(); // Synchronize our composition update; this is not a client content-ready signal.
   return Covers(host,client)&&GetForegroundWindow()==host;
  }
  public static bool Covers(IntPtr host,IntPtr client) {
   // Invisible helper windows in the Z-order chain do not obscure the client.
   for(IntPtr above=GetWindow(client,3);above!=IntPtr.Zero;above=GetWindow(above,3)) {
    if(above==host)return true;
    int cloaked;
    if(IsWindowVisible(above)&&(DwmGetWindowAttribute(above,14,out cloaked,4)!=0||cloaked==0))return false;
   }
   return false;
  }
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
  public static bool ForegroundBelongsTo(IntPtr window) {
   IntPtr foreground=GetForegroundWindow();
   return window!=IntPtr.Zero&&(foreground==window||GetAncestor(foreground,3)==window);
  }
  public static bool AppWindow(Config cfg,IntPtr window) {
   if(window==IntPtr.Zero)return false;
   uint pid;GetWindowThreadProcessId(window,out pid);
   try {
    using(Process p=Process.GetProcessById((int)pid)) {
     if(!cfg.ProcessNames.Any(n=>String.Equals(n,p.ProcessName,StringComparison.OrdinalIgnoreCase)))return false;
     IntPtr process=OpenProcess(0x1000,false,p.Id);
     if(process==IntPtr.Zero)return false;
     try {
      StringBuilder path=new StringBuilder(4096);int length=path.Capacity;
      return QueryFullProcessImageName(process,0,path,ref length)
       &&path.ToString().IndexOf(cfg.ProcessPathContains,StringComparison.OrdinalIgnoreCase)>=0;
     } finally { CloseHandle(process); }
    }
   } catch { return false; }
  }
  public static bool AppRunning(Config cfg) {
   foreach(string name in cfg.ProcessNames)foreach(Process p in Process.GetProcessesByName(name)) {
    try {
     IntPtr process=OpenProcess(0x1000,false,p.Id);
     if(process==IntPtr.Zero)continue;
     try {
      StringBuilder path=new StringBuilder(4096);int length=path.Capacity;
      if(QueryFullProcessImageName(process,0,path,ref length)&&path.ToString().IndexOf(cfg.ProcessPathContains,StringComparison.OrdinalIgnoreCase)>=0)return true;
     } finally { CloseHandle(process); }
    } finally { p.Dispose(); }
   }
   return false;
  }
  public static IntPtr FindApp(Config cfg) {
   return FindApp(cfg,IntPtr.Zero);
  }
  public static IntPtr FindApp(Config cfg,IntPtr preferred) {
   return FindApp(cfg,preferred,false);
  }
  public static IntPtr FindApp(Config cfg,IntPtr preferred,bool includeHidden) {
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
   WindowChoice choice=new WindowChoice(preferred);
   EnumWindows(delegate(IntPtr h,IntPtr unused) {
    uint pid;GetWindowThreadProcessId(h,out pid);
    if(!pids.Contains((int)pid)||(!includeHidden&&!IsWindowVisible(h))||GetWindow(h,4)!=IntPtr.Zero||(GetWindowLong(h,-20)&0x80)!=0)return true;
    int cloaked;
    if(DwmGetWindowAttribute(h,14,out cloaked,4)==0&&cloaked!=0)return true;
    Bounds b=ClientBounds(h);
    if(b.Width<450||b.Height<280)return true;
    if(choice.Consider(h,IsWindowVisible(h),b))return false;
    return true; // Keep a validated existing window instead of switching with Z order.
   },IntPtr.Zero);
   return choice.Handle;
  }
 }
 public sealed class BootWindow:Window {
  private readonly Config cfg;
  private readonly string root;
  private readonly MediaElement media;
  private readonly TextBlock status;
  private readonly DispatcherTimer timer;
  private readonly IHandoffDesktop desktop;
  private readonly Stopwatch wall=new Stopwatch();
  private readonly object logLock=new object();
  private IntPtr host,target;
  private ClientArrivalMonitor arrivalMonitor;
  private Func<IntPtr> foregroundWindow=Native.GetForegroundWindow;
  private Func<IntPtr,bool> clientOwnsForeground;
  private readonly WindowStability geometry=new WindowStability();
  private Bounds initialBounds,screenArea;
  private double lastProbe=-1;
  private double revealStarted=-1;
  private double handoffStarted;
  private bool ready,opened,closing,finishing,failed;
  private bool prewarmRequested;
  private volatile bool userSwitchedAway,hasOwnedFocus;
  private HandoffController handoff;
  private StackPanel recovery;
  public BootWindow(Config c,string baseDir) {
   cfg=c;root=baseDir;Title=String.IsNullOrWhiteSpace(c.DisplayName)?"Dragon Codex Boot":c.DisplayName;
   clientOwnsForeground=delegate(IntPtr window) { return Native.AppWindow(cfg,window); };
   desktop=new ClientDesktop(c,Log,delegate {
    IntPtr foreground=foregroundWindow();
    return !closing&&hasOwnedFocus&&!userSwitchedAway&&(foreground==host||clientOwnsForeground(foreground));
   },delegate { return initialBounds; });
   WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=true;
   AllowsTransparency=false;Topmost=false;Background=new SolidColorBrush(Color.FromRgb(250,247,253));
   target=Native.FindApp(cfg);
   LogClientState("client-before-animation",target);
   System.Windows.Forms.Screen screen=target!=IntPtr.Zero?System.Windows.Forms.Screen.FromHandle(target)
    :System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
   System.Drawing.Rectangle monitor=screen.WorkingArea;
   Bounds available=new Bounds(monitor.Left,monitor.Top,monitor.Width,monitor.Height);
   screenArea=available;
   double scale=Math.Min(1,Math.Min(monitor.Width/(double)cfg.PlayerWidth,monitor.Height/(double)cfg.PlayerHeight));
   initialBounds=Geometry.Centered(available,(int)Math.Round(cfg.PlayerWidth*scale),(int)Math.Round(cfg.PlayerHeight*scale));
   Width=cfg.PlayerWidth;Height=cfg.PlayerHeight;WindowStartupLocation=WindowStartupLocation.Manual;
   Grid grid=new Grid();
   media=new MediaElement { LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Manual,
    Stretch=Stretch.Uniform,ScrubbingEnabled=true,Volume=Geometry.Clamp(cfg.Volume) };
   grid.Children.Add(media);
   status=new TextBlock { HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,
    Margin=new Thickness(20,20,20,38),FontSize=16,Foreground=new SolidColorBrush(Color.FromRgb(115,91,144)),
    Visibility=Visibility.Collapsed };
   grid.Children.Add(status);
   Button skip=new Button { Content="跳过 / 关闭  Esc",HorizontalAlignment=HorizontalAlignment.Right,
    VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(16),Padding=new Thickness(11,5,11,5),
    Background=Brushes.White,Foreground=new SolidColorBrush(Color.FromRgb(118,97,135)),Opacity=.45 };
   skip.Click+=delegate {
    if(finishing) { Log("user-cancelled-handoff");closing=true;Close(); }
    else BeginHandOff("skip-button");
   };
   skip.MouseEnter+=delegate { skip.Opacity=1; };skip.MouseLeave+=delegate { skip.Opacity=.45; };
   recovery=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,
    VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed };
   Button retry=new Button {Content="重新打开 Codex",Margin=new Thickness(8),Padding=new Thickness(16,10,16,10)};
   Button exit=new Button {Content="关闭",Margin=new Thickness(8),Padding=new Thickness(16,10,16,10)};
   retry.Click+=delegate {
    userSwitchedAway=false;failed=false;finishing=false;recovery.Visibility=Visibility.Collapsed;
    ClientDesktop client=desktop as ClientDesktop;if(client!=null)client.EnableInteractiveRetry();
    BeginHandOff("explicit-retry");
   };
   exit.Click+=delegate { Log("user-closed-recovery");closing=true;Close(); };
   recovery.Children.Add(retry);recovery.Children.Add(exit);grid.Children.Add(recovery);
   grid.Children.Add(skip);Content=grid;
   Activated+=delegate { hasOwnedFocus=true;userSwitchedAway=false; };
   PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    if(e.Key==Key.Escape) {
     e.Handled=true;
     if(finishing) { Log("user-cancelled-handoff");closing=true;Close(); }
     else BeginHandOff("escape");
    }
   };
   SourceInitialized+=delegate {
    host=new WindowInteropHelper(this).Handle;
    HwndSource.FromHwnd(host).AddHook(FixedBoundsHook);
    arrivalMonitor=new ClientArrivalMonitor(cfg,host,delegate { return !closing&&revealStarted<0&&desktop.CanActivate; },
     delegate { return initialBounds; },Log);
    Native.SetWindowPos(host,IntPtr.Zero,initialBounds.Left,initialBounds.Top,initialBounds.Width,initialBounds.Height,0x4|0x10);
   };
   Loaded+=delegate {
    wall.Start();
    timer.Start();Log("started version=0.2.10 match-animation-bounds=true topmost=false player="+initialBounds.Width+"x"+initialBounds.Height
     +" at "+initialBounds.Left+","+initialBounds.Top);
    Log("client-placement-policy=match-animation no-position-cache=true");
    string video=Path.GetFullPath(Path.Combine(root,cfg.Video));
    if(!File.Exists(video)) { BeginHandOff("missing-video");return; }
    // Cold preparation uses the client's own nonactivating startup branch.
    media.Source=new Uri(video);media.Play();
   };
   media.MediaOpened+=delegate {
    opened=true;Bounds actual=Native.ClientBounds(host);
    Log("media-opened player-client="+actual.Width+"x"+actual.Height+" at "+actual.Left+","+actual.Top);
   };
   media.MediaFailed+=delegate(object sender,ExceptionRoutedEventArgs e) {
    Log("media-failed: "+e.ErrorException.Message);status.Text="视频无法播放，正在转入Codex";status.Visibility=Visibility.Visible;
    BeginHandOff("media-failed");
   };
   media.MediaEnded+=delegate { BeginHandOff("media-ended"); };
   timer=new DispatcherTimer(DispatcherPriority.Render);timer.Interval=TimeSpan.FromMilliseconds(16);
   timer.Tick+=Tick;
   Closed+=delegate { if(arrivalMonitor!=null)arrivalMonitor.Dispose();timer.Stop();media.Stop(); };
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
  private void TrackFocus() {
   IntPtr foreground=foregroundWindow();
   if(foreground==host) { hasOwnedFocus=true;return; }
   bool clientForeground=clientOwnsForeground(foreground);
   if(clientForeground)return;
   if(hasOwnedFocus&&foreground!=IntPtr.Zero&&!userSwitchedAway) {
    userSwitchedAway=true;Log("user-switched-away; automatic-activation-disabled");
   }
  }
  private void Probe() {
   double now=wall.Elapsed.TotalSeconds;
   if(now-lastProbe<.05)return;lastProbe=now;
   IntPtr candidate=Native.FindApp(cfg,target,true);
   if(candidate==IntPtr.Zero||!Native.Responsive(candidate)) { ready=false;geometry.Reset();return; }
   if(candidate!=target) { LogClientState("client-observed",candidate); }
   target=candidate; // Publish before permission/alignment checks on a cold arrival.
   // A BrowserWindow exists before it is shown. Stage it without activating or
   // showing it, while the client is still following its background startup path.
   if(Native.GetForegroundWindow()==host&&desktop.CanActivate)
    Native.SetWindowPos(candidate,host,0,0,0,0,0x1|0x2|0x10);
   if(!desktop.MatchesBounds(candidate)) {
    geometry.Reset();
    if(desktop.CanActivate)desktop.Align(candidate);
   }
   ready=Native.IsWindowVisible(candidate)&&desktop.MatchesBounds(candidate)&&geometry.Observe(candidate,desktop.GetBounds(candidate),now);
   target=candidate;
   // Visible/responsive/stable is a window heuristic, not an official Codex content-ready event.
  }
  private void Tick(object sender,EventArgs e) {
   if(closing)return;
   TrackFocus();
   if(!prewarmRequested&&target==IntPtr.Zero&&desktop.CanActivate) {
    prewarmRequested=true;desktop.Prewarm();
   }
   Probe();
   if(finishing) { if(!failed)CompleteHandOff();return; }
   if(wall.Elapsed.TotalSeconds>cfg.MaxWaitSeconds+20) { BeginHandOff("playback-timeout");return; }
   if(!opened) {
    if(wall.Elapsed.TotalSeconds>12)BeginHandOff("media-open-timeout");
    return;
   }
   double t=media.Position.TotalSeconds;
   if(t>=cfg.TransitionStart) { PrepareReveal();AdvanceReveal(); }
   if(t>=cfg.TransitionEnd)BeginHandOff("transition-ended");
  }
  private void PrepareReveal() {
   if(revealStarted>=0||!ready||!desktop.CanActivate||!desktop.MatchesBounds(target))return;
   if(desktop.Align(target)&&desktop.PrepareReveal(host,target)) {
    revealStarted=wall.Elapsed.TotalSeconds;
    LogClientState("reveal-real-client-prepared",target);
   }
  }
  private bool AdvanceReveal() {
   if(revealStarted<0)return true; // Cold/minimized client: retain the final frame.
   if(!ready||!desktop.MatchesBounds(target)||!desktop.RevealCovered(host,target)) {
    revealStarted=-1;Log("reveal-paused client-bounds-or-coverage-changed=true opacity="+Opacity);return false;
   }
   double progress=(wall.Elapsed.TotalSeconds-revealStarted)/(cfg.TransitionEnd-cfg.TransitionStart);
   Opacity=Math.Min(Opacity,1-Geometry.Ease(progress));
   return progress>=1;
  }
  private void BeginHandOff(string reason) {
   if(closing||finishing)return;
   finishing=true;media.Pause();
   handoffStarted=wall.Elapsed.TotalSeconds;
   TrackFocus();
   PrepareReveal();
   Log("handoff-start reason="+reason+" allow-activation="+(hasOwnedFocus&&!userSwitchedAway));
   status.Text="正在打开 Codex…  Esc关闭";
   status.Visibility=revealStarted>=0?Visibility.Collapsed:Visibility.Visible;
   handoff=new HandoffController(desktop,wall.Elapsed.TotalSeconds,cfg.MaxWaitSeconds,geometry);
   CompleteHandOff();
  }
  private void CompleteHandOff() {
   // Keep the animation surface alive through the last fade frame; never rewind
   // its opacity or remove its surface before the real window is ready.
   bool timedOut=wall.Elapsed.TotalSeconds-handoffStarted>=cfg.MaxWaitSeconds;
   PrepareReveal(); // Cold windows arriving after the movie also need the real fade.
   if(!timedOut&&desktop.CanActivate&&!AdvanceReveal())return;
   HandoffResult result=timedOut?HandoffResult.Failed:handoff.Tick(wall.Elapsed.TotalSeconds,hasOwnedFocus&&!userSwitchedAway,target);
   // If an interactive recovery has already shown the client, never cover it
   // with the movie again. Hand off forward after actual geometry settles.
   if(result==HandoffResult.Complete&&revealStarted<0&&!desktop.IsForeground(handoff.Window)) {
    if(wall.Elapsed.TotalSeconds-handoffStarted<cfg.MaxWaitSeconds)return;
    result=HandoffResult.Failed;
   }
   if(result==HandoffResult.Complete||result==HandoffResult.Background) {
    LogClientState("client-after-animation",handoff.Window);
    Log(result==HandoffResult.Complete?"handoff-confirmed foreground=true":"handoff-background client-ready=true focus-preserved=true");
    closing=true;Close();
   } else if(result==HandoffResult.Failed) {
    // Recovery must be readable, but must never bring the movie's last frame back.
    failed=true;media.Visibility=Visibility.Collapsed;Opacity=1;recovery.Visibility=Visibility.Visible;
    status.Text="未能确认 Codex 已就绪。可重试，或从任务栏进入客户端。";
    Log("handoff-failed window="+handoff.Window+" activation-allowed="+(hasOwnedFocus&&!userSwitchedAway));
   }
  }
  private void LogClientState(string label,IntPtr window) {
   Bounds bounds;
   if(window!=IntPtr.Zero&&Native.GetWindowRect(window,out bounds)) {
    Log(label+" handle="+window+" bounds="+bounds.Left+","+bounds.Top+","+bounds.Width+","+bounds.Height
     +" maximized="+Native.IsZoomed(window)+" minimized="+Native.IsIconic(window));
   }
  }
  private void Log(string s) {
   try {
    lock(logLock) {
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
    if(args.Contains("--inspect-client")) {
     string executable=BackgroundLaunch.Resolve(ReadConfig(root));
     Directory.CreateDirectory(Path.Combine(root,"qa"));
     File.WriteAllText(Path.Combine(root,"qa","client-launch-plan.json"),new JavaScriptSerializer().Serialize(new {
      executable=executable,exists=executable!=null&&File.Exists(executable),environment="CODEX_ELECTRON_START_IN_BACKGROUND=1",
      clientLaunched=false,scope="Package resolution only; no desktop window or process is launched."
     }));
     return executable==null?1:0;
    }
    if(args.Contains("--restore-entrypoints"))return Integrate(root,"Restore",true);
    if(args.Contains("--integrate-entrypoints"))return Integrate(root,"Install",true);
    bool owns;using(Mutex mutex=new Mutex(true,"Local\\DragonCodexBoot",out owns)) {
     if(!owns)return 0;
     Config c=ReadConfig(root);
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
