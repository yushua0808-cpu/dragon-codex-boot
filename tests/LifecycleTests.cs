using System;
using System.IO;
using System.Web.Script.Serialization;
using DragonCodexBoot;
class LifecycleTests {
 static int checks;
 static void Check(bool b,string label) { if(!b)throw new Exception(label);checks++; }
 public static int Main() {
  try {
   var recovery=new WindowLifecycle();
   Check(!recovery.RequestRecovery(false,20),"recovery needs initial launch");
   recovery.RequestLaunch(false);
   Check(!recovery.RequestRecovery(false,9),"recovery waits for startup grace");
   Check(!recovery.RequestRecovery(true,10),"usable window suppresses recovery");
   Check(recovery.RequestRecovery(false,10),"explicit recovery allowed after grace");
   Check(!recovery.RequestRecovery(false,19.9),"recovery double click suppressed");
   Check(recovery.RequestRecovery(false,20),"second explicit recovery allowed");
   Check(!recovery.RequestRecovery(false,40),"bounded recovery attempts");
   var life=new WindowLifecycle();
   Check(!WindowLifecycle.CanBind(true,false),"transient cold window cannot be moved");
   Check(WindowLifecycle.CanBind(true,true),"stable window may bind");
   Check(!WindowLifecycle.CanBind(false,true),"disabled binding remains disabled");
   Check(!life.RequestLaunch(true),"existing window suppresses launch");
   Check(life.RequestLaunch(false),"cold launch once");
   Check(!life.RequestLaunch(false),"no retry launch");
   life.Observe("hwnd1:pid1",true,0);Check(!life.Ready,"first sample insufficient");
   life.Observe("hwnd1:pid1",true,.59);Check(!life.Ready,"stability interval");
   life.Observe("hwnd1:pid1",true,.61);Check(life.Ready,"stable window ready");
   life.Observe(null,false,.62);Check(!life.Ready,"destroy clears readiness");
   life.Observe("hwnd1:pid1",true,.63);Check(!life.Ready,"returning handle must stabilize again");
   life.Observe("hwnd1:pid1",true,1.3);Check(life.Ready,"recovered window");
   life.Observe("hwnd1:pid2",true,1.31);Check(!life.Ready,"handle reused by different process");
   life.Observe("hwnd2:pid2",true,2);Check(!life.Ready,"replacement must stabilize");
   life.Observe("hwnd2:pid2",true,2.7);Check(life.Ready,"replacement stable");
   life.Observe("hwnd2:pid2",false,2.8);Check(!life.Ready,"hung window rejected");
   Check(!life.Expired(59.99,60)&&life.Expired(60,60),"bounded wait deadline");
   Check(WindowLifecycle.PrematureEnd(7,.2,4.5),"cached end event at .2 seconds must restart");
   Check(!WindowLifecycle.PrematureEnd(7,7.1,4.5),"normal seven-second media end");
   Check(!WindowLifecycle.PrematureEnd(7,4.6,4.5),"intentional hold not treated as early end");
   int[] splash=new int[160*90];
   for(int i=0;i<splash.Length;i++)splash[i]=0xFFFFFF;
   Check(!ContentReadiness.Analyze(splash,160,90).HasContent,"blank white is not content ready");
   for(int y=36;y<54;y++)for(int x=70;x<90;x++)splash[y*160+x]=0;
   Check(!ContentReadiness.Analyze(splash,160,90).HasContent,"centered black logo on white is rejected");
   for(int i=0;i<splash.Length;i++)splash[i]=0;
   for(int y=36;y<54;y++)for(int x=70;x<90;x++)splash[y*160+x]=0xFFFFFF;
   Check(!ContentReadiness.Analyze(splash,160,90).HasContent,"centered light logo on dark is rejected");
   for(int y=15;y<80;y+=8)for(int x=10;x<35;x++)splash[y*160+x]=0xFFFFFF;
   Check(ContentReadiness.Analyze(splash,160,90).HasContent,"distributed sidebar content is recognized");
   var content=new ContentGate();content.Observe(true,0);Check(!content.Ready(0),"one captured frame is insufficient");
   content.Observe(true,.4);Check(content.Ready(.4),"two rendered samples unlock reveal");
   content.Observe(false,.5);Check(!content.Ready(.5),"returning to splash blocks reveal");
   content.Observe(true,.9);content.Observe(true,1.3);Check(!content.Ready(3),"stale capture cannot authorize handoff");
   content.Reset();Check(!content.Ready(3),"replacement target clears rendered readiness");
   Check(!ContentGate.AllowHandoff(false,false,2.99),"advisory briefly retains cover");
   Check(ContentGate.AllowHandoff(false,false,3),"blank capture cannot block handoff beyond three seconds");
   Check(ContentGate.AllowHandoff(false,true,0),"user Enter/Skip bypasses pixel heuristic");
   Check(ContentGate.AllowHandoff(true,false,0),"verified rendering needs no advisory delay");
   content.MarkPresented();content.Observe(false,4);
   Check(content.Presented&&!content.Ready(4),"visible app stays presented when a later image sample is empty");
   content.Reset();Check(!content.Presented,"replacement window must not inherit prior presentation");
   foreach(var area in new Bounds[] {new Bounds(0,0,1920,1032),new Bounds(0,1080,1280,672),new Bounds(-2560,-1440,2560,1392),new Bounds(1920,-900,1600,900),new Bounds(0,0,720,1280)})
    foreach(var size in new int[][] {new[]{1280,720},new[]{1920,1080},new[]{7680,4320}}) {
     Bounds b=Geometry.Fit(area,size[0],size[1]);
     Check(b.Left>=area.Left&&b.Top>=area.Top&&b.Right<=area.Right&&b.Bottom<=area.Bottom,"contained player");
     Check(Math.Abs(b.Width*(double)size[1]/size[0]-b.Height)<2,"preserve aspect");
    }
   Bounds exact=Geometry.Fit(new Bounds(0,1080,1280,720),1280,720);
   var monitors=new[]{new Bounds(0,0,1920,1080),new Bounds(0,1080,1280,720)};
   Bounds bound=Geometry.Contain(new Bounds(1600,900,1200,900),new Bounds(0,0,1920,1032));
   Check(bound.Right<=1920&&bound.Bottom<=1032,"binding clamps oversized/offscreen client");
   bound=Geometry.Contain(new Bounds(100,100,800,600),new Bounds(0,0,1920,1032));
   Check(bound.Left==100&&bound.Top==100&&bound.Width==800&&bound.Height==600,"binding preserves visible client geometry");
   Check(Geometry.BestMonitor(new Bounds(0,1100,1000,600),monitors,0)==1,"auto uses existing app display");
   Check(Geometry.BestMonitor(new Bounds(),monitors,0)==0,"cold launch uses primary");
   Check(Geometry.BestMonitor(new Bounds(-2000,0,400,400),monitors,0)==0,"offscreen app falls back to primary");
   Check(exact.Left==0&&exact.Top==1080,"reported vertical display regression");
   Bounds work=Geometry.Fit(new Bounds(0,1080,1280,672),1280,720);
   Check(work.Left==43&&work.Top==1080&&work.Width==1194&&work.Height==672,"taskbar-aware secondary placement");
   Bounds wrong=Geometry.Centered(new Bounds(0,1080,1920,1080),1280,720);
   Check(wrong.Left==320&&wrong.Top==1260,"reported position implies incorrect monitor extent");
   Console.WriteLine("PASS: "+checks+" lifecycle/multimonitor assertions");return 0;
  } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
 }
}
