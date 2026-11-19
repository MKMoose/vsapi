using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

#nullable disable

namespace Vintagestory.API.Common
{

    /// <summary>
    /// Syncs every frame with entity.ActiveAnimationsByAnimCode, starts and stops animations when necessary
    /// and does recursive interpolation on the rotation, position and scale value for each frame, for each element and for each active element
    /// this produces always correctly blended animations but is significantly more costly for the cpu when compared to the technique used by the <see cref="AnimatorBase"/>.
    /// </summary>
    public class ClientAnimator : AnimatorBase
    {
        [ThreadStatic]
        static float[] ReusableIdentityMatrix;
        [ThreadStatic]
        static float[] localTransformMatrix;
        [ThreadStatic]
        static float[] tmpMatrix;
        public Dictionary<int, AnimationJoint> jointsById;
        protected HashSet<int> jointsDone = new HashSet<int>();

        public static int MaxConcurrentAnimations = 16;
        int maxDepth;
        List<ElementPose>[][] frameByDepthByAnimation;
        List<ElementPose>[][] nextFrameTransformsByAnimation;
        ShapeElementWeights[][][] weightsByAnimationAndElement;


        Action<string, AnimationSound> onShouldPlaySoundListener = null;

        public static ClientAnimator CreateForEntity(Entity entity, List<ElementPose> rootPoses, Animation[] animations, ShapeElement[] rootElements, Dictionary<int, AnimationJoint> jointsById)
        {
            ClientAnimator ca;
            if (entity is EntityAgent)
            {
                EntityAgent entityag = entity as EntityAgent;
                ca = new ClientAnimator(
                    () => entityag.Controls.MovespeedMultiplier * entityag.GetWalkSpeedMultiplier(0.3),
                    rootPoses,
                    animations,
                    rootElements,
                    jointsById,
                    entity.AnimManager.TriggerAnimationStopped,
                    entity.AnimManager.ShouldPlaySound
                );
                
            } else
            {
                ca = new ClientAnimator(
                    () => 1,
                    rootPoses,
                    animations,
                    rootElements,
                    jointsById,
                    entity.AnimManager.TriggerAnimationStopped,
                    entity.AnimManager.ShouldPlaySound
                );
            }

            ca.entityForLogging = entity;
            return ca;
        }

        public static ClientAnimator CreateForEntity(Entity entity, Animation[] animations, ShapeElement[] rootElements, Dictionary<int, AnimationJoint> jointsById)
        {
            ClientAnimator ca;
            if (entity is EntityAgent)
            {
                EntityAgent entityag = entity as EntityAgent;

                ca = new ClientAnimator(
                    () => entityag.Controls.MovespeedMultiplier * entityag.GetWalkSpeedMultiplier(0.3),
                    animations,
                    rootElements,
                    jointsById,
                    entity.AnimManager.TriggerAnimationStopped,
                    entity.AnimManager.ShouldPlaySound
                );
            }
            else
            {
                ca = new ClientAnimator(
                    () => 1,
                    animations,
                    rootElements,
                    jointsById,
                    entity.AnimManager.TriggerAnimationStopped,
                    entity.AnimManager.ShouldPlaySound
                );
            }

            ca.entityForLogging = entity;
            return ca;
        }

        public ClientAnimator(                  // This constructor is called only on the server side, by ServerAnimator constructors.  Note, no call to initMatrices, to save RAM on a server
            WalkSpeedSupplierDelegate walkSpeedSupplier,
            Animation[] animations,
            Action<string> onAnimationStoppedListener = null,
            Action<string, AnimationSound> onShouldPlaySoundListener = null
        ) : base(walkSpeedSupplier, animations, onAnimationStoppedListener)
        {
            this.onShouldPlaySoundListener = onShouldPlaySoundListener;
            initFields();
        }

        public ClientAnimator(                  // This constructor is called only on the client side
            WalkSpeedSupplierDelegate walkSpeedSupplier,
            List<ElementPose> rootPoses,
            Animation[] animations,
            ShapeElement[] rootElements,
            Dictionary<int, AnimationJoint> jointsById,
            Action<string> onAnimationStoppedListener = null,
            Action<string, AnimationSound> onShouldPlaySoundListener = null
        ) : base(walkSpeedSupplier, animations, onAnimationStoppedListener)
        {
            this.RootElements = rootElements;
            this.jointsById = jointsById;
            this.RootPoses = rootPoses;
            this.onShouldPlaySoundListener = onShouldPlaySoundListener;
            LoadAttachmentPoints(RootPoses);
            initFields();
            initMatrices(MaxJointId);
        }

