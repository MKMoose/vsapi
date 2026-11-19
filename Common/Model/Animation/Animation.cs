using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.API.Common
{
    public enum EnumEntityActivityStoppedHandling
    {
        PlayTillEnd = 0,
        Rewind = 1,
        Stop = 2,
        EaseOut = 3
    }

    public enum EnumEntityAnimationEndHandling
    {
        Repeat = 0,
        Hold = 1,
        Stop = 2,
        EaseOut = 3
    }




    /// <summary>
    /// Represents a shape animation and can calculate the transformation matrices for each frame to be sent to the shader
    /// Process
    /// 1. For each frame, for each root element, calculate the transformation matrix. Curent model matrix is identy mat.
    /// 1.1. Get previous and next key frame. Apply translation, rotation and scale to model matrix.
    /// 1.2. Store this matrix as animationmatrix in list
    /// 1.3. For each child element
    /// 1.3.1. Multiply local transformation matrix with the animation matrix. This matrix is now the curent model matrix. Go to 1 with child elements as root elems
    /// 
    /// 2. For each frame, for each joint
    /// 2.1. Calculate the inverse model matrix 
    /// 2.2. Multiply stored animationmatrix with the inverse model matrix
    /// 
    /// 3. done
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Animation
    {
        [JsonProperty]
        public int QuantityFrames;
    
        [JsonProperty]
        public string Name;

        [JsonProperty]
        public string Code;

        [JsonProperty]
        public int Version;

        [JsonProperty]
        public bool EaseAnimationSpeed = false;

        [JsonProperty]
        public AnimationKeyFrame[] KeyFrames;

        [JsonProperty]
        public EnumEntityActivityStoppedHandling OnActivityStopped = EnumEntityActivityStoppedHandling.Rewind;

        [JsonProperty]
        public EnumEntityAnimationEndHandling OnAnimationEnd = EnumEntityAnimationEndHandling.Repeat;



        public uint CodeCrc32;

        public AnimationFrame[][] PrevNextKeyFrameByFrame;


        protected HashSet<int> jointsDone = new HashSet<int>();
        public static readonly float[] reusableIdentityMatrix = Mat4f.Create();   // This will be cloned within GenerateFrame() before being modified


        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            Name = Name.DeDuplicate();
            Code = (Code ?? Name).DeDuplicate();
            CodeCrc32 = AnimationMetaData.GetCrc32(Code);

            if (KeyFrames == null) return;

            foreach (var keyFrame in KeyFrames)
            {
                var elements = keyFrame?.Elements;
                if (elements == null || elements.Count == 0) continue;

                var deduplicated = new Dictionary<string, AnimationKeyFrameElement>(elements.Count, elements.Comparer);
                foreach (var entry in elements)
                {
                    deduplicated.Add(entry.Key.DeDuplicate(), entry.Value);
                }
                keyFrame.Elements = deduplicated;
            }
        }


        /// <summary>
        /// Compiles the animation into a bunch of matrices, 31 matrices per frame.
        /// </summary>
        /// <param name="rootElements"></param>
        /// <param name="jointsById"></param>
        /// <param name="recursive">When false, will only do root elements</param>
        [Obsolete("Use the version that takes a sourceForLogging param instead")]
        public void GenerateAllFrames(ShapeElement[] rootElements, Dictionary<int, AnimationJoint> jointsById, bool recursive = true)
        {
            GenerateAllFrames(rootElements, jointsById, "unknown mod", recursive);
        }


        /// <summary>
        /// Compiles the animation into a bunch of matrices, 31 matrices per frame.
        /// </summary>
        /// <param name="rootElements"></param>
        /// <param name="jointsById"></param>
        /// <param name="recursive">When false, will only do root elements</param>
        public void GenerateAllFrames(ShapeElement[] rootElements, Dictionary<int, AnimationJoint> jointsById, string sourceForLogging, bool recursive = true)
        {
            for (int i = 0; i < rootElements.Length; i++)
            {
                rootElements[i].CacheInverseTransformMatrixRecursive();
            }

            if (KeyFrames.Length == 0)
            {
                throw new Exception("Animation '" + Code + "' from " + sourceForLogging + " has no keyframes, this will cause other errors every time it is ticked");
            }

            if (jointsById.Count >= GlobalConstants.MaxAnimatedElements)
            {
                if (GlobalConstants.MaxAnimatedElements < 46 && jointsById.Count <= 46) throw new Exception("Max joint cap of " + GlobalConstants.MaxAnimatedElements + " reached, needs to be at least " + jointsById.Count + ". In clientsettings.json, please try increasing the \"maxAnimatedElements\": setting to 46.  This works for most GPUs.  Otherwise you might need to disable the creature. Caused by: " + sourceForLogging);
                throw new Exception("Shape model error: " + sourceForLogging + " has " + jointsById.Count + " animation joints which exceeds the max joint cap of "+ GlobalConstants.MaxAnimatedElements + ". Sorry, you'll have to either disable this creature or simplify the model.");
            }

            // Linear segments cache one pose per keyframe and rely on ClientAnimator's runtime
            // lerp between cached poses for sub-segment values. Bezier and Step segments need
            // correct values at intermediate integer frames, so we pre-evaluate the curve at
            // every integer frame inside those segments. Linear-only segments stay sparse, so
            // adding one non-linear keyframe to a long animation only pays the dense-cache
            // cost for that one segment.
            int[] sortedFrames = ComputeCachedFramePositions();
            AnimationFrame[] resolvedKeyFrames = new AnimationFrame[sortedFrames.Length];

            for (int i = 0; i < resolvedKeyFrames.Length; i++)
            {
                resolvedKeyFrames[i] = new AnimationFrame() { FrameNumber = sortedFrames[i] };
            }

            for (int i = 0; i < resolvedKeyFrames.Length; i++)
            {
                jointsDone.Clear();
                GenerateFrame(i, resolvedKeyFrames, rootElements, jointsById, reusableIdentityMatrix, resolvedKeyFrames[i].RootElementTransforms, sourceForLogging, recursive);
            }

            PrevNextKeyFrameByFrame = new AnimationFrame[QuantityFrames][];
            for (int i = 0; i < QuantityFrames; i++)
            {
                getLeftRightResolvedFrame(i, resolvedKeyFrames, out AnimationFrame left, out AnimationFrame right);

                PrevNextKeyFrameByFrame[i] = new AnimationFrame[] { left, right };
            }

        }

        // Returns the sorted set of integer frames at which to cache an AnimationFrame: every
        // keyframe position, plus every integer frame inside a non-Linear segment. Segments
        // are walked per element-CHANNEL (matching getTwoKeyFramesElementForFlag), not per
        // element — a rotation-only keyframe between two position keyframes does not split
        // the position segment, so a Bezier position curve from frame 0 to 10 must densify
        // 1..9 even if a sibling channel has a keyframe at 5.
        private int[] ComputeCachedFramePositions()
        {
            HashSet<int> framePositions = new HashSet<int>();
            for (int i = 0; i < KeyFrames.Length; i++) framePositions.Add(KeyFrames[i].Frame);

            HashSet<string> elementNames = new HashSet<string>();
            for (int i = 0; i < KeyFrames.Length; i++)
            {
                if (KeyFrames[i].Elements == null) continue;
                foreach (var pair in KeyFrames[i].Elements) elementNames.Add(pair.Key);
            }

            List<int> channelKfIndices = new List<int>();
            foreach (string elemName in elementNames)
            {
                for (int channelFlag = 0; channelFlag < 3; channelFlag++)
                {
                    channelKfIndices.Clear();
                    for (int i = 0; i < KeyFrames.Length; i++)
                    {
                        if (KeyFrames[i].Elements != null
                            && KeyFrames[i].Elements.TryGetValue(elemName, out AnimationKeyFrameElement kelem)
                            && kelem.IsSet(channelFlag))
                        {
                            channelKfIndices.Add(i);
                        }
                    }
                    if (channelKfIndices.Count < 2) continue;

                    for (int i = 0; i < channelKfIndices.Count; i++)
                    {
                        int curIdx = channelKfIndices[i];
                        int nextIdx = channelKfIndices[(i + 1) % channelKfIndices.Count];

                        AnimationKeyFrameElement curElem = KeyFrames[curIdx].Elements[elemName];
                        EnumKeyFrameInterpolation mode = ChannelInterp(curElem, channelFlag);
                        if (mode == EnumKeyFrameInterpolation.Linear) continue;

                        int start = KeyFrames[curIdx].Frame;
                        int end = KeyFrames[nextIdx].Frame;
                        if (end <= start) end += QuantityFrames; // loop-around segment
                        for (int f = start + 1; f < end; f++)
                        {
                            framePositions.Add(f % QuantityFrames);
                        }
                    }
                }
            }

            int[] sorted = new int[framePositions.Count];
            int idx = 0;
            foreach (int f in framePositions) sorted[idx++] = f;
            System.Array.Sort(sorted);
            return sorted;
        }

        private static EnumKeyFrameInterpolation ChannelInterp(AnimationKeyFrameElement elem, int channelFlag)
        {
            switch (channelFlag)
            {
                case 0: return elem.PositionInterp;
                case 1: return elem.RotationInterp;
                case 2: return elem.ScaleInterp;
                default: return EnumKeyFrameInterpolation.Linear;
            }
        }

        protected void GenerateFrame(int indexNumber, AnimationFrame[] resKeyFrames, ShapeElement[] elements, Dictionary<int, AnimationJoint> jointsById, float[] modelMatrix, List<ElementPose> transforms, string sourceForLogging, bool recursive = true)
        {
            int frameNumber = resKeyFrames[indexNumber].FrameNumber;

            if (frameNumber >= QuantityFrames) throw new InvalidOperationException("Invalid animation '" + Code + "' from " + sourceForLogging +" has QuantityFrames set to " + QuantityFrames + " but a key frame at frame " + frameNumber + ". QuantityFrames always must be higher than frame number");

            float[] tmpLocalTransform = null;
            for (int i = 0; i < elements.Length; i++)
            {
                ShapeElement element = elements[i];

                ElementPose animTransform = new ElementPose();
                animTransform.ForElement = element;

                try
                {
                    GenerateFrameForElement(frameNumber, element, ref animTransform);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Failed to generate keyframe " + frameNumber + " for animation " + Code + " element " + element.Name + " from " + sourceForLogging, e);
                }
                transforms.Add(animTransform);

                if (element.JointId > 0 && !jointsDone.Contains(element.JointId))
                {
                    jointsDone.Add(element.JointId);
                }

                if (recursive && element.Children != null)
                {
                    float[] animModelMatrix = Mat4f.CloneIt(modelMatrix);
                    if (tmpLocalTransform == null) tmpLocalTransform = Mat4f.Create(); else Mat4f.Identity(tmpLocalTransform);   // Performance note: if not created here, it would be inside GetLocalTransformMatrix() anyhow
                    Mat4f.Mul(animModelMatrix, animModelMatrix, element.GetLocalTransformMatrix(Version, tmpLocalTransform, animTransform));
                    GenerateFrame(indexNumber, resKeyFrames, element.Children, jointsById, animModelMatrix, animTransform.ChildElementPoses, sourceForLogging);
                }
            }
        }



        protected void GenerateFrameForElement(int frameNumber, ShapeElement element, ref ElementPose transform)
        {
            for (int flag = 0; flag < 3; flag++)
            {

                getTwoKeyFramesElementForFlag(frameNumber, element, flag, out AnimationKeyFrameElement curKelem, out AnimationKeyFrameElement nextKelem);

                if (curKelem == null) continue;


                float t;

                if (nextKelem == null || curKelem == nextKelem)
                {
                    nextKelem = curKelem;
                    t = 0;
                }
                else
                {
                    if (nextKelem.Frame < curKelem.Frame)
                    {
                        int quantity = nextKelem.Frame + (QuantityFrames - curKelem.Frame);
                        int framePos = GameMath.Mod(frameNumber - curKelem.Frame, QuantityFrames);
                        
                        t = (float)framePos / quantity;
                    }
                    else
                    {
                        t = (float)(frameNumber - curKelem.Frame) / (nextKelem.Frame - curKelem.Frame);
                    }
                }


                lerpKeyFrameElement(curKelem, nextKelem, flag, t, ref transform);

                transform.RotShortestDistanceX = curKelem.RotShortestDistanceX;
                transform.RotShortestDistanceY = curKelem.RotShortestDistanceY;
                transform.RotShortestDistanceZ = curKelem.RotShortestDistanceZ;
            }
        }


        /// <summary>Resolves a keyframe element into the pose, evaluating the channel's curve at the given segment fraction t. Cache-time site for Linear/Bezier; Step is flagged on the pose so ElementPose.Add can hold the prev value at runtime.</summary>
        protected void lerpKeyFrameElement(AnimationKeyFrameElement prev, AnimationKeyFrameElement next, int forFlag, float t, ref ElementPose transform)
        {
            if (prev == null && next == null) return;

            if (forFlag == 0)
            {
                EnumKeyFrameInterpolation mode = prev.PositionInterp;
                float p0x = prev.OffsetX.Value / 16f, p0y = prev.OffsetY.Value / 16f, p0z = prev.OffsetZ.Value / 16f;
                float p1x = next.OffsetX.Value / 16f, p1y = next.OffsetY.Value / 16f, p1z = next.OffsetZ.Value / 16f;

                if (mode == EnumKeyFrameInterpolation.Step)
                {
                    transform.translateX = p0x;
                    transform.translateY = p0y;
                    transform.translateZ = p0z;
                }
                else if (mode == EnumKeyFrameInterpolation.Bezier)
                {
                    HermiteBasis(t, out float h00, out float h10, out float h01, out float h11);
                    float m0x = (prev.OffsetTangentOutX ?? 0f) / 16f, m1x = (next.OffsetTangentInX ?? 0f) / 16f;
                    float m0y = (prev.OffsetTangentOutY ?? 0f) / 16f, m1y = (next.OffsetTangentInY ?? 0f) / 16f;
                    float m0z = (prev.OffsetTangentOutZ ?? 0f) / 16f, m1z = (next.OffsetTangentInZ ?? 0f) / 16f;
                    transform.translateX = h00 * p0x + h10 * m0x + h01 * p1x + h11 * m1x;
                    transform.translateY = h00 * p0y + h10 * m0y + h01 * p1y + h11 * m1y;
                    transform.translateZ = h00 * p0z + h10 * m0z + h01 * p1z + h11 * m1z;
                }
                else
                {
                    transform.translateX = GameMath.Lerp(p0x, p1x, t);
                    transform.translateY = GameMath.Lerp(p0y, p1y, t);
                    transform.translateZ = GameMath.Lerp(p0z, p1z, t);
                }
                transform.IsStepPosition = mode == EnumKeyFrameInterpolation.Step;
            }
            else if (forFlag == 1)
            {
                EnumKeyFrameInterpolation mode = prev.RotationInterp;
                float p0x = prev.RotationX.Value, p0y = prev.RotationY.Value, p0z = prev.RotationZ.Value;
                float p1x = next.RotationX.Value, p1y = next.RotationY.Value, p1z = next.RotationZ.Value;

                if (mode == EnumKeyFrameInterpolation.Step)
                {
                    transform.degX = p0x;
                    transform.degY = p0y;
                    transform.degZ = p0z;
                }
                else
                {
                    // Honour RotShortestDistance by reframing p1 along the shortest arc before
                    // evaluating. Required at intermediate t (per-frame cache) so cached values
                    // follow the short arc instead of taking the raw long-arc lerp.
                    if (prev.RotShortestDistanceX) p1x = p0x + GameMath.AngleDegDistance(p0x, p1x);
                    if (prev.RotShortestDistanceY) p1y = p0y + GameMath.AngleDegDistance(p0y, p1y);
                    if (prev.RotShortestDistanceZ) p1z = p0z + GameMath.AngleDegDistance(p0z, p1z);

                    if (mode == EnumKeyFrameInterpolation.Bezier)
                    {
                        HermiteBasis(t, out float h00, out float h10, out float h01, out float h11);
                        float m0x = prev.RotationTangentOutX ?? 0f, m1x = next.RotationTangentInX ?? 0f;
                        float m0y = prev.RotationTangentOutY ?? 0f, m1y = next.RotationTangentInY ?? 0f;
                        float m0z = prev.RotationTangentOutZ ?? 0f, m1z = next.RotationTangentInZ ?? 0f;
                        transform.degX = h00 * p0x + h10 * m0x + h01 * p1x + h11 * m1x;
                        transform.degY = h00 * p0y + h10 * m0y + h01 * p1y + h11 * m1y;
                        transform.degZ = h00 * p0z + h10 * m0z + h01 * p1z + h11 * m1z;
                    }
                    else
                    {
                        transform.degX = GameMath.Lerp(p0x, p1x, t);
                        transform.degY = GameMath.Lerp(p0y, p1y, t);
                        transform.degZ = GameMath.Lerp(p0z, p1z, t);
                    }
                }
                transform.IsStepRotation = mode == EnumKeyFrameInterpolation.Step;
            }
            else
            {
                EnumKeyFrameInterpolation mode = prev.ScaleInterp;
                float p0x = prev.StretchX.Value, p0y = prev.StretchY.Value, p0z = prev.StretchZ.Value;
                float p1x = next.StretchX.Value, p1y = next.StretchY.Value, p1z = next.StretchZ.Value;

                if (mode == EnumKeyFrameInterpolation.Step)
                {
                    transform.scaleX = p0x;
                    transform.scaleY = p0y;
                    transform.scaleZ = p0z;
                }
                else if (mode == EnumKeyFrameInterpolation.Bezier)
                {
                    HermiteBasis(t, out float h00, out float h10, out float h01, out float h11);
                    float m0x = prev.StretchTangentOutX ?? 0f, m1x = next.StretchTangentInX ?? 0f;
                    float m0y = prev.StretchTangentOutY ?? 0f, m1y = next.StretchTangentInY ?? 0f;
                    float m0z = prev.StretchTangentOutZ ?? 0f, m1z = next.StretchTangentInZ ?? 0f;
                    transform.scaleX = h00 * p0x + h10 * m0x + h01 * p1x + h11 * m1x;
                    transform.scaleY = h00 * p0y + h10 * m0y + h01 * p1y + h11 * m1y;
                    transform.scaleZ = h00 * p0z + h10 * m0z + h01 * p1z + h11 * m1z;
                }
                else
                {
                    transform.scaleX = GameMath.Lerp(p0x, p1x, t);
                    transform.scaleY = GameMath.Lerp(p0y, p1y, t);
                    transform.scaleZ = GameMath.Lerp(p0z, p1z, t);
                }
                transform.IsStepScale = mode == EnumKeyFrameInterpolation.Step;
            }
        }

        /// <summary>Cubic Hermite basis with tangents pre-normalised to per-segment-t (glTF 2.0 CUBICSPLINE with t_d=1, https://github.com/KhronosGroup/glTF/blob/main/specification/2.0/Specification.adoc#interpolation-cubic). Computed once per channel and reused across X/Y/Z. With m0=m1=0 the curve collapses to smoothstep.</summary>
        private static void HermiteBasis(float t, out float h00, out float h10, out float h01, out float h11)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            h00 = 2 * t3 - 3 * t2 + 1;
            h10 = t3 - 2 * t2 + t;
            h01 = -2 * t3 + 3 * t2;
            h11 = t3 - t2;
        }


        

        protected void getTwoKeyFramesElementForFlag(int frameNumber, ShapeElement forElement, int forFlag, out AnimationKeyFrameElement left, out AnimationKeyFrameElement right)
        {
            left = null;
            right = null;

            int rightKfIndex = seekRightKeyFrame(frameNumber, forElement, forFlag);
            if (rightKfIndex == -1) return;

            right = KeyFrames[rightKfIndex].GetKeyFrameElement(forElement);

            int leftKfIndex = seekLeftKeyFrame(rightKfIndex, forElement, forFlag);
            if (leftKfIndex == -1)
            {
                left = right;
                return;
            }

            left = KeyFrames[leftKfIndex].GetKeyFrameElement(forElement);
        }


        private int seekRightKeyFrame(int aboveFrameNumber, ShapeElement forElement, int forFlag)
        {
            int firstIndex = -1;

            for (int i = 0; i < KeyFrames.Length; i++)
            {
                AnimationKeyFrame keyframe = KeyFrames[i];
                
                AnimationKeyFrameElement kelem = keyframe.GetKeyFrameElement(forElement);
                if (kelem != null && kelem.IsSet(forFlag))
                {
                    if (firstIndex == -1) firstIndex = i;
                    if (keyframe.Frame <= aboveFrameNumber) continue;

                    return i;
                }
            }

            return firstIndex;
        }

        private int seekLeftKeyFrame(int leftOfKeyFrameIndex, ShapeElement forElement, int forFlag)
        {
            for (int i = 0; i < KeyFrames.Length; i++)
            {
                int index = GameMath.Mod(leftOfKeyFrameIndex - i - 1, KeyFrames.Length);
                AnimationKeyFrame keyframe = KeyFrames[index];

                AnimationKeyFrameElement kelem = keyframe.GetKeyFrameElement(forElement);
                if (kelem != null && kelem.IsSet(forFlag))
                {
                    return index;
                }
            }

            return -1;
        }


        protected void getLeftRightResolvedFrame(int frameNumber, AnimationFrame[] frames, out AnimationFrame left, out AnimationFrame right)
        {
            left = null;
            right = null;

            // Go left of frameNumber until we hit the first keyframe
            int keyframeIndex = frames.Length - 1;
            bool loopAround = false;

            while (keyframeIndex >= -1)
            {
                AnimationFrame keyframe = frames[GameMath.Mod(keyframeIndex, frames.Length)];
                keyframeIndex--;

                if (keyframe.FrameNumber <= frameNumber || loopAround)
                {
                    left = keyframe;
                    break;
                }

                if (keyframeIndex == -1) loopAround = true;
            }


            keyframeIndex += 2;
            AnimationFrame nextkeyframe = frames[GameMath.Mod(keyframeIndex, frames.Length)];
            right = nextkeyframe;
            return;
        }

        public Animation Clone()
        {
            return new Animation()
            {
                Code = Code,
                CodeCrc32 = CodeCrc32,
                EaseAnimationSpeed = EaseAnimationSpeed,
                jointsDone = jointsDone,
                KeyFrames = CloneKeyFrames(),
                Name = Name,
                OnActivityStopped = OnActivityStopped,
                OnAnimationEnd = OnAnimationEnd,
                QuantityFrames = QuantityFrames,
                Version = Version,
            };
        }

        private AnimationKeyFrame[] CloneKeyFrames()
        {
            AnimationKeyFrame[] elems = new AnimationKeyFrame[KeyFrames.Length];

            for (int i = 0; i < KeyFrames.Length; i++)
            {
                elems[i] = KeyFrames[i].Clone();
            }

            return elems;
        }
    }
}
