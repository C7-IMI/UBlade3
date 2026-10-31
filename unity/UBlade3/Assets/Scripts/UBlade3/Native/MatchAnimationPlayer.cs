using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Assets;

namespace UBlade3.Native
{
    // N15: one skater/goalie's plan-driven clip player. It resolves a plan animation id through
    // MatchAnimationRegistry, starts the assembly's segment 0 like AnimPlayer::SetClip, advances at
    // the segment's own standard fps, loops/finishes per the segment's Loop field, fires the
    // AnimAssm trigger events when their frame is crossed, and poses the existing MatchSkaterView
    // through the same SkaterCharacter.PoseFrame/ApplyTo path the match already renders.
    //
    // Exact playback timing: AnimPlayer::SetClip (HYPERX 0x00425220) starts at StartFrame and steps
    // delta * StandardFps * playbackRate. StartClipPlayback's flRate argument is passed to
    // SelectAnimationSet as the relative flag (inside it is the SetClip blend argument); it is not
    // the playback speed. The shipped plan action/behparam rate is recorded in RequestedRate below
    // and playback stays at the original 1.0 unless a future SetAnimationSpeed(39) consumer changes
    // it. Deferred flags==1 waits for the current clip to finish, exactly as SelectAnimationSet
    // parks m_pending; other flag values switch immediately.
    public sealed class MatchAnimationPlayer
    {
        public const string Marker = "UBLADE3_NATIVE_ANIMATION_CLIP";

        public readonly int Slot;

        public readonly SkaterCharacter Character;

        public readonly MatchSkaterView View;

        public readonly MatchAnimationRegistry Registry;

        public SkaterAnimationClip CurrentClip;

        public int CurrentIndex = -1;

        public string CurrentName = string.Empty;

        public string Source = "default";

        public bool Temporary;

        public bool Done;

        public bool Infinite;

        public int PassesRemaining = 1;

        public int Frame;

        public double Position;

        public float PlaybackRate = 1.0f;

        public float RequestedRate = -1.0f;

        public int RequestedFlags;

        public int Loops;

        public SkaterAnimationClip PersistentClip;

        public int PersistentIndex = -1;

        public string PersistentName = string.Empty;

        public float PersistentRate = -1.0f;

        public int PersistentFlags;

        public readonly List<string> Trace = new List<string>();

        public readonly List<string> Unsupported = new List<string>();

        public Action<MatchAnimationPlayer, AnimationAssemblyTrigger, int> EventFired;

        public int ClipChanges;

        public int Events;

        public int UnsupportedCount;

        public int DeferredPending;

        public int DeferredApplied;

        public int Restores;

        private readonly List<AnimationAssemblyTrigger> triggers =
            new List<AnimationAssemblyTrigger>();

        private int triggerCursor;

        private int pendingIndex = -1;

        private float pendingRate = -1.0f;

        private int pendingFlags;

        public MatchAnimationPlayer(int slot, MatchSkaterView view, MatchAnimationRegistry registry)
        {
            Slot = slot;
            View = view;
            Registry = registry;

            if (view == null || view.Character == null)
            {
                throw new ArgumentNullException("view");
            }

            Character = view.Character;
            CurrentClip = Character.Animation;
            CurrentName = CurrentClip == null ? string.Empty : CurrentClip.AssemblyName;
            PersistentClip = CurrentClip;
            PersistentName = CurrentName;
            Registry.TryFindIndex(CurrentName, out CurrentIndex);
            Registry.TryFindIndex(CurrentName, out PersistentIndex);
            Done = CurrentClip == null;
            Frame = CurrentClip == null ? 0 : CurrentClip.StartFrame;
            Position = Frame;
        }

        public bool IsTemporary
        {
            get { return Temporary; }
        }

        public bool TryPlay(int index, float rate, int flags, string source)
        {
            RequestedRate = rate;
            RequestedFlags = flags;

            AnimationAssembly assembly;
            string name;
            string error;
            SkaterAnimationClip clip = Registry.TryLoad(index, View, out name, out error);

            if (clip == null)
            {
                UnsupportedCount++;
                Unsupported.Add("slot=" + Slot + " id=" + index + " name=" + (name ?? string.Empty)
                                 + " reason=" + error + " source=" + source);
                Trace.Add("unsupported id=" + index + " source=" + source + " reason=" + error);
                return false;
            }

            if (flags == 1 && CurrentClip != null && !Done)
            {
                pendingIndex = index;
                pendingRate = rate;
                pendingFlags = flags;
                DeferredPending++;
                Trace.Add("deferred id=" + index + " name=" + name + " source=" + source
                          + " current=" + CurrentName);
                return false;
            }

            if (CurrentClip != null && !Done && CurrentIndex == index && !Temporary)
            {
                // The original SelectAnimationSet does not restart the playing set; the request is
                // still consumed/applied by the consumer.
                Trace.Add("same-clip id=" + index + " name=" + name + " source=" + source);
                return true;
            }

            if (CurrentClip != null && !Done && CurrentIndex == index && Temporary
                && string.Equals(Source, source, StringComparison.Ordinal))
            {
                Trace.Add("same-clip id=" + index + " name=" + name + " source=" + source);
                return true;
            }

            StartClip(index, name, clip, rate, flags, source);
            return true;
        }