        public ClientAnimator(                  // This constructor is called only on the client side
            WalkSpeedSupplierDelegate walkSpeedSupplier,
            Animation[] animations,
            ShapeElement[] rootElements,
            Dictionary<int, AnimationJoint> jointsById,
            Action<string> onAnimationStoppedListener = null,
            Action<string, AnimationSound> onShouldPlaySoundListener = null
        ) : base(walkSpeedSupplier, animations, onAnimationStoppedListener)
        {
            this.RootElements = rootElements;
            this.jointsById = jointsById;

            RootPoses = new List<ElementPose>();
            LoadPosesAndAttachmentPoints(rootElements, RootPoses);
            this.onShouldPlaySoundListener = onShouldPlaySoundListener;
            initFields();
            initMatrices(MaxJointId);
        }

        protected virtual void initFields()
        {
            maxDepth = 2 + (RootPoses == null ? 0 : getMaxDepth(RootPoses, 1));

            frameByDepthByAnimation = new List<ElementPose>[maxDepth][];
            nextFrameTransformsByAnimation = new List<ElementPose>[maxDepth][];
            weightsByAnimationAndElement = new ShapeElementWeights[maxDepth][][];

            for (int i = 0; i < maxDepth; i++)
            {
                frameByDepthByAnimation[i] = new List<ElementPose>[MaxConcurrentAnimations];
                nextFrameTransformsByAnimation[i] = new List<ElementPose>[MaxConcurrentAnimations];
                weightsByAnimationAndElement[i] = new ShapeElementWeights[MaxConcurrentAnimations][];
            }
        }

        protected virtual void initMatrices(int maxJointId)
        {
            // Matrices are only required on client side; and from 1.20.5 limited to the MaxJointId (x16) in length to avoid wasting RAM
            var identMat = ClientAnimator.identMat;
            var defaultPoseMatrices = new float[16 * maxJointId];
            for (int i = 0; i < defaultPoseMatrices.Length; i++)
            {
                defaultPoseMatrices[i] = identMat[i % 16];
            }
            TransformationMatricesDefaultPose = defaultPoseMatrices;
            TransformationMatrices = new float[defaultPoseMatrices.Length];
        }

        public override void ReloadAttachmentPoints()
        {
            LoadAttachmentPoints(RootPoses);
        }

        protected virtual void LoadAttachmentPoints(List<ElementPose> cachedPoses)
        {
            for (int i = 0; i < cachedPoses.Count; i++)
            {
                ElementPose elem = cachedPoses[i];

                var attachmentPoints = elem.ForElement.AttachmentPoints;
                if (attachmentPoints != null)
                {
                    for (int j = 0; j < attachmentPoints.Length; j++)
                    {
                        AttachmentPoint apoint = attachmentPoints[j];
                        AttachmentPointByCode[apoint.Code] = new AttachmentPointAndPose() {
                            AttachPoint = apoint,
                            CachedPose = elem
                        };
                    }
                }

                if (elem.ChildElementPoses != null)
                {
                    LoadAttachmentPoints(elem.ChildElementPoses);
                }
            }
        }

        protected virtual void LoadPosesAndAttachmentPoints(ShapeElement[] elements, List<ElementPose> intoPoses)
        {
            ElementPose pose;
            for (int i = 0; i < elements.Length; i++)
            {
                ShapeElement elem = elements[i];

                intoPoses.Add(pose = new ElementPose());
                pose.AnimModelMatrix = Mat4f.Create();
                pose.ForElement = elem;

                if (elem.AttachmentPoints != null)
                {
                    for (int j = 0; j < elem.AttachmentPoints.Length; j++)
                    {
                        AttachmentPoint apoint = elem.AttachmentPoints[j];
                        AttachmentPointByCode[apoint.Code] = new AttachmentPointAndPose() {
                            AttachPoint = apoint,
                            CachedPose = pose
                        };
                    }
                }

                if (elem.Children != null)
                {
                    pose.ChildElementPoses = new List<ElementPose>(elem.Children.Length);
                    LoadPosesAndAttachmentPoints(elem.Children, pose.ChildElementPoses);
                }
            }
        }

