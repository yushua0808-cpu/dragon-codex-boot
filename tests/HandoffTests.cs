using System;
using DragonCodexBoot;

internal sealed class FakeDesktop : IHandoffDesktop {
 public IntPtr Window;
 public bool Responsive=true,Minimized,Foreground,ActivationWorks=true,ForegroundChanges=true,LaunchWorks=true;
 public int Launches,Activations,Restores,Alignments;
 public bool AlignmentWorks=true;
 public bool BoundsMatch;
 public bool ActivationAllowed=true,SwitchDuringProbe;
 public bool PresentationReady=true;
 public bool MoveDuringFlush;
 public int Flushes;
 public bool RevealWorks=true;
 public bool Covered=true;
 public int Reveals;
 public int Prewarms;
 public bool Prewarm() { Prewarms++;return true; }
 public bool PrepareReveal(IntPtr host,IntPtr window) { Reveals++;return RevealWorks&&BoundsMatch; }
 public bool RevealCovered(IntPtr host,IntPtr window) { return Covered; }
 public bool CanActivate { get { return ActivationAllowed; } }
 public IntPtr FindWindow(IntPtr preferred) { return Window; }
 public bool IsResponsive(IntPtr window) { if(SwitchDuringProbe)ActivationAllowed=false;return Responsive; }
 public bool IsMinimized(IntPtr window) { return Minimized; }
 public void Restore(IntPtr window) { Restores++;Minimized=false; }
 public bool Align(IntPtr window) { Alignments++;if(AlignmentWorks)BoundsMatch=true;return AlignmentWorks; }
 public bool MatchesBounds(IntPtr window) { return BoundsMatch; }
 public Bounds GetBounds(IntPtr window) { return BoundsMatch?new Bounds(100,100,800,450):new Bounds(300,250,1100,700); }
 public bool IsForeground(IntPtr window) { return Foreground; }
 public bool FlushPresentation(IntPtr window) { Flushes++;if(MoveDuringFlush)BoundsMatch=false;return PresentationReady; }
 public bool Activate(IntPtr window) { Activations++;if(ActivationWorks&&ForegroundChanges)Foreground=true;return ActivationWorks; }
 public bool Launch() { Launches++;return LaunchWorks; }
}
internal static class HandoffTests {
 static int checks;
 static void Check(bool condition,string label) { if(!condition)throw new Exception(label);checks++; }
 static HandoffController Session(FakeDesktop desktop) { return new HandoffController(desktop,0,12); }
 [STAThread]
 public static int Main(string[] args) {
  if(args.Length==2&&args[0]=="--hidden-arrival-fixture")return HiddenArrivalFixture.Run(args[1]);
  try {
   // Replay the observed cold arrival: helper first, then a hidden main HWND
   // with persisted bounds; it must be staged before responsiveness/first show.
   var staged=new System.Collections.Generic.List<IntPtr>();
   bool arrivalAllowed=true;
   var arrival=new ArrivalPlacement(()=>arrivalAllowed,h=>h==new IntPtr(22),h=>{staged.Add(h);return true;});
   Check(!arrival.Observe(new IntPtr(11),0,0)&&staged.Count==0,"Auxiliary cold windows cannot replace the primary arrival");
   Check(arrival.Observe(new IntPtr(22),0,0)&&staged.Count==1,"Hidden primary arrival must be aligned before its first show, without waiting for responsiveness or a 250ms poll");
   Check(!arrival.Observe(new IntPtr(22),-4,0)&&!arrival.Observe(new IntPtr(22),0,1),"Ignore accessibility child events");
   Check(arrival.Observe(new IntPtr(22),0,0)&&staged.Count==2,"Client's late saved-bounds restoration must be corrected again before reveal");
   arrivalAllowed=false;
   Check(!arrival.Observe(new IntPtr(22),0,0)&&staged.Count==2,"Arrival placement cannot move a client after user switches away");
   // Native cross-process CREATE/LOCATIONCHANGE delivery, with NO visible UI.
   string nativeResult=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"DragonBootArrival-"+Guid.NewGuid().ToString("N")+".txt");
   var nativeCfg=new Config { ProcessNames=new string[] {System.Diagnostics.Process.GetCurrentProcess().ProcessName},
    ProcessPathContains=System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) };
   var monitorType=typeof(BootWindow).Assembly.GetType("DragonCodexBoot.ClientArrivalMonitor",true);
   var nativeLog=new System.Collections.Generic.List<string>();
   using(var nativeMonitor=(IDisposable)Activator.CreateInstance(monitorType,new object[] {nativeCfg,IntPtr.Zero,
    new Func<bool>(()=>true),new Func<Bounds>(()=>new Bounds(100,120,800,450)),new Action<string>(line=>nativeLog.Add(line))})) {
    var fixtureStart=new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,
     "--hidden-arrival-fixture \""+nativeResult+"\"") {UseShellExecute=false,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden};
    using(var child=System.Diagnostics.Process.Start(fixtureStart)) {
     var timeout=System.Diagnostics.Stopwatch.StartNew();
     while(!child.HasExited&&timeout.Elapsed.TotalSeconds<6) {
      var frame=new System.Windows.Threading.DispatcherFrame();
      var pump=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background);
      pump.Interval=TimeSpan.FromMilliseconds(20);pump.Tick+=delegate {pump.Stop();frame.Continue=false;};
      pump.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
     }
     if(!child.HasExited)child.Kill();
     Check(System.IO.File.Exists(nativeResult)&&System.IO.File.ReadAllText(nativeResult)=="hidden-arrival-and-reset-aligned",
      "Real cross-process hidden HWND must receive centered bounds on CREATE and after restoring saved bounds, before any SHOW");
     Check(nativeLog.Exists(line=>line.Contains("client-arrival-placement-requested")&&line.Contains("visible=False")),
      "Native observer must actually stage the hidden client, rather than relying on the responsive poll");
    }
   }
   if(System.IO.File.Exists(nativeResult))System.IO.File.Delete(nativeResult);
   var windows=new WindowChoice(new IntPtr(11));
   Check(!windows.Consider(new IntPtr(11),false,new Bounds(204,156,2544,1059)),"Hidden preferred HWND cannot stop native enumeration");
   windows.Consider(new IntPtr(22),true,new Bounds(784,180,1920,1080));
   Check(windows.Handle==new IntPtr(22),"A hidden preferred/helper window must not displace the visible client and starve handoff stability");
   windows.Consider(new IntPtr(11),false,new Bounds(204,156,2544,1059));
   Check(windows.Handle==new IntPtr(22),"A larger hidden helper cannot replace an established visible window");
   var selectionDesktop=new FakeDesktop {Window=new IntPtr(22),BoundsMatch=true,Foreground=true};
   var selectionGeometry=new WindowStability();var selectionSession=new HandoffController(selectionDesktop,0,12,selectionGeometry);
   HandoffResult selectedResult=HandoffResult.Waiting;
   for(int i=0;i<=24&&selectedResult==HandoffResult.Waiting;i++) {
    double time=i*.05;var probeChoice=new WindowChoice(new IntPtr(11));
    probeChoice.Consider(new IntPtr(11),false,new Bounds(204,156,2544,1059));
    probeChoice.Consider(new IntPtr(22),true,new Bounds(784,180,1920,1080));
    selectionGeometry.Observe(probeChoice.Handle,selectionDesktop.GetBounds(probeChoice.Handle),time);
    selectedResult=selectionSession.Tick(time,true,probeChoice.Handle);
   }
   Check(selectedResult==HandoffResult.Complete,"Repeated hidden-helper probes cannot starve shared geometry and foreground readiness");
   // Exercise the real WPF handoff boundary without showing a window or launching a client.
   var cfg=new Config {ProcessNames=new string[] {"DragonBootAbsentTestClient"},ProcessPathContains="absent",
    PlayerWidth=800,PlayerHeight=450,MaxWaitSeconds=12};
   string fixture=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"DragonBootVisualRegression-"+Guid.NewGuid().ToString("N"));
   System.IO.Directory.CreateDirectory(fixture);
   System.IO.File.WriteAllText(System.IO.Path.Combine(fixture,"window-placement.json"),"{\"Left\":17,\"Top\":37,\"Width\":800,\"Height\":450}");
   var boot=new BootWindow(cfg,fixture);
   var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
   typeof(BootWindow).GetField("hasOwnedFocus",flags).SetValue(boot,true);
   typeof(BootWindow).GetField("foregroundWindow",flags).SetValue(boot,new Func<IntPtr>(()=>new IntPtr(77)));
   typeof(BootWindow).GetField("clientOwnsForeground",flags).SetValue(boot,new Func<IntPtr,bool>(h=>h==new IntPtr(77)));
   typeof(BootWindow).GetMethod("TrackFocus",flags).Invoke(boot,null);
   Check(!(bool)typeof(BootWindow).GetField("userSwitchedAway",flags).GetValue(boot),
    "A cold client's foreground arrival before target HWND discovery must not close the overlay as a user switch");
   Check(((IHandoffDesktop)typeof(BootWindow).GetField("desktop",flags).GetValue(boot)).CanActivate,
    "Cold client ownership permits alignment before its target handle has been adopted");
   typeof(BootWindow).GetField("foregroundWindow",flags).SetValue(boot,new Func<IntPtr>(()=>new IntPtr(88)));
   typeof(BootWindow).GetMethod("TrackFocus",flags).Invoke(boot,null);
   Check((bool)typeof(BootWindow).GetField("userSwitchedAway",flags).GetValue(boot),"Another application's foreground still cancels automatic handoff");
   typeof(BootWindow).GetField("foregroundWindow",flags).SetValue(boot,new Func<IntPtr>(()=>new IntPtr(77)));
   typeof(BootWindow).GetMethod("TrackFocus",flags).Invoke(boot,null);
   Check((bool)typeof(BootWindow).GetField("userSwitchedAway",flags).GetValue(boot),"A late client arrival cannot erase a genuine user switch");
   typeof(BootWindow).GetField("hasOwnedFocus",flags).SetValue(boot,false);
   typeof(BootWindow).GetField("userSwitchedAway",flags).SetValue(boot,false);
   var visual=new FakeDesktop();
   typeof(BootWindow).GetField("desktop",flags).SetValue(boot,visual);
   var player=(Bounds)typeof(BootWindow).GetField("initialBounds",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(boot);
   Check(!(player.Left==17&&player.Top==37),
    "Cold animation must ignore previously recorded client positions");
   var area=(Bounds)typeof(BootWindow).GetField("screenArea",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(boot);
   Check(Math.Abs(player.Left+player.Right-area.Left-area.Right)<=1&&Math.Abs(player.Top+player.Bottom-area.Top-area.Bottom)<=1,
    "Animation is centered on the selected working area");
   boot.Opacity=.15;
   typeof(BootWindow).GetMethod("BeginHandOff",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)
    .Invoke(boot,new object[] {"regression-test"});
   Check(Math.Abs(boot.Opacity-.15)<.001,"Handoff must not flash an already faded animation back to opaque");
   var advance=typeof(BootWindow).GetMethod("AdvanceReveal",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
   Check((bool)advance.Invoke(boot,null)&&Math.Abs(boot.Opacity-.15)<.001,"No prepared client means no fade to an empty desktop");
   typeof(BootWindow).GetField("revealStarted",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(boot,0.0);
   visual.Window=new IntPtr(42);visual.BoundsMatch=true;
   typeof(BootWindow).GetField("target",flags).SetValue(boot,visual.Window);
   typeof(BootWindow).GetField("ready",flags).SetValue(boot,true);
   cfg.TransitionStart=1;cfg.TransitionEnd=1.01;
   Check(!(bool)advance.Invoke(boot,null)&&boot.Opacity<=.15,"Prepared reveal never increases the current animation opacity");
   var clock=(System.Diagnostics.Stopwatch)typeof(BootWindow).GetField("wall",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(boot);
   clock.Start();System.Threading.Thread.Sleep(25);
   Check((bool)advance.Invoke(boot,null)&&boot.Opacity==0,"Prepared reveal ends transparent without an opaque rebound");
   visual.BoundsMatch=false;
   boot.Opacity=.4;
   Check(!(bool)advance.Invoke(boot,null)&&boot.Opacity<=.4,
    "A reveal must never return to the animation's last frame after the real client has been revealed");
   Check((double)typeof(BootWindow).GetField("revealStarted",flags).GetValue(boot)<0,"Geometry drift invalidates the prepared reveal");
   visual.BoundsMatch=true;
   typeof(BootWindow).GetField("hasOwnedFocus",flags).SetValue(boot,true);
   typeof(BootWindow).GetMethod("CompleteHandOff",flags).Invoke(boot,null);
   Check(visual.Reveals==1&&boot.Opacity<=.4&&!((bool)typeof(BootWindow).GetField("closing",flags).GetValue(boot)),
    "A resumed fade must not rewind an already revealed client back to the animation");
   visual.Covered=false;
   Check(!(bool)advance.Invoke(boot,null)&&boot.Opacity<=.4,"Losing coverage pauses without rewinding the last animation frame");
   visual.Covered=true;
   typeof(BootWindow).GetField("revealStarted",flags).SetValue(boot,-1.0);
   visual.RevealWorks=false;
   typeof(BootWindow).GetMethod("CompleteHandOff",flags).Invoke(boot,null);
   Check(!((bool)typeof(BootWindow).GetField("closing",flags).GetValue(boot)),"Failed reveal preparation cannot close the animation");
   typeof(BootWindow).GetField("handoffStarted",flags).SetValue(boot,-13.0);
   typeof(BootWindow).GetMethod("CompleteHandOff",flags).Invoke(boot,null);
   var movie=(System.Windows.Controls.MediaElement)typeof(BootWindow).GetField("media",flags).GetValue(boot);
   Check(movie.Visibility==System.Windows.Visibility.Collapsed,
    "A timed-out handoff shows recovery controls without bringing the movie's last frame back");
   boot.Close();
   var start=BackgroundLaunch.StartInfo(@"C:\Apps\ChatGPT.exe");
   Check(!start.UseShellExecute&&start.EnvironmentVariables["CODEX_ELECTRON_START_IN_BACKGROUND"]=="1",
    "Cold startup uses the actual client-supported nonactivating environment branch, not shell activation");
   Check(start.WorkingDirectory==@"C:\Apps"&&start.Arguments=="","Prewarm preserves the client profile and supplies no speculative flags");
   var early=new BootWindow(cfg,fixture);var earlyDesktop=new FakeDesktop();
   typeof(BootWindow).GetField("desktop",flags).SetValue(early,earlyDesktop);
   var tick=typeof(BootWindow).GetMethod("Tick",flags);
   tick.Invoke(early,new object[] {null,EventArgs.Empty});
   tick.Invoke(early,new object[] {null,EventArgs.Empty});
   Check(earlyDesktop.Prewarms==1&&earlyDesktop.Launches==0,
    "Playback prepares the client exactly once before handoff instead of waiting for the last frame to activate it");
   early.Close();
   var pending=new FakeDesktop {Window=new IntPtr(42),Foreground=true};
   Check(Session(pending).Tick(0,true,pending.Window)==HandoffResult.Waiting,
    "Foreground alone must not remove the animation before the client surface is composed");
   var drift=new FakeDesktop {Window=new IntPtr(42),Foreground=true};var drifting=Session(drift);
   drifting.Tick(0,true,drift.Window);drift.BoundsMatch=false;
   Check(drifting.Tick(.31,true,drift.Window)==HandoffResult.Waiting,
    "The same HWND moving after alignment must not pass foreground handoff");
   Check(drift.Alignments==2&&drift.Activations==0,"Drift invalidates alignment and is repaired before activation");
   Check(drifting.Tick(.92,true,drift.Window)==HandoffResult.Waiting&&drifting.Tick(1.23,true,drift.Window)==HandoffResult.Complete,
    "A repaired window needs fresh geometry and foreground stability intervals");
   var duringFlush=new FakeDesktop {Window=new IntPtr(42),Foreground=true,MoveDuringFlush=true};var flushSession=Session(duringFlush);
   flushSession.Tick(0,true,duringFlush.Window);flushSession.Tick(.61,true,duringFlush.Window);
   Check(flushSession.Tick(.92,true,duringFlush.Window)==HandoffResult.Waiting&&duringFlush.Flushes==1,
    "Geometry is checked again after compositor synchronization");
   var geometry=new WindowStability();var rect=new Bounds(-1200,40,800,450);
   Check(!geometry.Observe(new IntPtr(42),rect,0)&&geometry.Observe(new IntPtr(42),rect,.61),"Stable negative-coordinate geometry becomes ready");
   rect.Left+=20;rect.Right+=20;
   Check(!geometry.Observe(new IntPtr(42),rect,.62)&&!geometry.Observe(new IntPtr(42),rect,1),"Same-handle position changes reset readiness");
   rect.Bottom+=20;
   Check(!geometry.Observe(new IntPtr(42),rect,1.01)&&geometry.Observe(new IntPtr(42),rect,1.62),"Same-handle size changes require a fresh interval");
   Check(!geometry.Observe(new IntPtr(43),rect,1.63),"A replacement HWND resets geometry stability");
   geometry.Reset();Check(!geometry.Observe(new IntPtr(43),rect,2.5),"Probe invalidation prevents stale handoff readiness");
   var d=new FakeDesktop();var s=Session(d);
   Check(s.Tick(0,true,IntPtr.Zero)==HandoffResult.Waiting,"Missing client must not close the animation as success");
   Check(d.Launches==1,"Missing client requests activation");
   s.Tick(1,true,IntPtr.Zero);Check(d.Launches==1,"Shell activation is rate limited");
   d.Window=new IntPtr(42);
   Check(s.Tick(2,true,IntPtr.Zero)==HandoffResult.Waiting,"Late window remains covered during presentation");
   Check(s.Tick(2.61,true,IntPtr.Zero)==HandoffResult.Waiting&&s.Tick(2.92,true,IntPtr.Zero)==HandoffResult.Complete,"Late window is recovered after presentation");
   Check(d.Foreground&&d.Activations==1,"Foreground is confirmed before completion");
   Check(d.Alignments==1,"A cold client is aligned to the animation once before completion");
   d=new FakeDesktop {Window=new IntPtr(42),ActivationWorks=false};s=Session(d);
   Check(s.Tick(0,true,d.Window)==HandoffResult.Waiting,"Rejected foreground call is not success");
   Check(s.Tick(13,true,d.Window)==HandoffResult.Failed,"Rejected activation reaches an explicit failure");
   d=new FakeDesktop {Window=new IntPtr(42),ForegroundChanges=false};s=Session(d);
   Check(s.Tick(0,true,d.Window)==HandoffResult.Waiting,"An API success without foreground ownership is not success");
   d=new FakeDesktop {Window=new IntPtr(42),Minimized=true,SwitchDuringProbe=true};s=Session(d);
   Check(s.Tick(0,true,d.Window)==HandoffResult.Background,"Focus is rechecked after a slow responsiveness probe");
   Check(d.Restores==0&&d.Activations==0,"A stale activation permission cannot restore or focus the client");
   d=new FakeDesktop {Window=new IntPtr(42),Minimized=true};s=Session(d);
   Check(s.Tick(0,false,d.Window)==HandoffResult.Background,"Switching away preserves the user's foreground app");
   Check(d.Activations==0&&d.Restores==0&&d.Launches==0&&d.Alignments==0,"Background completion does not move, restore, activate, or launch");
   d=new FakeDesktop {Window=new IntPtr(42),Minimized=true};s=Session(d);
   Check(s.Tick(0,true,d.Window)==HandoffResult.Waiting&&d.Restores==1,"Minimized client is restored only at explicit handoff");
   Check(s.Tick(.61,true,d.Window)==HandoffResult.Waiting&&s.Tick(.92,true,d.Window)==HandoffResult.Complete,"Restored client remains covered until presentation");
   Check(d.Alignments==1,"Minimized client is aligned after restoration");
   d=new FakeDesktop {Window=new IntPtr(42),AlignmentWorks=false};s=Session(d);
   Check(s.Tick(0,true,d.Window)==HandoffResult.Waiting&&d.Activations==0,"A failed alignment operation does not reveal the wrong placement");
   Check(s.Tick(13,true,d.Window)==HandoffResult.Failed,"A alignment failure has a bounded recovery path");
   d=new FakeDesktop {Window=new IntPtr(42),Foreground=true};s=Session(d);
   Check(s.Tick(0,true,d.Window)==HandoffResult.Waiting&&d.Restores==0&&d.Activations==0,"Existing normal or maximized client is left intact");
   Check(s.Tick(.61,true,d.Window)==HandoffResult.Waiting&&s.Tick(.92,true,d.Window)==HandoffResult.Complete&&d.Flushes==1,"Close requires a compositor confirmation");
   d=new FakeDesktop {Window=new IntPtr(42),Foreground=true,PresentationReady=false};s=Session(d);
   s.Tick(0,true,d.Window);
   Check(s.Tick(.61,true,d.Window)==HandoffResult.Waiting&&s.Tick(.92,true,d.Window)==HandoffResult.Waiting,"An unpresented foreground surface must stay covered");
   Check(s.Tick(13,true,d.Window)==HandoffResult.Failed,"Presentation failure has a bounded recovery path");
   d=new FakeDesktop {Window=new IntPtr(42),Foreground=true};s=Session(d);s.Tick(0,true,d.Window);
   d.Responsive=false;s.Tick(.2,true,d.Window);d.Responsive=true;
   Check(s.Tick(.31,true,d.Window)==HandoffResult.Waiting,"Lost responsiveness resets the presentation interval");
   d.Window=new IntPtr(43);
   Check(s.Tick(.62,true,d.Window)==HandoffResult.Waiting,"A replaced client needs its own presentation interval");
   d.ActivationAllowed=false;
   Check(s.Tick(.94,true,d.Window)==HandoffResult.Background&&d.Flushes==0,"Switching away cancels pending presentation without foreground requests");
   d=new FakeDesktop {Window=new IntPtr(42),Responsive=false};s=Session(d);
   Check(s.Tick(0,true,d.Window)==HandoffResult.Waiting&&d.Activations==0,"Unresponsive windows cannot be handed off");
   d=new FakeDesktop();s=Session(d);
   Check(s.Tick(0,false,IntPtr.Zero)==HandoffResult.Waiting&&d.Launches==0,"Do not relaunch into the foreground after user switches away");
   Check(s.Tick(13,false,IntPtr.Zero)==HandoffResult.Failed,"No-window background wait is bounded");
   d=new FakeDesktop();s=Session(d);
   s.Tick(0,true,IntPtr.Zero);s.Tick(4,true,IntPtr.Zero);s.Tick(8,true,IntPtr.Zero);s.Tick(11,true,IntPtr.Zero);
   Check(d.Launches==3,"Missing-window activation retries are bounded");
   Bounds fit=Geometry.FitInside(new Bounds(10,20,1000,1000),1600,900);
   Check(fit.Width==1000&&fit.Height==562&&fit.Left==10&&fit.Top==239,"Thumbnail preserves the source aspect ratio");
   Console.WriteLine("PASS: "+checks+" handoff regression assertions. No desktop UI opened.");return 0;
  } catch(Exception e) { Console.Error.WriteLine(e);return 1; }
 }
}

// Only an isolated, hidden test window is created. No Codex process or UI.
internal static class HiddenArrivalFixture {
 delegate IntPtr WindowProc(IntPtr h,uint msg,IntPtr w,IntPtr l);
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
 struct WindowClass {
  public uint Size,Style;public WindowProc Proc;public int ClassExtra,WindowExtra;
  public IntPtr Instance,Icon,Cursor,Background;public string Menu,Name;public IntPtr SmallIcon;
 }
 [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
 struct Message {public IntPtr Window;public uint Id;public UIntPtr W;public IntPtr L;public uint Time;public int X,Y;public uint Private;}
 [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern ushort RegisterClassEx(ref WindowClass wc);
 [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr data);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr DefWindowProc(IntPtr h,uint m,IntPtr w,IntPtr l);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool PeekMessage(out Message msg,IntPtr h,uint min,uint max,uint remove);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr DispatchMessage(ref Message msg);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Bounds b);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int ht,uint flags);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr h);
 [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern IntPtr GetModuleHandle(string module);
 public static int Run(string result) {
  WindowProc proc=DefWindowProc;var wc=new WindowClass {Proc=proc,Instance=GetModuleHandle(null),Name="Chrome_WidgetWin_1"};
  wc.Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(WindowClass));
  if(RegisterClassEx(ref wc)==0)return 2;
  IntPtr window=CreateWindowEx(0,wc.Name,"DragonBoot hidden regression",0x00CF0000,2215,0,1225,1440,IntPtr.Zero,IntPtr.Zero,wc.Instance,IntPtr.Zero);
  if(window==IntPtr.Zero)return 3;
  bool reset=false;var timeout=System.Diagnostics.Stopwatch.StartNew();
  try {
   while(timeout.Elapsed.TotalSeconds<4) {
    Message msg;while(PeekMessage(out msg,IntPtr.Zero,0,0,1))DispatchMessage(ref msg);
    if(IsWindowVisible(window))return 4;
    Bounds bounds;
    if(GetWindowRect(window,out bounds)&&WindowStability.Same(bounds,new Bounds(100,120,800,450))) {
     if(reset) {System.IO.File.WriteAllText(result,"hidden-arrival-and-reset-aligned");return 0;}
     reset=true;SetWindowPos(window,IntPtr.Zero,2215,0,1225,1440,0x4|0x10);
    }
    System.Threading.Thread.Sleep(5);
   }
   return 5;
  } finally {DestroyWindow(window);GC.KeepAlive(proc);}
 }
}
