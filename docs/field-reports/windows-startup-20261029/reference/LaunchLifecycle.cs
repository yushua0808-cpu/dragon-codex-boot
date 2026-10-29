using System;

namespace DragonCodexBoot {
 public sealed class ClientSample {
  public long Sequence,Window;
  public int ProcessId;
  public double Time;
  public bool Known,HasProcesses,Responsive;
  public string Reason;
 }
 public enum LaunchAction { None,Activate,Handoff,Fail }
 // Pure policy shared by the real window and deterministic fault tests.
 public sealed class LaunchLifecycle {
  public readonly double Deadline;
  public bool MediaFinished { get; private set; }
  public bool Terminal { get; private set; }
  public bool Ready { get; private set; }
  public int Attempts { get; private set; }
  public long Window { get; private set; }
  public int ProcessId { get; private set; }
  public string Reason { get; private set; }
  public bool Pending { get; private set; }
  private long sequence=-1;
  private double stableSince,lastGood=-100,absentSince=-1,processWaitSince=-1,lastActivation=-100;
  private int goodSamples;
  private bool everReady,activationFailed;
  public LaunchLifecycle(double deadline) { Deadline=deadline;Reason="observing-client"; }
  public void FinishMedia() { if(!Terminal)MediaFinished=true; }
  public void Cancel() { Terminal=true;Ready=false;Reason="cancelled"; }
  public void ActivationCompleted(bool success) { if(Terminal)return;Pending=false;activationFailed=!success; }
  private LaunchAction End(string reason,LaunchAction action) {Terminal=true;Reason=reason;return action;}
  private LaunchAction Activate(double now) {Attempts++;Pending=true;lastActivation=now;Reason=Attempts==1?"activate-client":"retry-after-client-exit";return LaunchAction.Activate;}
  public LaunchAction Step(double now,ClientSample sample) {
   if(Terminal)return LaunchAction.None;
   if(now>=Deadline)return End("startup-timeout",LaunchAction.Fail);
   // An inaccessible unrelated namesake must not veto an independently verified window.
   // Incomplete process enumeration still prevents activation when there is no window.
   bool fresh=sample!=null&&(sample.Known||sample.Window!=0)&&now-sample.Time<=2;
   if(!fresh) {Ready=false;absentSince=-1;processWaitSince=-1;Reason="probe-unavailable";return LaunchAction.None;}
   if(sample.Sequence!=sequence) {
    sequence=sample.Sequence;
    if(sample.Window!=Window||sample.ProcessId!=ProcessId) {
     Window=sample.Window;ProcessId=sample.ProcessId;stableSince=sample.Time;lastGood=-100;goodSamples=0;
    }
    if(Window==0) {goodSamples=0;lastGood=-100;}
    else if(sample.Responsive) {
     if(sample.Time-lastGood>1.25) {stableSince=sample.Time;goodSamples=0;}
     lastGood=sample.Time;goodSamples++;
    }
   }
   Ready=Window!=0&&goodSamples>=3&&now-stableSince>=1&&now-lastGood<=1.25;
   if(Ready)everReady=true;
   // A small responsiveness lapse may retain readiness; final handoff requires a recent good probe.
   if(MediaFinished&&Ready&&now-lastGood<=.75)return End("handoff-ready",LaunchAction.Handoff);
   if(Window!=0) {absentSince=-1;processWaitSince=-1;Reason=Ready?"waiting-for-media":"waiting-for-responsive-window";return LaunchAction.None;}
   if(sample.HasProcesses) {
    absentSince=-1;
    if(processWaitSince<0)processWaitSince=now;
    Reason="waiting-for-existing-process";
    // A long-lived tray/background instance still needs a normal activation request.
    if(Attempts==0&&!everReady&&now-processWaitSince>=8)return Activate(now);
    return LaunchAction.None;
   }
   processWaitSince=-1;
   if(absentSince<0)absentSince=now;
   Reason="waiting-for-exit-settle";
   if(everReady&&now-absentSince>=2)return End("client-exited-before-handoff",LaunchAction.Fail);
   if(Pending)return LaunchAction.None;
   if(activationFailed)return End("activation-failed",LaunchAction.Fail);
   if(now-absentSince<2)return LaunchAction.None;
   if(Attempts==0)return Activate(now);
   // A transient activation PID may forward to another instance. Wait for the whole app,
   // not just that PID, and allow only one recovery attempt inside the original deadline.
   if(now-lastActivation<5)return LaunchAction.None;
   if(Attempts==1&&!everReady)return Activate(now);
   if(Attempts>=2)return End("client-exited-after-retry",LaunchAction.Fail);
   return LaunchAction.None;
  }
 }
}
