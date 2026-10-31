using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Assets;
using UnityEngine;

namespace UBlade3.Native
{
    // N15: the shipped animation registry as the plan VM sees it.
    //
    // AnimScriptTable::ParseScript (HYPERX 0x00418210) reads AnimAssm.txt once and appends one
    // AnimDB per assembly line, in file order. The plan's animation ids are indexes into that table
    // (PlanAction::SetAnimation, BehParam::SetAnimation/SetPersistentAnimation). AnimRegistry::
    // FindAnimByNameIdx walks the same table. The C# AnimationAssemblyFile keeps the assemblies in
    // file order, so index i here is the plan's id i.
    //
    // Each AnimDB's first node is segment 0; AnimPlayer::SelectAnimationSet calls
    // SetClip(TransitionClip(0), ...), and SetClip starts at that node's StartFrame. This class
    // resolves id -> assembly -> segment 0 -> WBA through the existing SkaterAnimationClip loader,
    // and refuses a clip whose bone count does not match the character's rig. A refusal is explicit
    // unsupported telemetry; no substitute clip is ever played.
    public sealed class MatchAnimationRegistry
    {
        public const string Marker = "UBLADE3_NATIVE_ANIMATION_REGISTRY";

        public const string RelativeAssemblyPath = "animdata\\AnimAssm.txt";

        public readonly OriginalAssetRoot Root;

        public readonly AnimationAssemblyFile Assemblies;

        public readonly string[] Names;

        public readonly List<string> Warnings = new List<string>();

        private readonly Dictionary<int, SkaterAnimationClip> loadedClips =
            new Dictionary<int, SkaterAnimationClip>();

        private readonly Dictionary<int, string> loadErrors = new Dictionary<int, string>();

        private MatchAnimationRegistry(OriginalAssetRoot root, AnimationAssemblyFile assemblies)
        {
            Root = root;
            Assemblies = assemblies;
            Names = new string[assemblies.Assemblies.Length];

            for (int i = 0; i < Names.Length; i++)
            {
                Names[i] = assemblies.Assemblies[i].Name;
            }
        }

        public int Count
        {
            get { return Names.Length; }
        }

        public static MatchAnimationRegistry Load(OriginalAssetRoot root)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            AnimationAssemblyFile file = SkaterAnimationClip.LoadAssemblies(root);
            MatchAnimationRegistry registry = new MatchAnimationRegistry(root, file);
            Debug.Log(Marker + " assemblies=" + registry.Count + " source=\"" + file.SourceName + "\"");
            return registry;
        }

        public bool TryResolve(int id, out string name, out AnimationAssembly assembly)
        {
            name = null;
            assembly = null;

            if (id < 0 || id >= Names.Length)
            {
                return false;
            }

            name = Names[id];
            assembly = Assemblies.Assemblies[id];
            return assembly != null && !string.IsNullOrEmpty(name);
        }

        public bool TryFindIndex(string name, out int id)
        {
            id = -1;

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (int i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    id = i;
                    return true;
                }
            }

            return false;
        }

        // Resolves a plan animation id for one character. The clip is loaded through the same
        // WBA/AnimAssm path as the preview and match views; a bone mismatch or load error is
        // returned as an explicit unsupported reason.
        public SkaterAnimationClip TryLoad(int id, MatchSkaterView view, out string animationName,
            out string error)
        {
            animationName = null;
            error = string.Empty;

            if (view == null || view.Character == null || view.Character.Rig == null)
            {
                error = "no-view";
                return null;
            }

            AnimationAssembly assembly;
            if (!TryResolve(id, out animationName, out assembly))
            {
                error = "id-out-of-range";

                if (!loadErrors.ContainsKey(id))
                {
                    loadErrors[id] = error;
                }

                return null;
            }

            SkaterAnimationClip cached;
            if (loadedClips.TryGetValue(id, out cached))
            {
                if (cached.Clips.Clips.Length != view.Character.Rig.BoneCount)
                {
                    error = "bones=" + cached.Clips.Clips.Length + "/" + view.Character.Rig.BoneCount;
                    return null;
                }

                return cached;
            }

            string cachedError;
            if (loadErrors.TryGetValue(id, out cachedError))
            {
                error = cachedError;
                return null;
            }

            try
            {
                SkaterAnimationClip clip = SkaterAnimationClip.Load(Root, Assemblies, animationName);

                if (clip.Clips.Clips.Length != view.Character.Rig.BoneCount)
                {
                    error = "bones=" + clip.Clips.Clips.Length + "/" + view.Character.Rig.BoneCount;
                    loadErrors[id] = error;
                    return null;
                }

                loadedClips[id] = clip;
                return clip;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                loadErrors[id] = error;
                return null;
            }
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append(Marker).Append(" assemblies=").Append(Count)
                .Append(" loadedClips=").Append(loadedClips.Count)
                .Append(" errors=").Append(loadErrors.Count);
            return text.ToString();
        }
    }
}
