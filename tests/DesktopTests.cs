using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DragonCodexBoot;
class DesktopTests {
 [STAThread] public static int Main(string[] args) {
  try {
   Native.SetProcessDpiAwarenessContext(new IntPtr(-4));Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
   Config c=Entry.ReadConfig(args[0]);c.TargetDisplay=args[1];
   bool recovery=args.Length>2&&args[2]=="recovery";
   bool missing=recovery||(args.Length>2&&args[2]=="missing");
   bool shortVideo=args.Length>2&&args[2]=="short";
   bool auto=args.Length>2&&args[2]=="auto";
   bool delayed=args.Length>2&&args[2]=="delayed-content";
   bool unavailable=args.Length>2&&args[2]=="unavailable-content";
   bool manual=args.Length>2&&args[2]=="manual-entry";
   bool stale=args.Length>2&&args[2]=="preview-stale";
   bool coldBound=args.Length>2&&args[2]=="cold-bound";
   bool bound=args.Length>2&&(args[2]=="bound"||coldBound);
   if(bound) {auto=true;c.BindToAppWindow=true;}
   if(auto)c.TargetDisplay="auto";
   if(shortVideo) { c.HoldAt=11.3;c.TransitionStart=12.7;c.TransitionEnd=13.65; }
   if(missing)c.MaxWaitSeconds=recovery?35:2;
   c.Video=args[3];c.MatchClientToPlayer=false;
   c.AutoReplaceEntrypoints=false;c.Volume=0;
   IntPtr real=Native.FindApp(c);
   if(Native.Eligible(IntPtr.Zero,c)||Native.Eligible(new IntPtr(12345),c))throw new Exception("Invalid HWND accepted");
   using(var fixture=new System.Windows.Forms.Form()) {
    fixture.Width=800;fixture.Height=600;fixture.Show();
    if(Native.Eligible(fixture.Handle,c))throw new Exception("Unpackaged unrelated top-level window accepted");
    IntPtr destroyed=fixture.Handle;fixture.Close();
    if(Native.Eligible(destroyed,c))throw new Exception("Destroyed window accepted");
   }
   Console.WriteLine("existing="+Native.Describe(real,c));
   if(real==IntPtr.Zero)throw new Exception("No eligible existing Codex window; test will not cold launch active chat.");
   if(auto) {
    Native.ShowWindow(real,6);
    try {if(!Native.Eligible(real,c)||Native.FindApp(c)==IntPtr.Zero)throw new Exception("Minimized existing instance was lost");}
    finally {Native.ShowWindow(real,9);}
   }
   ContentEvidence rendered=Native.CaptureContent(real);
   Console.WriteLine("existing-render="+rendered.HasContent+" "+rendered.Reason);
   if(!rendered.HasContent)Console.WriteLine("Advisory capture unavailable; exercise bounded fallback instead of blocking startup");
   var screens=Native.Monitors();
   var screen=auto?screens[Geometry.BestMonitor(Native.ClientBounds(real),screens.Select(s=>s.Monitor).ToArray(),screens.FindIndex(s=>(s.Flags&1)!=0))]:screens.First(s=>s.Device==args[1]);
   if(coldBound)screen=screens.First(s=>(s.Flags&1)!=0);
   Bounds originalClient=Native.ClientBounds(real);
   var area=screen.Work;int samples=0;bool outside=false,timedout=false;
   int launches=0;
   int discoveryCalls=0;
   var readinessClock=Stopwatch.StartNew();
   var app=new Application();BootWindow boot=null;boot=missing?new BootWindow(c,args[0],delegate {return IntPtr.Zero;},delegate {launches++;})
    :coldBound?new BootWindow(c,args[0],delegate {return ++discoveryCalls<=2?IntPtr.Zero:real;},delegate {launches++;})
    :(delayed||unavailable)?new BootWindow(c,args[0],Native.FindApp,null,delegate(IntPtr h) {
     return unavailable||readinessClock.Elapsed.TotalSeconds<9?new ContentEvidence {Reason="injected-startup-logo-or-unavailable"}:Native.CaptureContent(h);
    },delegate {return true;})
    :stale?new BootWindow(c,args[0],Native.FindApp,null,null,delegate {return true;})
    :manual?new BootWindow(c,args[0],Native.FindApp,null,null,delegate(IntPtr h) {
     bool requested=(bool)typeof(BootWindow).GetField("userHandoff",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(boot);
     if(!requested)return false;Native.SetForegroundWindow(h);return Native.GetForegroundWindow()==h;
    }):new BootWindow(c,args[0]);
   if(manual)boot.ShowInTaskbar=true; // Expose only the test host to the UI automation window picker.
   string logPath=Path.Combine(args[0],"logs","launcher.log");
   long logOffset=new FileInfo(logPath).Length;
   var timer=new DispatcherTimer();timer.Interval=TimeSpan.FromMilliseconds(100);var clock=Stopwatch.StartNew();
   int staleFrames=0;bool waitingReappeared=false;int recoveryClicks=0;
   timer.Tick+=delegate {
    if(recovery) {
     var method=typeof(BootWindow).GetMethod("EnterOrRecover",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
     if(recoveryClicks==0&&clock.Elapsed.TotalSeconds>=10) {method.Invoke(boot,null);method.Invoke(boot,null);recoveryClicks=1;}
     if(recoveryClicks==1&&clock.Elapsed.TotalSeconds>=20.2) {method.Invoke(boot,null);method.Invoke(boot,null);recoveryClicks=2;}
     if(clock.Elapsed.TotalSeconds>=21)boot.Close();
    }
    if(stale) {
     var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
     var gate=(ContentGate)typeof(BootWindow).GetField("contentGate",flags).GetValue(boot);
     if(gate.Presented) {
      gate.Observe(false,clock.Elapsed.TotalSeconds);staleFrames++;
      var label=(System.Windows.Controls.TextBlock)typeof(BootWindow).GetField("status",flags).GetValue(boot);
      if(label.Visibility==Visibility.Visible)waitingReappeared=true;
     }
    }
    var h=new WindowInteropHelper(boot).Handle;Bounds b=Native.ClientBounds(h);
    if(b.Width>0) { samples++;if(b.Left<area.Left||b.Top<area.Top||b.Right>area.Right||b.Bottom>area.Bottom)outside=true; }
    if(bound&&clock.Elapsed.TotalSeconds>1) {
     Bounds client=Native.ClientBounds(real);
     if(Math.Abs(client.Left-b.Left)>2||Math.Abs(client.Top-b.Top)>2||Math.Abs(client.Width-b.Width)>2||Math.Abs(client.Height-b.Height)>2)outside=true;
    }
    if(clock.Elapsed.TotalSeconds>(manual?45:22)) { timedout=true;boot.Close(); }
   };
   boot.Closed+=delegate { timer.Stop(); };timer.Start();int code;
   try {code=app.Run(boot);} finally {if(coldBound)Native.AlignClient(real,originalClient);}
   string log;using(var file=new FileStream(logPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {file.Seek(logOffset,SeekOrigin.Begin);using(var reader=new StreamReader(file))log=reader.ReadToEnd();}
   if(recovery) {
    if(launches!=3||recoveryClicks!=2||timedout||!log.Contains("user-requested-recovery")||!log.Contains("user-recovery-suppressed")||log.Contains("handed-off-to-real-window"))throw new Exception("Recovery flow failed launches="+launches);
    Console.WriteLine("PASS recovery desktop: two explicit retries, double-click suppressed, no automatic relaunch; AppLaunch injected");return 0;
   }
   if(missing) {
    if(code!=2||outside||timedout||launches!=1||!log.Contains("handoff-failed reason=window-or-playback-timeout")||log.Contains("handed-off-to-real-window"))throw new Exception("Missing-window fallback failed exit="+code+" launches="+launches);
    Console.WriteLine("PASS desktop missing-window: one injected launch, bounded timeout, failure UI auto-exit; no real AppLaunch");return 0;
   }
   if(unavailable) {
    if(code!=0||!log.Contains("handed-off-to-real-window")||!log.Contains("content-unverified-fallback")||log.Contains("live-window-connected")||clock.Elapsed.TotalSeconds>14)throw new Exception("Blank capture blocked bounded handoff fallback");
    Console.WriteLine("PASS unavailable-content: handoff after bounded advisory delay; activation injected, no real foreground claim");return 0;
   }
   if(delayed&&code==2&&log.Contains("foreground-activation-denied")) {
    int policyAt=log.IndexOf("content-probe ready=True"),previewAt=log.IndexOf("live-window-connected");
    if(policyAt<0||(previewAt>=0&&previewAt<policyAt)||!log.Contains("handoff-waiting-for-rendered-content")||clock.Elapsed.TotalSeconds<9)throw new Exception("Delayed content policy failed");
    Console.WriteLine("PASS delayed-content gate sequencing; foreground handoff NOT VERIFIED (Windows denied activation)");return 0;
   }
   if(code!=0||outside||timedout||samples<50||!log.Contains("handed-off-to-real-window")||(!coldBound&&log.Contains("app-launch-requested")))throw new Exception("Desktop regression failed samples="+samples+" outside="+outside+" timeout="+timedout+" exit="+code);
   if(coldBound&&(launches!=1||!log.Contains("cold-window-bound-to-player")))throw new Exception("Cold binding/launch regression");
   if(!log.Contains("handoff-cover-retained media-visible=True"))throw new Exception("Handoff cleared the animation cover");
   if(manual&&!log.Contains("user-requested-handoff content-check-bypassed"))throw new Exception("User-entry fallback was not exercised");
   if(stale&&(staleFrames==0||waitingReappeared||!log.Contains("real-window-presented")||log.Contains("handoff-waiting-for-rendered-content")||clock.Elapsed.TotalSeconds>9))throw new Exception("Presented app was covered again by stale readiness");
   int contentAt=log.IndexOf("content-probe ready=True"),thumbnailAt=log.IndexOf("live-window-connected");
   if(thumbnailAt>=0&&(contentAt<0||thumbnailAt<contentAt))throw new Exception("Thumbnail was revealed before rendered content checks passed");
   if(contentAt<0&&!log.Contains("content-unverified-fallback"))throw new Exception("Unverified capture lacked explicit bounded fallback");
   if(delayed&&(!log.Contains("handoff-waiting-for-rendered-content")||clock.Elapsed.TotalSeconds<9))throw new Exception("Startup logo was not held behind animation");
   Console.WriteLine("PASS desktop display="+args[1]+" samples="+samples+" seconds="+clock.Elapsed.TotalSeconds.ToString("F2")+" media/window/handoff tested; no AppLaunch");return 0;
  } catch(Exception e) { Console.Error.WriteLine(e);return 1; }
 }
}
