using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Assets;

namespace UBlade3.Native
{
    // N15/N16: one skater/goalie's plan-driven clip player. It resolves plan animation ids through
    // MatchAnimationRegistry, starts the assembly's segment like AnimPlayer::SetClip, advances at
    // that segment's own standard fps, loops/finishes per the segment Loop field, fires the AnimAssm
    // trigger events when their frame is crossed, follows a sync trigger to its target
    // assembly/segment/frame, advances to the next segment on clip end, and poses the existing
    // MatchSkaterView through SkaterCharacter.PoseFrame/ApplyTo.
    //
    // Exact timing: AnimPlayer::SetClip (HYPERX 0x00425220) starts at StartFrame and steps
    // delta * StandardFps * playbackRate. StartClipPlayback's flRate is the SetClip blend argument,
    // not playback speed, so it is recorded as RequestedRate and playback remains 1.0. Deferred
    // flags==1 waits for the current clip to finish, as SelectAnimationSet parks m_pending.
    // Trigger transitions are hard switches: the blend/crossfade interpolation is not represented in
    // the CPU pose path and is documented as a gap rather than faked.
    public sealed class MatchAnimationPlayer
    {
        public const string Marker = "UBLADE3_NATIVE_ANIMATION_CLIP";

        public readonly int Slot;

        public readonly SkaterCharacter Character;

        public readonly MatchSkaterView View;

        public readonly MatchAnimationRegistry Registry;

        public SkaterAnimationClip CurrentClip;

        public AnimationAssembly CurrentAssembly;

        public int CurrentSegmentIndex;

        public AnimationAssemblySegment CurrentSegment;

        public int CurrentIndex = -1;

        public string CurrentName = string.Empty;

        public string Source = "default";

        public bool Temporary;

        public bool Done;

        public bool Infinite;

        public int PassesRemaining = 1;

        public int Frame;

        public double Position;

        public int StartFrame;

        public int EndFrame;

        public float StandardFps = 30.0f;

        public float PlaybackRate = 1.0f;

        public float RequestedRate = -1.0f;

        public int RequestedFlags;

        public int Loops;

        public SkaterAnimationClip PersistentClip;

        public AnimationAssembly PersistentAssembly;

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

        public int SyncTransitions;

        public int SyncFailures;

        public int SegmentTransitions;

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
            Registry.TryResolve(CurrentIndex, out string resolvedName, out CurrentAssembly);
            PersistentAssembly = CurrentAssembly;
            CurrentSegment = CurrentClip == null ? null : CurrentClip.Segment;
            CurrentSegmentIndex = 0;
            StartFrame = CurrentSegment == null ? 0 : CurrentSegment.StartFrame;
            EndFrame = CurrentSegment == null ? 0 : CurrentSegment.EndFrame;
            StandardFps = CurrentSegment == null ? 30.0f : CurrentSegment.StandardFps;
            Done = CurrentClip == null;
            Frame = StartFrame;
            Position = Frame;
            LoadTriggersForSegment();
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
            Registry.TryResolve(index, out string resolvedName, out assembly);

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
                Trace.Add("same-clip id=" + index + " name=" + name + " source=" + source);
                return true;
            }

            if (CurrentClip != null && !Done && CurrentIndex == index && Temporary
                && string.Equals(Source, source, StringComparison.Ordinal))
            {
                Trace.Add("same-clip id=" + index + " name=" + name + " source=" + source);
                return true;
            }