        public bool SetPersistent(int index, float rate, int flags, string source)
        {
            AnimationAssembly assembly;
            string name;
            string error;
            SkaterAnimationClip clip = Registry.TryLoad(index, View, out name, out error);

            if (clip == null)
            {
                UnsupportedCount++;
                Unsupported.Add("slot=" + Slot + " id=" + index + " name=" + (name ?? string.Empty)
                                 + " reason=" + error + " source=" + source);
                Trace.Add("unsupported persistent id=" + index + " reason=" + error);
                return false;
            }

            PersistentIndex = index;
            PersistentName = name;
            PersistentClip = clip;
            PersistentRate = rate;
            PersistentFlags = flags;
            Trace.Add("persistent id=" + index + " name=" + name + " rate="
                      + rate.ToString("0.###", CultureInfo.InvariantCulture) + " flags=" + flags);

            if (CurrentClip == null || Done || Temporary)
            {
                StartClip(index, name, clip, rate, flags, "persistent");
                return true;
            }

            return false;
        }

        public void RestorePersistent(string reason)
        {
            Restores++;

            if (PersistentClip == null || PersistentIndex < 0)
            {
                Done = true;
                Trace.Add("restore no-persistent reason=" + reason);
                return;
            }

            StartClip(PersistentIndex, PersistentName, PersistentClip, PersistentRate,
                PersistentFlags, "persistent");
            Trace.Add("restore persistent=" + PersistentName + " reason=" + reason);
        }

        private void StartClip(int index, string name, SkaterAnimationClip clip, float rate, int flags,
            string source)
        {
            if (clip == null)
            {
                return;
            }

            CurrentIndex = index;
            CurrentName = name;
            CurrentClip = clip;
            Source = source;
            Temporary = !string.Equals(source, "persistent", StringComparison.Ordinal)
                        && !string.Equals(source, "default", StringComparison.Ordinal);
            RequestedRate = rate;
            RequestedFlags = flags;
            PlaybackRate = 1.0f;
            Frame = clip.StartFrame;
            Position = Frame;
            Done = false;
            Infinite = clip.Segment.LoopCount == 0;
            PassesRemaining = clip.Segment.LoopCount > 0 ? clip.Segment.LoopCount : 1;
            Loops = 0;
            triggerCursor = 0;
            pendingIndex = -1;
            triggers.Clear();

            AnimationAssembly assembly;
            string assemblyName;

            if (Registry.TryResolve(index, out assemblyName, out assembly) && assembly != null)
            {
                for (int i = 0; i < assembly.Triggers.Length; i++)
                {
                    if (assembly.Triggers[i].Segment == 0)
                    {
                        triggers.Add(assembly.Triggers[i]);
                    }
                }

                triggers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
            }

            ClipChanges++;
            Trace.Add("clip slot=" + Slot + " id=" + index + " name=" + name + " source=" + source
                      + " range=" + clip.StartFrame + ".." + clip.EndFrame + " fps="
                      + clip.StandardFps.ToString("0.###", CultureInfo.InvariantCulture) + " loop="
                      + clip.Segment.LoopCount + " rate=" + rate.ToString("0.###",
                          CultureInfo.InvariantCulture) + " flags=" + flags);

            // The first frame is posed immediately; the consumer's Pose() call applies it to the
            // match view after the view's own placement/animation clock has run.
            Pose();
        }

