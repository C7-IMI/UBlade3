using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UBlade3.Assets;

namespace UBlade3.Native
{
    // One bone (component slot) of an original WBM rig, as the .wbm stores it.
    // src/graphics/meshbone.h class MeshBoneDesc; docs/ASSET-FORMATS.md section 5.4.
    public sealed class SkaterBone
    {
        // The slot index in the model component (the bone's own id; every shipped rig has
        // linkId == slot, validated by SkaterRig).
        public int Index;

        // MeshBoneDesc.m_nLinkId (0x00). The slot's own id, not its parent's.
        public short LinkId;

        // MeshBoneDesc.m_anChildIds (0x20): direct slot indices, not offsets.
        public short[] ChildIds;

        // MeshBoneDesc.m_vRest (0x04): the bone's bind translation in model units.
        public AssetVector3 Rest;

        // MeshBoneDesc.m_vScale (0x10): the conversion MeshBone::PropagateTransforms (HYPERX
        // 0x0046ef10) applies to the posed translation of a bone whose clip returns a transform.
        public AssetVector3 Scale;

        // MeshBoneDesc.m_nUnused (0x1c); nothing in either reference reads it.
        public int Unused;

        // Parent slot id, or -1 for the rig root.
        public int ParentIndex = -1;

        public override string ToString()
        {
            return "bone " + Index + " link " + LinkId + " parent " + ParentIndex
                   + " rest " + Rest + " scale " + Scale;
        }
    }

    // The skeleton and pose pipeline of one original WBM model, reconstructed from the same
    // functions the shipped engine runs:
    //
    //   EngineObject::ApplyHeaderRotation      (HYPERX 0x00472620)  model angle Z, X, Y
    //   MeshBone::BindToLODMesh               (HYPERX 0x0046fba0)  local translation = rest
    //   AnimClip::GetBoneTransform            (HYPERX 0x00416800)  per bone, per frame
    //   AnimClip::GetBoneTransformFull        (HYPERX 0x00416930)  first frame after a clip change
    //   Anim::EulerToMatrix                   (HYPERX 0x00416ba0)  Euler degrees -> packed 16 bit
    //   Anim::UnpackFixed16ToFloat            (HYPERX 0x00416cf0)  packed 16 bit -> float 3x3
    //   MeshBone::PropagateTransforms         (HYPERX 0x0046ef10)  world = parent * local
    //   EngineObject::UpdateRenderSlotTransform (HYPERX 0x00471630) object transform * rig
    //
    // The one non-obvious rule this reproduces: GetBoneTransform answers 1 only for the formats
    // that write a translation (1, 3, 6, 8); for formats 2 and 4 it writes the 3x3 rotation and
    // answers 0. AnimPlayer::UpdateFrame only raises MESHBONE_ANIMATION_DIRTY on a 1 answer, and
    // MeshBone::PropagateTransforms only multiplies the local translation by MeshBoneDesc.Scale
    // while that flag is set. So on the shipped skater rigs the root (format 1, translation only)
    // has its clip translation converted by Scale, while every format 2 bone keeps its rest
    // translation unconverted. This class implements exactly that split.
    public sealed class SkaterRig
    {
        // Every shipped skater rig has sixteen slots (docs/ASSET-FORMATS.md section 5.9).
        public const int ExpectedBoneCount = 16;

        // ModelComponent::AngleDegrees is converted by ModelDatabase::LoadLOD (HYPERX 0x004728e0)
        // with g_flDegreesToRadians = 0.017453292f before EngineObject::ApplyHeaderRotation sees it.
        public const float DegreesToRadians = ModelComponent.DegreesToRadians;

        private SkaterRig()
        {
        }

        public string ModelName { get; private set; }

        public OriginalModelSet ModelSet { get; private set; }

        public string ModelPath { get; private set; }

        public WbmModel Model { get; private set; }

        public SkaterBone[] Bones { get; private set; }

        public int BoneCount
        {
            get { return Bones.Length; }
        }

