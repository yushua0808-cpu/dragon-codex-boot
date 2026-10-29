using System;
using System.Collections.Generic;
using DragonCodexBoot;
class LifecycleTests {
 static long sequence;static int checks;static List<string> results=new List<string>();
 static void Assert(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static ClientSample S(double t,bool process,long window,bool responsive=true,bool known=true){return new ClientSample {Sequence=++sequence,Time=t,Known=known,HasProcesses=process,Window=window,ProcessId=window==0?0:10,Responsive=responsive,Reason="test"};}
 static LaunchAction Step(LaunchLifecycle l,double t,bool p,long w,bool r=true,bool k=true){return l.Step(t,S(t,p,w,r,k));}
 static void Prime(LaunchLifecycle l){for(int i=0;i<=4;i++)Step(l,i*.25,true,11);}
 static void Deterministic() {
  var l=new LaunchLifecycle(60);Prime(l);Assert(l.Ready,"warm readiness");Assert(l.Attempts==0,"warm no activate");l.FinishMedia();Assert(Step(l,1.25,true,11)==LaunchAction.Handoff,"warm handoff");Assert(Step(l,4,false,0)==LaunchAction.None,"no revival after handoff");results.Add("warm-handoff-terminal");
  l=new LaunchLifecycle(60);Step(l,0,false,0);Assert(Step(l,1.9,false,0)==LaunchAction.None,"quiet gap");Assert(Step(l,2,false,0)==LaunchAction.Activate,"cold activation");l.ActivationCompleted(true);l.FinishMedia();for(int i=0;i<4;i++)Assert(Step(l,3+i*.25,true,22)==LaunchAction.None,"cold window stabilization");Assert(Step(l,4,true,22)==LaunchAction.Handoff,"cold one handoff");Assert(l.Attempts==1,"cold attempts");results.Add("cold-delayed-window");
  l=new LaunchLifecycle(60);for(int i=0;i<12;i++)Assert(Step(l,i*.25,true,0)==LaunchAction.None,"exit residue waits");Step(l,3,false,0);Assert(Step(l,4.9,false,0)==LaunchAction.None,"exit gap wait");Assert(Step(l,5,false,0)==LaunchAction.Activate,"exit gap activate");results.Add("exit-residue-settle");
  l=new LaunchLifecycle(60);Step(l,0,true,0);Assert(Step(l,7.9,true,0)==LaunchAction.None,"background wait");Assert(Step(l,8,true,0)==LaunchAction.Activate,"background activation bounded");results.Add("tray-instance-activation");
  l=new LaunchLifecycle(60);Step(l,0,false,0);Step(l,2,false,0);l.ActivationCompleted(true);Step(l,2.5,true,0);Step(l,3,false,0);Assert(Step(l,5,false,0)==LaunchAction.None,"transient pid grace");Assert(Step(l,7,false,0)==LaunchAction.Activate,"one retry");l.ActivationCompleted(true);Assert(Step(l,12,false,0)==LaunchAction.Fail,"retry exhausted");Assert(l.Attempts==2,"two max activations");Assert(Step(l,40,false,0)==LaunchAction.None,"no third retry");results.Add("failed-activation-two-attempt-limit");
  l=new LaunchLifecycle(60);Step(l,0,false,0);Step(l,2,false,0);l.ActivationCompleted(true);for(int i=0;i<4;i++)Step(l,3+i*.25,true,33);l.FinishMedia();Assert(Step(l,4,true,33)==LaunchAction.Handoff,"transient activation successor");Assert(l.Attempts==1,"no retry with successor");results.Add("activation-pid-handoff-to-successor");
  l=new LaunchLifecycle(60);Prime(l);l.FinishMedia();Assert(Step(l,1.25,true,11,false)==LaunchAction.Handoff,"single missed response at media end tolerated");results.Add("media-end-short-responsiveness-lapse");
  l=new LaunchLifecycle(60);Prime(l);Step(l,1.25,true,11,false);Step(l,1.5,true,11,false);Step(l,1.75,true,11,false);l.FinishMedia();Assert(Step(l,2,true,11,false)==LaunchAction.None,"long lapse not handoff");Assert(l.MediaFinished,"finished retained during lapse");Assert(Step(l,2.1,true,11)==LaunchAction.Handoff,"late readiness finishes without replay");results.Add("media-end-late-readiness-no-replay");
  l=new LaunchLifecycle(60);Prime(l);l.FinishMedia();Assert(Step(l,1.25,true,22)==LaunchAction.None,"new hwnd not immediately ready");Step(l,1.5,true,22);Step(l,1.75,true,22);Step(l,2,true,22);Assert(Step(l,2.25,true,22)==LaunchAction.Handoff,"new hwnd restabilized");Assert(l.Window==22,"correct target after change");results.Add("window-replacement");
  l=new LaunchLifecycle(60);Prime(l);Step(l,1.25,false,0);Assert(!l.Ready,"destroyed window revokes readiness");Assert(Step(l,3.25,false,0)==LaunchAction.Fail,"closed established client not restarted");Assert(l.Attempts==0,"user closed no revival");results.Add("client-exit-after-ready-no-relaunch");
  l=new LaunchLifecycle(60);Step(l,0,false,0);l.Cancel();Assert(Step(l,2,false,0)==LaunchAction.None,"cancel no launch");l.ActivationCompleted(true);l.FinishMedia();Assert(l.Terminal&&!l.MediaFinished,"late callbacks ignored");results.Add("cancellation-and-late-callbacks");
  l=new LaunchLifecycle(60);Step(l,0,false,0);Assert(Step(l,20,false,0,true,false)==LaunchAction.None,"unknown process state no launch");Assert(Step(l,60,false,0,true,false)==LaunchAction.Fail,"unknown state exact timeout");results.Add("unknown-probe-fails-closed");
  l=new LaunchLifecycle(60);for(int i=0;i<=4;i++)Step(l,i*.25,true,11,true,false);l.FinishMedia();Assert(Step(l,1.25,true,11,true,false)==LaunchAction.Handoff,"verified window survives unrelated inaccessible process");Assert(l.Attempts==0,"incomplete process enumeration never triggers redundant activation");results.Add("verified-window-with-incomplete-enumeration");
  l=new LaunchLifecycle(60);var stale=S(0,false,0);l.Step(0,stale);Assert(l.Step(4,stale)==LaunchAction.None,"stale snapshot no activation");Assert(l.Attempts==0,"no stale launch");results.Add("stale-snapshot");
  l=new LaunchLifecycle(60);Step(l,0,true,0);l.FinishMedia();Assert(Step(l,60,true,0)==LaunchAction.Fail,"deadline 60 not 80");Assert(l.MediaFinished,"deadline no playback rewind");results.Add("deadline-media-once");
  l=new LaunchLifecycle(60);Step(l,0,false,0);Step(l,2,false,0);l.ActivationCompleted(false);Assert(Step(l,2.25,false,0)==LaunchAction.Fail,"hresult fail reported");Assert(l.Attempts==1,"hresult no loop");results.Add("activation-hresult-failure");
 }
 static int Main() {
  try { Deterministic(); Console.WriteLine("PASS assertions="+checks+" cases="+results.Count); return 0; }
  catch(Exception error) { Console.Error.WriteLine(error); return 1; }
 }
}
