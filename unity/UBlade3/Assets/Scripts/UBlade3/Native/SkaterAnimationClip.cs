using System;
using System.Collections.Generic;
using System.Globalization;
using UBlade3.Assets;

namespace UBlade3.Native
{
    // One original animation assembly resolved to its .wba set and its first segment.
    //
    // The mapping is the shipped one, not a guess:
    //   Data\AnimData\AnimAssm.txt is loaded by AnimScriptTable::ParseScript (HYPERX 0x00418210);
    //   an assembly line names the action ("AA_Skate_Std") and the segment lines under it name the
    //   .wba file, its start/end frames and its three frame rates. A playing object selects that
    //   assembly by name (GameBehaviorObject::UpdateLimpAnim HYPERX 0x0041c220, PlanAction anim
    //   handlers, BehParam::SetPersistentAnimation HYPERX 0x0041cb20) and AnimPlayer::SetClip
    //   (HYPERX 0x00425220) starts at segment 0, frame m_wStartFrame, stepping
    //   delta * m_flStandardFps * playbackRate.
    public sealed class SkaterAnimationClip
    {
        // The segment fields AnimPlayer::SetClip and AnimPlayer::UpdateTick read.
        public const string AssemblyRelativePath = "animdata\\AnimAssm.txt";

        private static AnimationAssemblyFile cachedAssemblies;

        private static string cachedAssembliesRoot;

        private SkaterAnimationClip()
        {
        }

        public string AssemblyName { get; private set; }

        public string Description { get; private set; }

        // Segment 0 of the assembly: the clip AnimPlayer::SetClip starts at.
        public AnimationAssemblySegment Segment { get; private set; }

        // The .wba set the segment names, already loaded and validated.
        public AnimationClipSet Clips { get; private set; }

        public string WbaName { get; private set; }

        public string WbaPath { get; private set; }

        public int StartFrame
        {
            get { return Segment.StartFrame; }
        }

        public int EndFrame
        {
            get { return Segment.EndFrame; }
        }

        // AnimNode::m_flStandardFps: the rate AnimPlayer::SetClip steps a clip at
        // (m_state.flStep = clock delta * standardFps * playbackRate).
        public float StandardFps
        {
            get { return Segment.StandardFps; }
        }

        // Every frame from start to end inclusive is displayed: AnimPlayer::CheckLoopPoint
        // (HYPERX 0x00424770) wraps only once the frame has reached the segment end.
        public int DisplayedFrameCount
        {
            get { return (EndFrame - StartFrame) + 1; }
        }

        public float DurationSeconds
        {
            get { return DisplayedFrameCount / StandardFps; }
        }

        // The frame the engine shows after the given playback time: the clip starts at
        // StartFrame and advances int(time * StandardFps) frames, wrapping at the segment end
        // (AnimPlayer::UpdateFrame HYPERX 0x00424880 advances m_state.flPosition by m_state.flStep
        // = clock delta * standardFps * rate and reads int(position); CheckLoopPoint wraps to
        // m_wStartFrame). A preview that samples a pose from a time therefore uses the same frame
        // the shipped player would at that moment.
        public int FrameAtSeconds(double seconds)
        {
            if (StandardFps <= 0f)
            {
                throw new OriginalAssetException(
                    "Assembly \"" + AssemblyName + "\" segment 0 has standard rate " + StandardFps
                    + "; AnimPlayer cannot step it.");
            }

            if (seconds < 0.0)
            {
                seconds = 0.0;
            }

            // A whole-frame time (k / fps) has no exact double representation; a floor of the
            // product can land a hair under k and show the previous frame. The shipped player
            // accumulates float deltas and shows int(position), so at an exact frame boundary the
            // frame it was heading to is the meaningful one: the epsilon (a millionth of a frame)
            // makes k / fps map to k, as the engine would after k ticks of a 1/fps clock.
            double advanced = Math.Floor((seconds * StandardFps) + 0.000001);
            long steps = (long)advanced;
            int span = DisplayedFrameCount;
            int offset = (int)(steps % span);

            return StartFrame + offset;
        }

