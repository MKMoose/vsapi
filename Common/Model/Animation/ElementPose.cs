using System.Collections.Generic;

#nullable disable

namespace Vintagestory.API.Common
{
    /// <summary>
    /// The position of an element.
    /// </summary>
    public class ElementPose
    {
        /// <summary>
        /// The element this positioning is for.
        /// </summary>
        public ShapeElement ForElement;

        /// <summary>
        /// The model matrix of this element.
        /// </summary>
        public float[] AnimModelMatrix;

        public List<ElementPose> ChildElementPoses = new List<ElementPose>();

        public float degOffX, degOffY, degOffZ;

        public float degX, degY, degZ;
        public float scaleX = 1, scaleY = 1, scaleZ = 1;
        public float translateX, translateY, translateZ;

        public bool RotShortestDistanceX;
        public bool RotShortestDistanceY;
        public bool RotShortestDistanceZ;

        /// <summary>True when the outgoing position segment from this pose's keyframe is Step (hold prev value until the next keyframe). Only set for poses on Step keyframes.</summary>
        public bool IsStepPosition;
        /// <summary>True when the outgoing rotation segment from this pose's keyframe is Step.</summary>
        public bool IsStepRotation;
        /// <summary>True when the outgoing scale segment from this pose's keyframe is Step.</summary>
        public bool IsStepScale;

        public void Clear()
        {
            degX = 0;
            degY = 0;
            degZ = 0;
            scaleX = 1;
            scaleY = 1;
            scaleZ = 1;
            translateX = 0;
            translateY = 0;
            translateZ = 0;
        }

        public void Add(ElementPose tf, ElementPose tfNext, float l, float weight)
        {
            if (tf.IsStepRotation)
            {
                degX += tf.degX * weight;
                degY += tf.degY * weight;
                degZ += tf.degZ * weight;
            }
            else
            {
                if (tf.RotShortestDistanceX)
                {
                    float distX = MathTools.GameMath.AngleDegDistance(tf.degX, tfNext.degX);
                    degX += (tf.degX + distX * l);
                }
                else
                {
                    degX += (tf.degX * (1 - l) + tfNext.degX * l) * weight;
                }

                if (tf.RotShortestDistanceY)
                {
                    float distY = MathTools.GameMath.AngleDegDistance(tf.degY, tfNext.degY);
                    degY += (tf.degY + distY * l);
                }
                else
                {
                    degY += (tf.degY * (1 - l) + tfNext.degY * l) * weight;
                }

                if (tf.RotShortestDistanceZ)
                {
                    float distZ = MathTools.GameMath.AngleDegDistance(tf.degZ, tfNext.degZ);
                    degZ += (tf.degZ + distZ * l);
                }
                else
                {
                    degZ += (tf.degZ * (1 - l) + tfNext.degZ * l) * weight;
                }
            }

            if (tf.IsStepScale)
            {
                scaleX += (tf.scaleX - 1) * weight;
                scaleY += (tf.scaleY - 1) * weight;
                scaleZ += (tf.scaleZ - 1) * weight;
            }
            else
            {
                scaleX += ((tf.scaleX - 1) * (1 - l) + (tfNext.scaleX - 1) * l) * weight;
                scaleY += ((tf.scaleY - 1) * (1 - l) + (tfNext.scaleY - 1) * l) * weight;
                scaleZ += ((tf.scaleZ - 1) * (1 - l) + (tfNext.scaleZ - 1) * l) * weight;
            }

            if (tf.IsStepPosition)
            {
                translateX += tf.translateX * weight;
                translateY += tf.translateY * weight;
                translateZ += tf.translateZ * weight;
            }
            else
            {
                translateX += (tf.translateX * (1 - l) + tfNext.translateX * l) * weight;
                translateY += (tf.translateY * (1 - l) + tfNext.translateY * l) * weight;
                translateZ += (tf.translateZ * (1 - l) + tfNext.translateZ * l) * weight;
            }
        }

        internal void SetMat(float[] modelMatrix)
        {
            var AnimModelMatrix = this.AnimModelMatrix;
            for (int i = 0; i < modelMatrix.Length; i++)
            {
                AnimModelMatrix[i] = modelMatrix[i];
            }
        }

        public override string ToString()
        {
            return string.Format("translate: {0}/{1}/{2}, rotate: {3}/{4}/{5}, scale: {6}/{7}/{8}", translateX, translateY, translateZ, degX, degY, degZ, scaleX, scaleY, scaleZ);
        }
    }
}