            StartClip(index, name, clip, assembly, 0, -1, rate, flags, source);
            return true;
        }

        public bool SetPersistent(int index, float rate, int flags, string source)
        {
            AnimationAssembly assembly;
            string name;
            string error;
            SkaterAnimationClip clip = Registry.TryLoad(index, View, out name, out error);
            Registry.TryResolve(index, out string resolvedName, out assembly);

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
            PersistentAssembly = assembly;
            PersistentRate = rate;
            PersistentFlags = flags;
            Trace.Add("persistent id=" + index + " name=" + name + " rate="
                      + rate.ToString("0.###", CultureInfo.InvariantCulture) + " flags=" + flags);

            if (CurrentClip == null || Done || Temporary)
            {
                StartClip(index, name, clip, assembly, 0, -1, rate, flags, "persistent");
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

            StartClip(PersistentIndex, PersistentName, PersistentClip, PersistentAssembly, 0, -1,
                PersistentRate, PersistentFlags, "persistent");
            Trace.Add("restore persistent=" + PersistentName + " reason=" + reason);
        }

        private void StartClip(int index, string name, SkaterAnimationClip clip,
            AnimationAssembly assembly, int segmentIndex, int startFrameOverride, float rate, int flags,
            string source)
        {
            if (clip == null)
            {
                return;
            }

            AnimationAssemblySegment segment = null;

            if (assembly != null && segmentIndex >= 0 && segmentIndex < assembly.Segments.Length)
            {
                segment = assembly.Segments[segmentIndex];
            }
            else if (segmentIndex == 0 && clip.Segment != null)
            {
                segment = clip.Segment;
            }

            if (segment == null)
            {
                SyncFailures++;
                UnsupportedCount++;
                Unsupported.Add("slot=" + Slot + " id=" + index + " segment=" + segmentIndex
                                 + " reason=no-segment");
                Trace.Add("unsupported segment id=" + index + " segment=" + segmentIndex);
                return;
            }

            if (segment.EndFrame >= clip.Clips.AnimationHeader.FrameCount
                || segment.StartFrame < 0 || segment.EndFrame < segment.StartFrame)
            {
                SyncFailures++;
                UnsupportedCount++;
                Unsupported.Add("slot=" + Slot + " id=" + index + " segment=" + segmentIndex
                                 + " reason=range " + segment.StartFrame + ".." + segment.EndFrame
                                 + " frames=" + clip.Clips.AnimationHeader.FrameCount);
                return;
            }

            CurrentIndex = index;
            CurrentName = name;
            CurrentClip = clip;
            CurrentAssembly = assembly;
            CurrentSegment = segment;
            CurrentSegmentIndex = segmentIndex;
            Source = source;
            Temporary = !string.Equals(source, "persistent", StringComparison.Ordinal)
                        && !string.Equals(source, "default", StringComparison.Ordinal);
            RequestedRate = rate;
            RequestedFlags = flags;
            PlaybackRate = 1.0f;
            StartFrame = startFrameOverride >= 0 ? startFrameOverride : segment.StartFrame;
            EndFrame = segment.EndFrame;

            if (StartFrame < segment.StartFrame)
            {
                StartFrame = segment.StartFrame;
            }

            if (StartFrame > EndFrame)
            {
                StartFrame = EndFrame;
            }

            StandardFps = segment.StandardFps;
            Frame = StartFrame;
            Position = Frame;
            Done = false;
            Infinite = segment.LoopCount == 0;
            PassesRemaining = segment.LoopCount > 0 ? segment.LoopCount : 1;
            Loops = 0;
            triggerCursor = 0;
            pendingIndex = -1;
            LoadTriggersForSegment();
            ClipChanges++;
            Trace.Add("clip slot=" + Slot + " id=" + index + " name=" + name + " source=" + source
                      + " segment=" + segmentIndex + " range=" + StartFrame + ".." + EndFrame
                      + " fps=" + StandardFps.ToString("0.###", CultureInfo.InvariantCulture)
                      + " loop=" + segment.LoopCount + " rate=" + rate.ToString("0.###",
                          CultureInfo.InvariantCulture) + " flags=" + flags);
            Pose();
        }

        private void LoadTriggersForSegment()
        {
            triggers.Clear();

            if (CurrentAssembly == null)
            {
                return;
            }

            for (int i = 0; i < CurrentAssembly.Triggers.Length; i++)
            {
                if (CurrentAssembly.Triggers[i].Segment == CurrentSegmentIndex)
                {
                    triggers.Add(CurrentAssembly.Triggers[i]);
                }
            }

            triggers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }

        public void Tick(double deltaSeconds)
        {
            if (CurrentClip == null || CurrentSegment == null || Done)
            {
                Pose();
                return;
            }

            if (deltaSeconds <= 0.0)
            {
                Pose();
                return;
            }

            double span = EndFrame - StartFrame + 1;
            double step = deltaSeconds * StandardFps * PlaybackRate;
            int oldFrame = Frame;
            double next = Position + step;
            int guard = 0;

            while (next > EndFrame + 0.000001 && guard++ < 16)
            {
                if (FireTriggers(oldFrame, EndFrame))
                {
                    Pose();
                    return;
                }

                if (Infinite)
                {
                    Loops++;
                    next -= span;
                    oldFrame = StartFrame - 1;
                    triggerCursor = 0;
                }
                else if (PassesRemaining > 1)
                {
                    PassesRemaining--;
                    Loops++;
                    next -= span;
                    oldFrame = StartFrame - 1;
                    triggerCursor = 0;
                }
                else
                {
                    Position = EndFrame;
                    Frame = EndFrame;
                    Done = true;
                    Trace.Add("done slot=" + Slot + " id=" + CurrentIndex + " name=" + CurrentName
                              + " segment=" + CurrentSegmentIndex + " source=" + Source);
                    break;
                }
            }

            if (!Done)
            {
                Position = next;
                Frame = (int)Math.Floor(next + 0.000001);

                if (Frame < StartFrame)
                {
                    Frame = StartFrame;
                }

                if (Frame > EndFrame)
                {
                    Frame = EndFrame;
                }

                if (FireTriggers(oldFrame, Frame))
                {
                    Pose();
                    return;
                }
            }
            else
            {
                if (CurrentAssembly != null
                    && CurrentSegmentIndex + 1 < CurrentAssembly.Segments.Length)
                {
                    SegmentTransitions++;
                    Trace.Add("next-segment slot=" + Slot + " id=" + CurrentIndex + " from="
                              + CurrentSegmentIndex + " to=" + (CurrentSegmentIndex + 1));
                    StartClip(CurrentIndex, CurrentName, CurrentClip, CurrentAssembly,
                        CurrentSegmentIndex + 1, -1, RequestedRate, RequestedFlags, Source);
                    Pose();
                    return;
                }

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
                    Registry.TryResolve(index, out string resolvedName, out assembly);

                    if (clip != null)
                    {
                        StartClip(index, name, clip, assembly, 0, -1, rate, flags, "deferred");
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

        private bool FireTriggers(int fromFrame, int toFrame)
        {
            if (triggers.Count == 0 || toFrame < fromFrame)
            {
                return false;
            }

            while (triggerCursor < triggers.Count && triggers[triggerCursor].Frame <= fromFrame)
            {
                triggerCursor++;
            }

            while (triggerCursor < triggers.Count && triggers[triggerCursor].Frame <= toFrame)
            {
                AnimationAssemblyTrigger trigger = triggers[triggerCursor];
                Events++;
                bool sync = trigger.HasSync && TrySync(trigger);

                if (EventFired != null)
                {
                    EventFired(this, trigger, Frame);
                }

                Trace.Add("event slot=" + Slot + " frame=" + trigger.Frame + " action="
                          + trigger.ActionName + " segment=" + trigger.Segment + " sync="
                          + (trigger.HasSync ? trigger.SyncAssembly + ":" + trigger.SyncSegment + ":"
                              + trigger.SyncFrame : "none") + " applied=" + (sync ? "True" : "False"));
                triggerCursor++;

                if (sync)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TrySync(AnimationAssemblyTrigger trigger)
        {
            if (!trigger.HasSync || string.IsNullOrEmpty(trigger.SyncAssembly))
            {
                SyncFailures++;
                return false;
            }

            if (!Registry.TryFindIndex(trigger.SyncAssembly, out int syncIndex))
            {
                SyncFailures++;
                Trace.Add("sync-missing slot=" + Slot + " assembly=" + trigger.SyncAssembly);
                return false;
            }

            if (!Registry.TryResolve(syncIndex, out string syncName, out AnimationAssembly syncAssembly))
            {
                SyncFailures++;
                return false;
            }

            string error;
            SkaterAnimationClip syncClip = Registry.TryLoad(syncIndex, View, out syncName, out error);

            if (syncClip == null)
            {
                SyncFailures++;
                Trace.Add("sync-load-failed slot=" + Slot + " assembly=" + trigger.SyncAssembly
                          + " reason=" + error);
                return false;
            }

            StartClip(syncIndex, syncName, syncClip, syncAssembly, trigger.SyncSegment,
                trigger.SyncFrame, RequestedRate, RequestedFlags, "sync");
            SyncTransitions++;
            Trace.Add("sync slot=" + Slot + " from=" + CurrentName + " to=" + syncName
                      + " segment=" + trigger.SyncSegment + " frame=" + trigger.SyncFrame);
            return true;
        }

        public void Pose()
        {
            if (View == null || Character == null || CurrentClip == null || Character.UnityMesh == null)
            {
                return;
            }

            int frame = Frame;

            if (frame < StartFrame || frame > EndFrame)
            {
                frame = Math.Max(StartFrame, Math.Min(EndFrame, frame));
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
                   + " frame=" + Frame + " range=" + StartFrame + ".." + EndFrame
                   + " segment=" + CurrentSegmentIndex + " source=" + Source
                   + " temp=" + (Temporary ? "True" : "False")
                   + " loops=" + Loops + " done=" + (Done ? "True" : "False")
                   + " persistent=" + PersistentName;
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Marker).Append(" slot=").Append(Slot);
            text.Append(" clip=").Append(CurrentName);
            text.Append(" id=").Append(CurrentIndex);
            text.Append(" segment=").Append(CurrentSegmentIndex);
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
            text.Append(" syncTransitions=").Append(SyncTransitions);
            text.Append(" syncFailures=").Append(SyncFailures);
            text.Append(" segmentTransitions=").Append(SegmentTransitions);
            text.Append(" persistent=").Append(PersistentName);
            text.Append(" requestedRate=").Append(RequestedRate.ToString("0.###",
                CultureInfo.InvariantCulture));
            text.Append(" playbackRate=").Append(PlaybackRate.ToString("0.###",
                CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