        // Loads the assembly script and resolves one assembly + its segment 0 + its .wba set.
        public static SkaterAnimationClip Load(OriginalAssetRoot root, string assemblyName)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            return Load(root, LoadAssemblies(root), assemblyName);
        }

        public static SkaterAnimationClip Load(OriginalAssetRoot root, AnimationAssemblyFile assemblies, string assemblyName)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            if (assemblies == null)
            {
                throw new ArgumentNullException("assemblies");
            }

            if (string.IsNullOrEmpty(assemblyName))
            {
                throw new ArgumentException("assembly name is empty", "assemblyName");
            }

            AnimationAssembly assembly = null;

            for (int i = 0; i < assemblies.Assemblies.Length; i++)
            {
                if (string.Equals(assemblies.Assemblies[i].Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    assembly = assemblies.Assemblies[i];
                    break;
                }
            }

            if (assembly == null)
            {
                throw new OriginalAssetException(
                    "Animation assembly \"" + assemblyName + "\" is not in " + assemblies.SourceName
                    + " (" + assemblies.Assemblies.Length + " assemblies).");
            }

            if (assembly.Segments.Length == 0)
            {
                throw new OriginalAssetException(
                    "Animation assembly \"" + assemblyName + "\" has no segments; the player would start no clip.");
            }

            AnimationAssemblySegment segment = assembly.Segments[0];

            if (string.IsNullOrEmpty(segment.FileName))
            {
                throw new OriginalAssetException(
                    "Animation assembly \"" + assemblyName + "\" segment 0 names no .wba file.");
            }

            string wbaRelative = "animdata\\" + segment.FileName + ".wba";
            string wbaPath = root.ResolveDataFile(wbaRelative);

            if (wbaPath == null)
            {
                throw new OriginalAssetException(
                    "Animation assembly \"" + assemblyName + "\" segment 0 names \"" + segment.FileName
                    + "\", which has no .wba file under " + root.AnimDataPath + ".");
            }

            AnimationClipSet clips = WbaReader.LoadFile(wbaPath);

            SkaterAnimationClip clip = new SkaterAnimationClip();
            clip.AssemblyName = assembly.Name;
            clip.Description = assembly.Description;
            clip.Segment = segment;
            clip.Clips = clips;
            clip.WbaName = segment.FileName;
            clip.WbaPath = wbaPath;

            clip.Validate();

            return clip;
        }

        // Loads (and caches) the shipped assembly script. The script is 21 KB of text and is the
        // same file for every animation, so a preview loads it once.
        public static AnimationAssemblyFile LoadAssemblies(OriginalAssetRoot root)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            if (cachedAssemblies != null && string.Equals(cachedAssembliesRoot, root.DataPath, StringComparison.OrdinalIgnoreCase))
            {
                return cachedAssemblies;
            }

            string path = root.ResolveDataFile(AssemblyRelativePath);

            if (path == null)
            {
                throw new OriginalAssetException(
                    "AnimAssm.txt was not found under " + root.AnimDataPath
                    + "; the original animation assembly script is required to map an action name to a .wba clip.");
            }

            cachedAssemblies = AnimationAssemblyReader.LoadFile(path);
            cachedAssembliesRoot = root.DataPath;

