using System;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

#nullable disable

namespace Vintagestory.API.Common
{
    /// <summary>Per-channel interpolation mode for the curve outgoing from a keyframe to the next one.</summary>
    public enum EnumKeyFrameInterpolation
    {
        /// <summary>Linear blend between this keyframe's value and the next. Legacy default.</summary>
        Linear = 0,
        /// <summary>Cubic Hermite curve using this keyframe's out-tangent and the next keyframe's in-tangent. With both tangents at 0 the curve collapses to a smoothstep S-curve.</summary>
        Bezier = 1,
        /// <summary>Hold this keyframe's value until the next keyframe is reached.</summary>
        Step = 2
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class AnimationKeyFrameElement
    {
        private const uint PositionMask = 0x007;
        private const uint StretchMask = 0x038;
        private const uint RotationMask = 0x1C0;
        private const uint OriginMask = 0xE00;
        private const uint ValueMask = PositionMask | StretchMask | RotationMask | OriginMask;
        private const uint InterpolationMask = 3;

        // Bits 0-11 track nullable values, 12-14 hold shortest-distance flags,
        // and 15-20 hold the three two-bit interpolation modes.
        private uint flags;
        private float offsetX, offsetY, offsetZ;
        private float stretchX, stretchY, stretchZ;
        private float rotationX, rotationY, rotationZ;
        private float originX, originY, originZ;

        [JsonProperty]
        public float? OffsetX { get => GetValue(offsetX, 1u << 0); set => SetValue(ref offsetX, 1u << 0, value); }
        [JsonProperty]
        public float? OffsetY { get => GetValue(offsetY, 1u << 1); set => SetValue(ref offsetY, 1u << 1, value); }
        [JsonProperty]
        public float? OffsetZ { get => GetValue(offsetZ, 1u << 2); set => SetValue(ref offsetZ, 1u << 2, value); }
        [JsonProperty]
        public float? StretchX { get => GetValue(stretchX, 1u << 3); set => SetValue(ref stretchX, 1u << 3, value); }
        [JsonProperty]
        public float? StretchY { get => GetValue(stretchY, 1u << 4); set => SetValue(ref stretchY, 1u << 4, value); }
        [JsonProperty]
        public float? StretchZ { get => GetValue(stretchZ, 1u << 5); set => SetValue(ref stretchZ, 1u << 5, value); }
        [JsonProperty]
        public float? RotationX { get => GetValue(rotationX, 1u << 6); set => SetValue(ref rotationX, 1u << 6, value); }
        [JsonProperty]
        public float? RotationY { get => GetValue(rotationY, 1u << 7); set => SetValue(ref rotationY, 1u << 7, value); }
        [JsonProperty]
        public float? RotationZ { get => GetValue(rotationZ, 1u << 8); set => SetValue(ref rotationZ, 1u << 8, value); }
        [JsonProperty]
        public float? OriginX { get => GetValue(originX, 1u << 9); set => SetValue(ref originX, 1u << 9, value); }
        [JsonProperty]
        public float? OriginY { get => GetValue(originY, 1u << 10); set => SetValue(ref originY, 1u << 10, value); }
        [JsonProperty]
        public float? OriginZ { get => GetValue(originZ, 1u << 11); set => SetValue(ref originZ, 1u << 11, value); }

        [JsonProperty]
        public bool RotShortestDistanceX { get => (flags & (1u << 12)) != 0; set => SetFlag(1u << 12, value); }
        [JsonProperty]
        public bool RotShortestDistanceY { get => (flags & (1u << 13)) != 0; set => SetFlag(1u << 13, value); }
        [JsonProperty]
        public bool RotShortestDistanceZ { get => (flags & (1u << 14)) != 0; set => SetFlag(1u << 14, value); }

        /// <summary>Interpolation mode for the position channel's curve outgoing from this keyframe.</summary>
        [JsonProperty]
        public EnumKeyFrameInterpolation PositionInterp { get => GetInterpolation(15); set => SetInterpolation(15, value); }
        /// <summary>Interpolation mode for the rotation channel's curve outgoing from this keyframe.</summary>
        [JsonProperty]
        public EnumKeyFrameInterpolation RotationInterp { get => GetInterpolation(17); set => SetInterpolation(17, value); }
        /// <summary>Interpolation mode for the scale channel's curve outgoing from this keyframe.</summary>
        [JsonProperty]
        public EnumKeyFrameInterpolation ScaleInterp { get => GetInterpolation(19); set => SetInterpolation(19, value); }

        /// <summary>Cubic Hermite tangent for bezier interpolation, in value-per-segment-t. Null = 0 (smoothstep when both endpoints are 0).</summary>
        [JsonProperty]
        public float? OffsetTangentInX { get => GetTangent(0); set => SetTangent(0, value); }
        [JsonProperty]
        public float? OffsetTangentInY { get => GetTangent(1); set => SetTangent(1, value); }
        [JsonProperty]
        public float? OffsetTangentInZ { get => GetTangent(2); set => SetTangent(2, value); }
        [JsonProperty]
        public float? OffsetTangentOutX { get => GetTangent(3); set => SetTangent(3, value); }
        [JsonProperty]
        public float? OffsetTangentOutY { get => GetTangent(4); set => SetTangent(4, value); }
        [JsonProperty]
        public float? OffsetTangentOutZ { get => GetTangent(5); set => SetTangent(5, value); }

        [JsonProperty]
        public float? RotationTangentInX { get => GetTangent(6); set => SetTangent(6, value); }
        [JsonProperty]
        public float? RotationTangentInY { get => GetTangent(7); set => SetTangent(7, value); }
        [JsonProperty]
        public float? RotationTangentInZ { get => GetTangent(8); set => SetTangent(8, value); }
        [JsonProperty]
        public float? RotationTangentOutX { get => GetTangent(9); set => SetTangent(9, value); }
        [JsonProperty]
        public float? RotationTangentOutY { get => GetTangent(10); set => SetTangent(10, value); }
        [JsonProperty]
        public float? RotationTangentOutZ { get => GetTangent(11); set => SetTangent(11, value); }

        [JsonProperty]
        public float? StretchTangentInX { get => GetTangent(12); set => SetTangent(12, value); }
        [JsonProperty]
        public float? StretchTangentInY { get => GetTangent(13); set => SetTangent(13, value); }
        [JsonProperty]
        public float? StretchTangentInZ { get => GetTangent(14); set => SetTangent(14, value); }
        [JsonProperty]
        public float? StretchTangentOutX { get => GetTangent(15); set => SetTangent(15, value); }
        [JsonProperty]
        public float? StretchTangentOutY { get => GetTangent(16); set => SetTangent(16, value); }
        [JsonProperty]
        public float? StretchTangentOutZ { get => GetTangent(17); set => SetTangent(17, value); }

        // Keep all eighteen tangents in one optional allocation. Explicit null values
        // do not allocate storage, and clearing the last tangent releases it.
        private TangentData tangents;

        [InlineArray(18)]
        private struct TangentValues
        {
            private float value;
        }

        private sealed class TangentData
        {
            public uint Present;
            public TangentValues Values;
        }

        private float? GetValue(float value, uint mask)
        {
            return (flags & mask) != 0 ? value : null;
        }

        private void SetValue(ref float storage, uint mask, float? value)
        {
            storage = value.GetValueOrDefault();
            SetFlag(mask, value.HasValue);
        }

        private void SetFlag(uint mask, bool value)
        {
            flags = value ? flags | mask : flags & ~mask;
        }

        private EnumKeyFrameInterpolation GetInterpolation(int shift)
        {
            return (EnumKeyFrameInterpolation)((flags >> shift) & InterpolationMask);
        }

        private void SetInterpolation(int shift, EnumKeyFrameInterpolation value)
        {
            if ((uint)value > (uint)EnumKeyFrameInterpolation.Step)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported keyframe interpolation mode.");
            }

            flags = (flags & ~(InterpolationMask << shift)) | ((uint)value << shift);
        }

        private float? GetTangent(int index)
        {
            TangentData data = tangents;
            if (data == null || (data.Present & (1u << index)) == 0) return null;

            return data.Values[index];
        }

        private void SetTangent(int index, float? value)
        {
            TangentData data = tangents;
            if (data == null)
            {
                if (!value.HasValue) return;
                tangents = data = new TangentData();
            }

            data.Values[index] = value.GetValueOrDefault();
            uint mask = 1u << index;
            data.Present = value.HasValue ? data.Present | mask : data.Present & ~mask;
            if (data.Present == 0) tangents = null;
        }

        internal int Frame;
        public ShapeElement ForElement;

        public bool AnySet => (flags & ValueMask) != 0;
        public bool PositionSet => (flags & PositionMask) != 0;
        public bool StretchSet => (flags & StretchMask) != 0;
        public bool RotationSet => (flags & RotationMask) != 0;
        public bool OriginSet => (flags & OriginMask) != 0;

        internal bool IsSet(int flag)
        {
            switch (flag)
            {
                case 0: return PositionSet;
                case 1: return RotationSet;
                case 2: return StretchSet;
                case 3: return OriginSet;
            }

            return false;
        }
    }
}