        private int getMaxDepth(List<ElementPose> poses, int depth)
        {
            int maxDepth = depth;
            for (int i = 0; i < poses.Count; i++)
            {
                var pose = poses[i];

                if (pose.ChildElementPoses != null)
                {
                    maxDepth = Math.Max(maxDepth, getMaxDepth(pose.ChildElementPoses, depth + 1));
                }
            }

            return maxDepth;
        }

        public override ElementPose GetPosebyName(string name, StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
        {
            return getPosebyName(RootPoses, name);
        }

        private ElementPose getPosebyName(List<ElementPose> poses, string name, StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
        {
            for (int i = 0; i < poses.Count; i++)
            {
                var pose = poses[i];
                if (pose.ForElement.Name.Equals(name, stringComparison)) return pose;

                if (pose.ChildElementPoses != null)
                {
                    var foundPose = getPosebyName(pose.ChildElementPoses, name);
                    if (foundPose != null) return foundPose;
                }
            }

            return null;
        }

        protected override void AnimNowActive(RunningAnimation anim, AnimationMetaData animData)
        {
            base.AnimNowActive(anim, animData);

            if (anim.Animation.PrevNextKeyFrameByFrame == null)
            {
                anim.Animation.GenerateAllFrames(RootElements, jointsById, entityForLogging?.Code);
            }

            anim.LoadWeights(RootElements);
        }

        protected override void AnimMetaChanged(RunningAnimation anim, AnimationMetaData animData)
        {
            base.AnimMetaChanged(anim, animData);

            anim.LoadWeights(RootElements);
        }


        int[] prevFrames = new int[MaxConcurrentAnimations];
        int[] nextFrames = new int[MaxConcurrentAnimations];
        float[] lerpByAnimation = new float[MaxConcurrentAnimations];

        public override int MaxJointId => jointsById.Count + 1;


        float skipFramesAccum = 0;
        public override void OnFrame(Dictionary<string, AnimationMetaData> activeAnimationsByAnimCode, float dt)
        {
            if (RuntimeEnv.RetroMode)
            {
                skipFramesAccum += dt;
                if (skipFramesAccum < 0.1f) return;
                skipFramesAccum = 0f;
                dt = 0.1f;
            }

            for (int j = 0; j < activeAnimCount; j++)
            {
                RunningAnimation anim = CurAnims[j];
                if (anim.Animation.PrevNextKeyFrameByFrame == null && anim.Animation.KeyFrames.Length > 0)
                {
                    anim.Animation.GenerateAllFrames(RootElements, jointsById, entityForLogging?.Code);
                }

                if (anim.meta.AnimationSounds != null && anim.meta.AnimationSounds.Length > 0 && onShouldPlaySoundListener != null)
                {
                    if (anim.SoundPlayedAtIteration == null) anim.SoundPlayedAtIteration = new int[anim.meta.AnimationSounds.Length].Fill(-1);

                    for (int i = 0; i < anim.meta.AnimationSounds.Length; i++)
                    {
                        var animsound = anim.meta.AnimationSounds[i];
                        if (anim.CurrentFrame >= animsound.Frame && anim.SoundPlayedAtIteration[i] != anim.Iterations && anim.Active && anim.NowPlaying)
                        {
                            onShouldPlaySoundListener(anim.meta.Animation, animsound);
                            anim.SoundPlayedAtIteration[i] = anim.Iterations;
                        }
                    }
                }
            }

            base.OnFrame(activeAnimationsByAnimCode, dt);
        }

        protected override void calculateMatrices(float dt)
        {
            if (!CalculateMatrices) return;

            jointsDone.Clear();

            int animVersion = 0;

            int maxcount = activeAnimCount;
            var CurAnims = this.CurAnims;
            var weightsByAnimation = this.weightsByAnimationAndElement[0];
            var frameByAnimation = frameByDepthByAnimation[0];
            for (int j = 0; j < CurAnims.Length; j++)
            {
                if (j >= maxcount) break;
                RunningAnimation anim = CurAnims[j];

                weightsByAnimation[j] = anim.ElementWeights;

                animVersion = Math.Max(animVersion, anim.Animation.Version);

                AnimationFrame[] prevNextFrame = anim.Animation.PrevNextKeyFrameByFrame[(int)anim.CurrentFrame % anim.Animation.QuantityFrames];
                AnimationFrame prevNextFrame0 = prevNextFrame[0];
                frameByAnimation[j] = prevNextFrame0.RootElementTransforms;
                prevFrames[j] = prevNextFrame0.FrameNumber;

                if (anim.Animation.OnAnimationEnd == EnumEntityAnimationEndHandling.Hold && (int)anim.CurrentFrame + 1 == anim.Animation.QuantityFrames)
                {
                    nextFrameTransformsByAnimation[0][j] = prevNextFrame0.RootElementTransforms;
                    nextFrames[j] = prevNextFrame0.FrameNumber;
                }
                else
                {
                    AnimationFrame prevNextFrame1 = prevNextFrame[1];
                    nextFrameTransformsByAnimation[0][j] = prevNextFrame1.RootElementTransforms;
                    nextFrames[j] = prevNextFrame1.FrameNumber;
                }

                int prevF = prevFrames[j];
                int nextF = nextFrames[j];

                // May loop around, so nextFrame can be smaller than prevFrame
                float keyFrameDist = nextF > prevF ? (nextF - prevF) : (anim.Animation.QuantityFrames - prevF + nextF);
                float curFrameDist = anim.CurrentFrame >= prevF ? (anim.CurrentFrame - prevF) : (anim.Animation.QuantityFrames - prevF + anim.CurrentFrame);
                lerpByAnimation[j] = curFrameDist / keyFrameDist;
            }

            calculateMatrices(
                animVersion,
                dt,
                RootPoses,
                weightsByAnimation,
                ReusableIdentityMatrix ??= Mat4f.Create(),
                frameByAnimation,
                nextFrameTransformsByAnimation[0],
                0
            );

            var TransformationMatrices = this.TransformationMatrices;
            if (TransformationMatrices != null)
            {
                // radfast 20.2.25:  jointIdMul16 is (jointId * 16); I think this is the highest performance way to loop through this, taking account of the many array bounds checks
                for (int jointidMul16 = 0; jointidMul16 < TransformationMatrices.Length; jointidMul16 += 16)    
                {
                    if (jointsById.ContainsKey(jointidMul16 / 16)) continue;

                    for (int j = 0; j < 16; j++)
                    {
                        TransformationMatrices[jointidMul16 + j] = identMat[j];
                    }
                }
            }

            foreach (var val in AttachmentPointByCode)
            {
                var cachedMatrix = val.Value.CachedPose.AnimModelMatrix;
                var animMatrix = val.Value.AnimModelMatrix;
                for (int i = 0; i < 16; i++)
                {
                    animMatrix[i] = cachedMatrix[i];
                }
            }
        }



        // Careful when changing around stuff in here, this is a recursively called method
        private void calculateMatrices(
            int animVersion,
            float dt,
            List<ElementPose> outFrame,
            ShapeElementWeights[][] weightsByAnimationAndElement,
            float[] modelMatrix,
            List<ElementPose>[] nowKeyFrameByAnimation,
            List<ElementPose>[] nextInKeyFrameByAnimation,
            int depth
        )
        {
            depth++;
            List<ElementPose>[] nowChildKeyFrameByAnimation = this.frameByDepthByAnimation[depth];
            List<ElementPose>[] nextChildKeyFrameByAnimation = this.nextFrameTransformsByAnimation[depth];
            ShapeElementWeights[][] childWeightsByAnimationAndElement = this.weightsByAnimationAndElement[depth];

            localTransformMatrix ??= Mat4f.Create();
            for (int childPoseIndex = 0; childPoseIndex < outFrame.Count; childPoseIndex++)
            {
                ElementPose outFramePose = outFrame[childPoseIndex];
                ShapeElement elem = outFramePose.ForElement;

                outFramePose.SetMat(modelMatrix);
                Mat4f.Identity(localTransformMatrix);

                outFramePose.Clear();

                float weightSum = 0f;
#if DEBUG
                StringBuilder sb = null;
                if (EleWeightDebug) sb = new StringBuilder();
#endif
                for (int animIndex = 0; animIndex < activeAnimCount; animIndex++)
                {
                    RunningAnimation anim = CurAnims[animIndex];

                    ShapeElementWeights sew = weightsByAnimationAndElement[animIndex][childPoseIndex];

                    if (sew.BlendMode != EnumAnimationBlendMode.Add)
                    {
                        weightSum += sew.Weight * anim.EasingFactor;
                    }

#if DEBUG
                    if (EleWeightDebug) sb.Append(string.Format("{0:0.0} from {1} (blendmode {2}), ", sew.Weight * anim.EasingFactor, anim.Animation.Code, sew.BlendMode));
#endif
                }
#if DEBUG
                if (EleWeightDebug)
                {
                    if (eleWeights.ContainsKey(elem.Name)) eleWeights[elem.Name] += sb.ToString();
                    else eleWeights[elem.Name] = sb.ToString();
                }
#endif

                for (int animIndex = 0; animIndex < activeAnimCount; animIndex++)
                {
                    RunningAnimation anim = CurAnims[animIndex];
                    ShapeElementWeights sew = weightsByAnimationAndElement[animIndex][childPoseIndex];

                    ElementPose nowFramePose = nowKeyFrameByAnimation[animIndex][childPoseIndex];
                    ElementPose nextFramePose = nextInKeyFrameByAnimation[animIndex][childPoseIndex];

                    // Children can have non-zero weights even when this element has zero weight.
                    nowChildKeyFrameByAnimation[animIndex] = nowFramePose.ChildElementPoses;
                    childWeightsByAnimationAndElement[animIndex] = sew.ChildElements;
                    nextChildKeyFrameByAnimation[animIndex] = nextFramePose.ChildElementPoses;

                    if (sew.BlendMode != EnumAnimationBlendMode.Add && sew.Weight == 0f)
                    {
                        continue;
                    }

                    // BlendMode "Add" uses the easing factor directly, independently of element weights.
                    anim.CalcBlendedWeight(sew.BlendMode == EnumAnimationBlendMode.Add ? 0f : weightSum / sew.Weight, sew.BlendMode);

                    outFramePose.Add(nowFramePose, nextFramePose, lerpByAnimation[animIndex], anim.BlendedWeight);
                }

                elem.GetLocalTransformMatrix(animVersion, localTransformMatrix, outFramePose);
                Mat4f.Mul(outFramePose.AnimModelMatrix, outFramePose.AnimModelMatrix, localTransformMatrix);

                if (TransformationMatrices != null)     // It is null on a server, non-null on a client
                {
                    if (elem.JointId > 0 && !jointsDone.Contains(elem.JointId))
                    {
                        var tmpMatrixLocal = tmpMatrix ??= Mat4f.Create();
                        Mat4f.Mul(tmpMatrixLocal, outFramePose.AnimModelMatrix, elem.inverseModelTransform);

                        int index = 16 * elem.JointId;
                        var transformationMatrices = TransformationMatrices;
                        if (index + 16 > transformationMatrices.Length)     // Check we have space for this joint: we normally should, if MaxJointId was correct, but a mod could have modified the shape or something
                        {
                            var transformationMatricesDefault = this.TransformationMatricesDefaultPose;
                            initMatrices(elem.JointId + 1);    // This replaces the matrices in both fields, but our local references are still the old populated matrices
                            Array.Copy(transformationMatrices, this.TransformationMatrices, transformationMatrices.Length);
                            Array.Copy(transformationMatricesDefault, this.TransformationMatricesDefaultPose, transformationMatricesDefault.Length);
                            transformationMatrices = this.TransformationMatrices;
                        }
                        for (int i = 0; i < 16; i++)
                        {
                            transformationMatrices[index + i] = tmpMatrixLocal[i];
                        }

                        jointsDone.Add(elem.JointId);
                    }
                }

                if (outFramePose.ChildElementPoses != null)
                {
                    calculateMatrices(
                        animVersion,
                        dt,
                        outFramePose.ChildElementPoses,
                        childWeightsByAnimationAndElement,
                        outFramePose.AnimModelMatrix,
                        nowChildKeyFrameByAnimation,
                        nextChildKeyFrameByAnimation,
                        depth
                    );
                }
            }
        }

#if DEBUG
        static bool EleWeightDebug=false;
#endif
        Dictionary<string, string> eleWeights = new Dictionary<string, string>();
        public override string DumpCurrentState()
        {
#if DEBUG
            EleWeightDebug = true;
#endif
            eleWeights.Clear();
            calculateMatrices(1 / 60f);
#if DEBUG
            EleWeightDebug = false;
#endif

            return base.DumpCurrentState() + "\nElement weights:\n" + string.Join("\n", eleWeights.Select(x => x.Key + ": " + x.Value));
        }

    }
}