            return cachedAssemblies;
        }

        // Everything AnimClip::Load (HYPERX 0x00416510) and AnimPlayer::SetClip need to be true for
        // the shipped pose pipeline to play this segment; a violation is fatal, never replaced with a
        // substitute clip.
        private void Validate()
        {
            AnimationHeader header = Clips.AnimationHeader;

            if (header.ClipCount != Clips.Clips.Length)
            {
                throw new OriginalAssetException(
                    "Animation set \"" + WbaName + "\" declares " + header.ClipCount + " clips but holds "
                    + Clips.Clips.Length + ".");
            }

            if (Segment.StartFrame < 0 || Segment.EndFrame < Segment.StartFrame)
            {
                throw new OriginalAssetException(
                    "Assembly \"" + AssemblyName + "\" segment 0 range " + Segment.StartFrame + ".."
                    + Segment.EndFrame + " is not a forward range.");
            }

            if (Segment.EndFrame >= header.FrameCount)
            {
                throw new OriginalAssetException(
                    "Assembly \"" + AssemblyName + "\" segment 0 ends at frame " + Segment.EndFrame
                    + ", past \"" + WbaName + "\"'s " + header.FrameCount + " frames; AnimClip::GetBoneTransform "
                    + "would read past the clip arrays.");
            }

            if (!(Segment.StandardFps > 0f) || float.IsNaN(Segment.StandardFps) || float.IsInfinity(Segment.StandardFps))
            {
                throw new OriginalAssetException(
                    "Assembly \"" + AssemblyName + "\" segment 0 standard rate " + Segment.StandardFps
                    + " is not a usable frame rate.");
            }

            for (int i = 0; i < Clips.Clips.Length; i++)
            {
                AnimationClip clip = Clips.Clips[i];

                if (clip == null)
                {
                    throw new OriginalAssetException("Animation set \"" + WbaName + "\" has a null clip at index " + i + ".");
                }

                if (!clip.HasUsableTransforms)
                {
                    throw new OriginalAssetException(
                        "Animation set \"" + WbaName + "\" clip " + i + " uses format " + clip.Format
                        + ", which the shipped loader does not transform (docs/ASSET-FORMATS.md section 7.3).");
                }

                if (clip.AxisOrder == null || clip.AxisOrder.Length != 3)
                {
                    throw new OriginalAssetException(
                        "Animation set \"" + WbaName + "\" clip " + i + " axis order is not three letters.");
                }

                if ((clip.Format == AnimationClip.FormatSingleTranslationEuler
                     || clip.Format == AnimationClip.FormatSingleTranslationEuler4
                     || clip.Format == AnimationClip.FormatTranslationsEuler)
                    && (clip.EulerDegrees == null || clip.EulerDegrees.Length != header.FrameCount * 3))
                {
                    throw new OriginalAssetException(
                        "Animation set \"" + WbaName + "\" clip " + i + " carries "
                        + (clip.EulerDegrees == null ? 0 : clip.EulerDegrees.Length) + " Euler floats for "
                        + header.FrameCount + " frames; AnimClip::Load reads frame count * 3.");
                }

                if (clip.Format == AnimationClip.FormatTranslations
                    && (clip.Translations == null || clip.Translations.Length != header.FrameCount))
                {
                    throw new OriginalAssetException(
                        "Animation set \"" + WbaName + "\" clip " + i + " carries no per frame translations.");
                }
            }

            if (Clips.TrailingBytes < 0)
            {
                throw new OriginalAssetException("Animation set \"" + WbaName + "\" has a negative trailing byte count.");
            }
        }

        public string Describe()
        {
            return "UBLADE3_SKATER_PREVIEW_CLIP assembly=" + AssemblyName
                   + " desc=\"" + (Description ?? string.Empty) + "\""
                   + " wba=" + WbaName
                   + " path=" + WbaPath
                   + " segment=0 range=" + StartFrame + ".." + EndFrame
                   + " displayedFrames=" + DisplayedFrameCount
                   + " standardFps=" + StandardFps.ToString("0.###", CultureInfo.InvariantCulture)
                   + " loop=" + Segment.LoopCount
                   + " duration=" + DurationSeconds.ToString("0.####", CultureInfo.InvariantCulture)
                   + " clips=" + Clips.Clips.Length
                   + " frames=" + Clips.AnimationHeader.FrameCount;
        }
    }
}