        public void Tick(double deltaSeconds)
        {
            if (CurrentClip == null || Done)
            {
                Pose();
                return;
            }

            if (deltaSeconds <= 0.0)
            {
                Pose();
                return;
            }

            double span = CurrentClip.EndFrame - CurrentClip.StartFrame + 1;
            double step = (deltaSeconds * CurrentClip.StandardFps * PlaybackRate);
            int oldFrame = Frame;
            double next = Position + step;
            int guard = 0;

            while (next > CurrentClip.EndFrame + 0.000001 && guard++ < 16)
            {
                FireTriggers(oldFrame, CurrentClip.EndFrame);

                if (Infinite)
                {
                    Loops++;
                    next -= span;
                    oldFrame = CurrentClip.StartFrame - 1;
                    triggerCursor = 0;
                }
                else if (PassesRemaining > 1)
                {
                    PassesRemaining--;
                    Loops++;
                    next -= span;
                    oldFrame = CurrentClip.StartFrame - 1;
                    triggerCursor = 0;
                }
                else
                {
                    Position = CurrentClip.EndFrame;
                    Frame = CurrentClip.EndFrame;
                    Done = true;
                    Trace.Add("done slot=" + Slot + " id=" + CurrentIndex + " name=" + CurrentName
                              + " source=" + Source);
                    break;
                }
            }

            if (!Done)
            {
                Position = next;
                Frame = (int)Math.Floor(next + 0.000001);

                if (Frame < CurrentClip.StartFrame)
                {
                    Frame = CurrentClip.StartFrame;
                }

                if (Frame > CurrentClip.EndFrame)
                {
                    Frame = CurrentClip.EndFrame;
                }

                FireTriggers(oldFrame, Frame);
            }
            else
            {
                if (pendingIndex >= 0)
                {
                    int index = pendingIndex;
                    float rate = pendingRate;
                    int flags = pendingFlags;
                    pendingIndex = -1;
                    DeferredApplied++;

                    string name;
                    string error;
                    AnimationAssembly assembly;
                    SkaterAnimationClip clip = Registry.TryLoad(index, View, out name, out error);

                    if (clip != null)
                    {
                        StartClip(index, name, clip, rate, flags, "deferred");
                    }
                    else
                    {
                        RestorePersistent("deferred-load-failed");
                    }
                }
                else
                {
                    RestorePersistent("clip-done");
                }
            }

            Pose();
        }

        private void FireTriggers(int fromFrame, int toFrame)
        {
            if (triggers.Count == 0 || toFrame < fromFrame)
            {
                return;
            }

            while (triggerCursor < triggers.Count && triggers[triggerCursor].Frame <= fromFrame)
            {
                triggerCursor++;
            }

            while (triggerCursor < triggers.Count && triggers[triggerCursor].Frame <= toFrame)
            {
                AnimationAssemblyTrigger trigger = triggers[triggerCursor];
                Events++;

                if (EventFired != null)
                {
                    EventFired(this, trigger, Frame);
                }

                Trace.Add("event slot=" + Slot + " frame=" + trigger.Frame + " action="
                          + trigger.ActionName + " segment=" + trigger.Segment + " sync="
                          + (trigger.HasSync ? trigger.SyncAssembly + ":" + trigger.SyncSegment + ":"
                              + trigger.SyncFrame : "none"));
                triggerCursor++;
            }
        }

        public void Pose()
        {
            if (View == null || Character == null || CurrentClip == null || Character.UnityMesh == null)
            {
                return;
            }

            int frame = Frame;

            if (frame < CurrentClip.StartFrame || frame > CurrentClip.EndFrame)
            {
                frame = Math.Max(CurrentClip.StartFrame, Math.Min(CurrentClip.EndFrame, frame));
            }

            try
            {
                Character.Animation = CurrentClip;
                Character.YawRadians = View.Yaw;
                Character.PoseFrame(frame);
                Character.Mesh.ApplyTo(Character.UnityMesh);
            }
            catch (Exception exception)
            {
                UnsupportedCount++;
                Unsupported.Add("slot=" + Slot + " pose id=" + CurrentIndex + " reason="
                                + exception.GetType().Name + ":" + exception.Message);
                Done = true;
            }
        }

        public string Snapshot()
        {
            return "slot=" + Slot + " id=" + CurrentIndex + " name=" + CurrentName
                   + " frame=" + Frame + " range=" + (CurrentClip == null ? "-"
                       : CurrentClip.StartFrame + ".." + CurrentClip.EndFrame)
                   + " source=" + Source + " temp=" + (Temporary ? "True" : "False")
                   + " loops=" + Loops + " done=" + (Done ? "True" : "False")
                   + " persistent=" + PersistentName;
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Marker).Append(" slot=").Append(Slot);
            text.Append(" clip=").Append(CurrentName);
            text.Append(" id=").Append(CurrentIndex);
            text.Append(" frame=").Append(Frame);
            text.Append(" source=").Append(Source);
            text.Append(" temporary=").Append(Temporary ? "True" : "False");
            text.Append(" clipChanges=").Append(ClipChanges);
            text.Append(" events=").Append(Events);
            text.Append(" loops=").Append(Loops);
            text.Append(" done=").Append(Done ? "True" : "False");
            text.Append(" unsupported=").Append(UnsupportedCount);
            text.Append(" deferred=").Append(DeferredPending);
            text.Append(" deferredApplied=").Append(DeferredApplied);
            text.Append(" persistent=").Append(PersistentName);
            text.Append(" requestedRate=").Append(RequestedRate.ToString("0.###",
                CultureInfo.InvariantCulture));
            text.Append(" playbackRate=").Append(PlaybackRate.ToString("0.###",
                CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