        // EngineObject::ApplyHeaderRotation: m_transformA, composed Z then X then Y from the model
        // component's angle (already in radians in the reader). A zero component is skipped, which
        // is the same product.
        public HbMat4 HeaderRotation { get; private set; }

        public int LodCount
        {
            get { return Model.Lods.Length; }
        }

        // Bones that own a mesh block in LOD zero (the skater root slot has no geometry).
        public int MeshSlotCount { get; private set; }

        public static SkaterRig Load(OriginalAssetRoot root, string modelName, OriginalModelSet modelSet)
        {
            if (root == null)
            {
                throw new ArgumentNullException("root");
            }

            if (string.IsNullOrEmpty(modelName))
            {
                throw new ArgumentException("model name is empty", "modelName");
            }

            OriginalModelSet foundSet;
            string path = root.FindModelFile(modelName, modelSet, out foundSet);

            if (path == null)
            {
                throw new OriginalAssetException(
                    "Model \"" + modelName + "\" is in neither original model set (" + root.ShadedModelsPath
                    + " and " + root.TexturedModelsPath + ").");
            }

            return FromModel(WbmReader.LoadFile(path), modelName, foundSet, path);
        }

        public static SkaterRig FromModel(WbmModel model, string modelName, OriginalModelSet modelSet, string modelPath)
        {
            if (model == null)
            {
                throw new ArgumentNullException("model");
            }

            if (model.Component == null || model.Component.SlotDescriptors == null)
            {
                throw new OriginalAssetException("Model \"" + modelName + "\" has no bone descriptors.");
            }

            if (model.Lods == null || model.Lods.Length == 0)
            {
                throw new OriginalAssetException("Model \"" + modelName + "\" has no LOD group.");
            }

            SkaterRig rig = new SkaterRig();
            rig.Model = model;
            rig.ModelName = modelName;
            rig.ModelSet = modelSet;
            rig.ModelPath = modelPath;

            MeshBoneDesc[] descriptors = model.Component.SlotDescriptors;
            int count = model.Component.SubComponentCount;

            if (count != descriptors.Length)
            {
                throw new OriginalAssetException(
                    "Model \"" + modelName + "\" declares " + count + " subcomponents but has "
                    + descriptors.Length + " bone descriptors.");
            }

            if (count <= 0)
            {
                throw new OriginalAssetException("Model \"" + modelName + "\" has no bones.");
            }

            SkaterBone[] bones = new SkaterBone[count];

            for (int i = 0; i < count; i++)
            {
                MeshBoneDesc descriptor = descriptors[i];
                SkaterBone bone = new SkaterBone();
                bone.Index = i;
                bone.LinkId = descriptor.LinkId;
                bone.Rest = descriptor.Rest;
                bone.Scale = descriptor.Scale;
                bone.Unused = descriptor.Unused;
                bone.ChildIds = descriptor.ChildIds;

                if (descriptor.LinkId != i)
                {
                    throw new OriginalAssetException(
                        "Model \"" + modelName + "\" slot " + i + " has link id " + descriptor.LinkId
                        + "; every shipped rig has link id == slot (MeshBone:"
                        + ":PropagateTransforms keys the cycle guard off it).");
                }

                bones[i] = bone;
            }

            int rootCount = 0;

            for (int i = 0; i < count; i++)
            {
                for (int c = 0; c < bones[i].ChildIds.Length; c++)
                {
                    int child = bones[i].ChildIds[c];

                    if (child < 0 || child >= count)
                    {
                        throw new OriginalAssetException(
                            "Model \"" + modelName + "\" slot " + i + " names child " + child
                            + " outside its " + count + " slots.");
                    }

                    if (child == i)
                    {
                        continue;
                    }

                    if (bones[child].ParentIndex >= 0 && bones[child].ParentIndex != i)
                    {
                        throw new OriginalAssetException(
                            "Model \"" + modelName + "\" slot " + child + " is a child of both slot "
                            + bones[child].ParentIndex + " and slot " + i + ".");
                    }

                    bones[child].ParentIndex = i;
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (bones[i].ParentIndex < 0)
                {
                    rootCount++;
                }
            }

            if (rootCount != 1)
            {
                throw new OriginalAssetException(
                    "Model \"" + modelName + "\" has " + rootCount + " bone roots; the shipped rigs have one.");
            }

            if (bones[0].ParentIndex >= 0)
            {
                throw new OriginalAssetException(
                    "Model \"" + modelName + "\" has its rig root at slot " + FindRoot(bones)
                    + "; the engine poses from slot zero (EngineObject::UpdateRenderSlotTransform).");
            }

            rig.Bones = bones;
            rig.HeaderRotation = BuildHeaderRotation(model.Component.AngleRadians);

            ModelLod lod0 = model.Lods[0];
            int meshSlots = 0;

            for (int i = 0; i < lod0.BySlot.Length; i++)
            {
                if (lod0.BySlot[i] != null)
                {
                    meshSlots++;
                }
            }

            rig.MeshSlotCount = meshSlots;

            ValidateHierarchy(rig);

            return rig;
        }

        private static int FindRoot(SkaterBone[] bones)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i].ParentIndex < 0)
                {
                    return i;
                }
            }

            return -1;
        }

        // Parent-before-child walk; the same order the renderer's recursive PropagateTransforms
        // visits (a depth-first walk of the child ids), reduced to the topological order the
        // matrices need. Throws when the hierarchy has a cycle, like the original's stack would.
        private static int[] TopologicalOrder(SkaterBone[] bones)
        {
            int count = bones.Length;
            int[] order = new int[count];
            bool[] solved = new bool[count];
            int reached = 0;

            for (int pass = 0; pass < count; pass++)
            {
                bool progressed = false;

                for (int i = 0; i < count; i++)
                {
                    if (solved[i])
                    {
                        continue;
                    }

                    int parent = bones[i].ParentIndex;

                    if (parent >= 0 && !solved[parent])
                    {
                        continue;
                    }

                    order[reached] = i;
                    reached++;
                    solved[i] = true;
                    progressed = true;
                }

                if (reached == count)
                {
                    return order;
                }

                if (!progressed)
                {
                    throw new OriginalAssetException("The bone hierarchy has a cycle.");
                }
            }

            return order;
        }

        private static void ValidateHierarchy(SkaterRig rig)
        {
            int[] order = TopologicalOrder(rig.Bones);

            if (order.Length != rig.BoneCount)
            {
                throw new OriginalAssetException("The bone hierarchy does not cover every slot.");
            }
        }

        // EngineObject::ApplyHeaderRotation (HYPERX 0x00472620): the model component's angle,
        // applied Z then X then Y. The original writes it into the identity transform with the
        // staged rotation helpers; a zero component is skipped and the products agree.
        public static HbMat4 BuildHeaderRotation(AssetVector3 radians)
        {
            HbMat4 rotation = HbMat4.Identity();

            if (radians.Z != 0f)
            {
                rotation = rotation.Multiply(HbMat4.RotationZ(radians.Z));
            }

            if (radians.X != 0f)
            {
                rotation = rotation.Multiply(HbMat4.RotationX(radians.X));
            }

            if (radians.Y != 0f)
            {
                rotation = rotation.Multiply(HbMat4.RotationY(radians.Y));
            }

            return rotation;
        }

        // EngineObject::UpdateRenderSlotTransform (HYPERX 0x00471630) builds
        // m_transformC = m_transformB * m_transformA and then adds PhysicsBody::pos. m_transformA
        // is the header rotation above; m_transformB is the object's facing, and the preview's
        // yaw around the original's up axis (Z) stands in for it. A still preview can also add a
        // placement offset, exactly as the matrix's translation column.
        public HbMat4 ObjectTransform(float yawRadians, float offsetX, float offsetY, float offsetZ)
        {
            HbMat4 rotation = HeaderRotation;

            if (yawRadians != 0f)
            {
                rotation = HbMat4.RotationZ(yawRadians).Multiply(rotation);
            }

            HbMat4 transform = HbMat4.Translation(offsetX, offsetY, offsetZ);
            transform.M[0] = rotation.M[0];
            transform.M[1] = rotation.M[1];
            transform.M[2] = rotation.M[2];
            transform.M[4] = rotation.M[4];
            transform.M[5] = rotation.M[5];
            transform.M[6] = rotation.M[6];
            transform.M[8] = rotation.M[8];
            transform.M[9] = rotation.M[9];
            transform.M[10] = rotation.M[10];

            return transform;
        }

        public HbMat4 ObjectTransform(float yawRadians)
        {
            return ObjectTransform(yawRadians, 0f, 0f, 0f);
        }

        // AnimPlayer::UpdateFrame (HYPERX 0x00424880) walks the clip set by bone index and writes
        // each bone's local matrix; then EngineObject::UpdateRenderSlotTransform propagates. The
        // first frame after a clip change (m_state.bFullDecode) uses GetBoneTransformFull, whose
        // only difference for the shipped formats is that format 1 also writes an identity 3x3;
        // the bind pose already has that identity, so PoseFrame poses both cases identically.
        public HbMat4[] PoseFrame(AnimationClipSet clips, int frame)
        {
            return PoseFrame(clips, frame, HeaderRotation);
        }

        public HbMat4[] PoseFrame(AnimationClipSet clips, int frame, HbMat4 objectTransform)
        {
            if (clips == null)
            {
                throw new ArgumentNullException("clips");
            }

            if (objectTransform == null)
            {
                throw new ArgumentNullException("objectTransform");
            }

            if (clips.Clips == null || clips.Clips.Length != BoneCount)
            {
                throw new OriginalAssetException(
                    "Animation set \"" + clips.SourceName + "\" has "
                    + (clips.Clips == null ? 0 : clips.Clips.Length) + " clips for " + ModelName
                    + "'s " + BoneCount + " bones; the shipped player indexes one clip per bone.");
            }

            int frameCount = clips.AnimationHeader.FrameCount;

            if (frame < 0 || frame >= frameCount)
            {
                throw new OriginalAssetException(
                    "Frame " + frame + " is outside \"" + clips.SourceName + "\"'s " + frameCount + " frames.");
            }

            int[] order = TopologicalOrder(Bones);
            HbMat4[] world = new HbMat4[BoneCount];

            for (int k = 0; k < order.Length; k++)
            {
                int i = order[k];
                AnimationClip clip = clips.Clips[i];

                if (clip == null)
                {
                    throw new OriginalAssetException(
                        "Animation set \"" + clips.SourceName + "\" has no clip for bone " + i + ".");
                }

                HbMat4 local = BuildBoneLocal(clip, Bones[i], frame);
                int parent = Bones[i].ParentIndex;

                world[i] = parent < 0 ? objectTransform.Multiply(local) : world[parent].Multiply(local);
            }

            return world;
        }

        // The unanimated rig: MeshBone::BindToLODMesh (HYPERX 0x0046fba0) sets every local matrix
        // to the descriptor's rest translation with no rotation, and the rebind path
        // (MeshBone::BindToLODMeshShadowless, HYPERX 0x0046fc50) writes the same translation. This
        // is the pose the engine holds before any animation clip is applied; the preview uses it
        // only for its first framing and the probe records it as the skeleton reference.
        public HbMat4[] BindPose()
        {
            return BindPose(HeaderRotation);
        }

        public HbMat4[] BindPose(HbMat4 objectTransform)
        {
            if (objectTransform == null)
            {
                throw new ArgumentNullException("objectTransform");
            }

            int[] order = TopologicalOrder(Bones);
            HbMat4[] world = new HbMat4[BoneCount];

            for (int k = 0; k < order.Length; k++)
            {
                int i = order[k];
                HbMat4 local = HbMat4.Translation(Bones[i].Rest.X, Bones[i].Rest.Y, Bones[i].Rest.Z);
                int parent = Bones[i].ParentIndex;

                world[i] = parent < 0 ? objectTransform.Multiply(local) : world[parent].Multiply(local);
            }

            return world;
        }

        // MeshBone::BindToLODMesh (0x0046fba0) starts every local matrix at the descriptor's rest
        // translation. AnimClip::GetBoneTransform then writes, per its format:
        //   1  translation only, one per frame      -> local = T(clip * scale); rotation identity
        //   2,4 one translation then Euler rotation -> local = T(rest) * R(euler); translation not
        //                                              scaled (the function answers 0, see class
        //                                              comment)
        //   3  translation and Euler per frame       -> local = T(clip * scale) * R(euler)
        // Formats 5-8 are not shipped; the shipped loader either leaves the rotation uninitialised
        // or raises, so this refuses to invent a pose for them.
        private static HbMat4 BuildBoneLocal(AnimationClip clip, SkaterBone bone, int frame)
        {
            HbMat4 local = HbMat4.Identity();

            switch (clip.Format)
            {
                case AnimationClip.FormatTranslations:
                {
                    AssetVector3 translation = clip.Translations[frame];
                    SetScaledTranslation(local, translation, bone.Scale);
                    return local;
                }

                case AnimationClip.FormatSingleTranslationEuler:
                case AnimationClip.FormatSingleTranslationEuler4:
                {
                    EulerToMatrix(clip, frame, local);
                    local.M[3] = bone.Rest.X;
                    local.M[7] = bone.Rest.Y;
                    local.M[11] = bone.Rest.Z;
                    return local;
                }

                case AnimationClip.FormatTranslationsEuler:
                {
                    EulerToMatrix(clip, frame, local);
                    SetScaledTranslation(local, clip.Translations[frame], bone.Scale);
                    return local;
                }

                default:
                    throw new OriginalAssetException(
                        "Bone " + bone.Index + " uses animation format " + clip.Format
                        + "; the shipped loader gives no usable transform for it (docs/ASSET-FORMATS.md section 7.3).");
            }
        }

        private static void SetScaledTranslation(HbMat4 local, AssetVector3 translation, AssetVector3 scale)
        {
            // MeshBone::PropagateTransforms: m_local.SetTranslation(m_a[n][3] * m_vScale.n, ...)
            local.M[3] = translation.X * scale.X;
            local.M[7] = translation.Y * scale.Y;
            local.M[11] = translation.Z * scale.Z;
        }

        // Anim::EulerToMatrix (HYPERX 0x00416ba0) then the stored 16 bit rotation stream it builds,
        // read back through Anim::UnpackFixed16ToFloat (HYPERX 0x00416cf0). The original computes
        // the matrix at load time, packs each cell into a PackedRotation word, and unpacks it again
        // in GetBoneTransform, so the pose the renderer sees is the quantised matrix - not the raw
        // float one. Reproducing the pack/unpack here keeps the preview's joint rotations equal to
        // the shipped runtime's to within the 12 bit mantissa it stores.
        public static void EulerToMatrix(AnimationClip clip, int frame, HbMat4 output)
        {
            if (clip == null)
            {
                throw new ArgumentNullException("clip");
            }

            if (output == null)
            {
                throw new ArgumentNullException("output");
            }

            if (clip.EulerDegrees == null || clip.EulerDegrees.Length < (frame + 1) * 3)
            {
                throw new OriginalAssetException(
                    "Clip " + clip.Index + " has no Euler keyframes for frame " + frame + ".");
            }

            if (clip.AxisOrder == null || clip.AxisOrder.Length != 3)
            {
                throw new OriginalAssetException("Clip " + clip.Index + " has an invalid axis order.");
            }

            float[] m = output.M;

            for (int i = 0; i < 16; i++)
            {
                m[i] = 0f;
            }

            m[0] = 1f;
            m[5] = 1f;
            m[10] = 1f;
            m[15] = 1f;

            bool identity = true;

            // The original loops nAxis 0..2 and reads angle 2-nAxis with axis letter 2-nAxis, so
            // the last letter in the string is applied first.
            for (int nAxis = 0; nAxis < 3; nAxis++)
            {
                int source = 2 - nAxis;
                float degrees = clip.EulerDegrees[(frame * 3) + source];
                float radians = degrees * DegreesToRadians;
                float cos = (float)Math.Cos(radians);
                float sin = (float)Math.Sin(radians);
                char axis = clip.AxisOrder[source];

                switch (axis)
                {
                    case 'x':
                        RotateXStaged(m, cos, sin, ref identity);
                        break;

                    case 'y':
                        RotateAroundYStaged(m, cos, sin, ref identity);
                        break;

                    case 'z':
                        RotateZStaged(m, cos, sin, ref identity);
                        break;

                    default:
                        throw new OriginalAssetException(
                            "Clip " + clip.Index + " names Euler axis '" + axis + "'; the original only handles x, y and z.");
                }
            }

            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    int at = (row * 4) + column;
                    m[at] = UnpackFixed16(PackFixed16(m[at]));
                }
            }
        }

        // The identity arms of TransformObject::RotateXStaged / RotateAroundY / ApplyRotationZ
        // write the first rotation straight; the general arms multiply the staged rotation in on
        // the right, with the exact float/double expressions the decompilation records.
        private static void RotateXStaged(float[] m, float cos, float sin, ref bool identity)
        {
            if (identity)
            {
                m[5] = cos;
                m[10] = cos;
                m[6] = -sin;
                m[9] = sin;
            }
            else
            {
                float old0 = m[1];
                m[1] = (old0 * cos) + (m[2] * sin);
                m[2] = (m[2] * cos) - (old0 * sin);

                float old1 = m[5];
                m[5] = (old1 * cos) + (m[6] * sin);
                m[6] = (m[6] * cos) - (old1 * sin);

                float old2 = m[9];
                m[9] = (old2 * cos) + (m[10] * sin);
                m[10] = (m[10] * cos) - (old2 * sin);
            }

            identity = false;
        }

        private static void RotateAroundYStaged(float[] m, float cos, float sin, ref bool identity)
        {
            if (identity)
            {
                m[10] = cos;
                m[0] = cos;
                m[8] = -sin;
                m[2] = sin;
            }
            else
            {
                double old = m[0];
                m[0] = (float)((cos * old) - (sin * (double)m[2]));
                m[2] = (float)(((cos * (double)m[2]) + (sin * old)));

                old = m[4];
                m[4] = (float)((cos * old) - (sin * (double)m[6]));
                m[6] = (float)(((cos * (double)m[6]) + (sin * old)));

                old = m[8];
                m[8] = (float)((cos * old) - (sin * (double)m[10]));
                m[10] = (float)(((cos * (double)m[10]) + (sin * old)));
            }

            identity = false;
        }

        private static void RotateZStaged(float[] m, float cos, float sin, ref bool identity)
        {
            if (identity)
            {
                m[5] = cos;
                m[0] = cos;
                m[1] = -sin;
                m[4] = sin;
            }
            else
            {
                float first = m[0];
                m[0] = (first * cos) + (m[1] * sin);
                m[1] = (m[1] * cos) - (first * sin);

                first = m[4];
                m[4] = (first * cos) + (m[5] * sin);
                m[5] = (m[5] * cos) - (first * sin);

                first = m[8];
                m[8] = (first * cos) + (sin * m[9]);
                m[9] = (cos * m[9]) - (first * sin);
            }

            identity = false;
        }

        // Anim::EulerToMatrix's pack of one 3x3 cell into a 16 bit word.
        public static ushort PackFixed16(float value)
        {
            if (Math.Abs((double)value) < 5e-05)
            {
                value = 0f;
            }

            uint n = (uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

            if ((n & 0xfffu) > 0x800u)
            {
                n += 0x1000u;
            }

            if ((n & 0x80000000u) != 0u)
            {
                n |= 0xf8000000u;
            }
            else
            {
                n &= 0x07fff000u;
            }

            n <<= 4;

            return (ushort)(n >> 16);
        }

        // Anim::UnpackFixed16ToFloat: the hand written assembly's top-15-bits exponent/mantissa
        // reconstruction, including its 0x38000000 base and 0xb8000000 negative form.
        public static float UnpackFixed16(ushort word)
        {
            uint value = (uint)word << 17;
            bool negative = (word & 0x8000u) != 0u;
            value >>= 5;
            value |= negative ? 0xb8000000u : 0x38000000u;

            return BitConverter.ToSingle(BitConverter.GetBytes((int)value), 0);
        }

        // TransformObject::TransformPoint (HYPERX 0x00403110): row major, translation in column 3.
        public static void TransformPoint(HbMat4 matrix, AssetVector3 point, out float x, out float y, out float z)
        {
            float[] m = matrix.M;
            x = (m[0] * point.X) + (m[1] * point.Y) + (m[2] * point.Z) + m[3];
            y = (m[4] * point.X) + (m[5] * point.Y) + (m[6] * point.Z) + m[7];
            z = (m[8] * point.X) + (m[9] * point.Y) + (m[10] * point.Z) + m[11];
        }

        // The rotation part of TransformObject::TransformDirection (HYPERX 0x00406de0).
        public static void TransformDirection(HbMat4 matrix, AssetVector3 direction, out float x, out float y, out float z)
        {
            float[] m = matrix.M;
            x = (m[0] * direction.X) + (m[1] * direction.Y) + (m[2] * direction.Z);
            y = (m[4] * direction.X) + (m[5] * direction.Y) + (m[6] * direction.Z);
            z = (m[8] * direction.X) + (m[9] * direction.Y) + (m[10] * direction.Z);
        }

        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("UBLADE3_SKATER_PREVIEW_RIG model=").Append(ModelName);
            text.Append(" set=").Append(ModelSet == OriginalModelSet.Textured ? "Mdltxt" : "Mdlshd");
            text.Append(" path=").Append(ModelPath);
            text.Append(" bones=").Append(BoneCount);
            text.Append(" meshSlots=").Append(MeshSlotCount);
            text.Append(" lods=").Append(LodCount);
            text.Append(" angle=").Append(Angle(Model.Component.AngleDegrees));
            return text.ToString();
        }

        private static string Angle(AssetVector3 value)
        {
            return value.X.ToString("0.###", CultureInfo.InvariantCulture) + ","
                   + value.Y.ToString("0.###", CultureInfo.InvariantCulture) + ","
                   + value.Z.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    // FNV-1a 64 over a canonical stream of parsed primitives (floats through their bit patterns,
    // exactly like UBlade3.Assets.AssetProbe), so a preview run is reproducible and a data or
    // pipeline change moves the digest.
    public static class SkaterHash
    {
        public const ulong Offset = 0xcbf29ce484222325UL;

        public const ulong Prime = 0x100000001b3UL;

        public static void Add(ref ulong hash, string text)
        {
            if (text == null)
            {
                text = string.Empty;
            }

            for (int i = 0; i < text.Length; i++)
            {
                hash ^= (byte)(text[i] & 0xff);
                hash *= Prime;
            }

            hash ^= 0xff;
            hash *= Prime;
        }

        public static void Add(ref ulong hash, int value)
        {
            Add(ref hash, (uint)value);
        }

        public static void Add(ref ulong hash, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                hash ^= (byte)((value >> (i * 8)) & 0xff);
                hash *= Prime;
            }
        }

        public static void Add(ref ulong hash, float value)
        {
            Add(ref hash, (uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
        }

        public static void Add(ref ulong hash, AssetVector3 value)
        {
            Add(ref hash, value.X);
            Add(ref hash, value.Y);
            Add(ref hash, value.Z);
        }

        public static string Format(ulong hash)
        {
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
