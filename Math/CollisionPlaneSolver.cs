using System;

namespace Vintagestory.API.MathTools
{
    /// <summary>
    /// A half-space constraint used to resolve movement against a collision surface.
    /// </summary>
    public struct CollisionPlane
    {
        /// <summary>The unit normal pointing toward allowed movement.</summary>
        public readonly FastVec3d Normal;
        /// <summary>The minimum displacement required along <see cref="Normal"/>.</summary>
        public readonly double MinimumDistance;
        /// <summary>The maximum correction this plane may contribute.</summary>
        public readonly double PushLimit;
        /// <summary>The correction contributed by this plane during the latest solve.</summary>
        public double Push;

        public CollisionPlane(FastVec3d normal, double minimumDistance, double pushLimit = double.PositiveInfinity)
        {
            double lengthSq = normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z;
            if (!double.IsFinite(lengthSq) || lengthSq <= 0) throw new ArgumentException("A collision plane requires a finite, non-zero normal.", nameof(normal));
            if (!double.IsFinite(minimumDistance)) throw new ArgumentOutOfRangeException(nameof(minimumDistance), "A collision plane requires a finite minimum distance.");
            if (double.IsNaN(pushLimit) || pushLimit < 0) throw new ArgumentOutOfRangeException(nameof(pushLimit), "A collision plane push limit cannot be negative or NaN.");

            double inverseLength = 1 / Math.Sqrt(lengthSq);
            Normal = new FastVec3d(normal.X * inverseLength, normal.Y * inverseLength, normal.Z * inverseLength);
            MinimumDistance = minimumDistance;
            PushLimit = pushLimit;
            Push = 0;
        }

        /// <summary>
        /// Gets the distance beyond this plane. A negative value violates the constraint.
        /// </summary>
        public readonly double GetSeparation(FastVec3d delta)
        {
            return delta.X * Normal.X + delta.Y * Normal.Y + delta.Z * Normal.Z - MinimumDistance;
        }
    }

    public readonly struct CollisionPlaneSolverResult(FastVec3d delta, int iterationCount)
    {
        public readonly FastVec3d Delta = delta;
        public readonly int IterationCount = iterationCount;
    }

    /// <summary>
    /// Resolves a displacement against one or more collision planes.
    /// </summary>
    public static class CollisionPlaneSolver
    {
        public const int DefaultMaxIterations = 20;
        public const double DefaultTolerance = 0.0001;

        public static CollisionPlaneSolverResult Solve(FastVec3d targetDelta, Span<CollisionPlane> planes, double tolerance = DefaultTolerance, int maxIterations = DefaultMaxIterations)
        {
            if (!double.IsFinite(targetDelta.X) || !double.IsFinite(targetDelta.Y) || !double.IsFinite(targetDelta.Z)) throw new ArgumentException("The target displacement must be finite.", nameof(targetDelta));
            if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance), "The solver tolerance must be finite and positive.");
            if (maxIterations <= 0) throw new ArgumentOutOfRangeException(nameof(maxIterations), "The solver requires at least one iteration.");
            if (planes.IsEmpty) return new CollisionPlaneSolverResult(targetDelta, 0);

            for (int i = 0; i < planes.Length; i++)
            {
                planes[i].Push = 0;
            }

            FastVec3d delta = targetDelta;
            int iterationCount = 0;
            for (; iterationCount < maxIterations; iterationCount++)
            {
                double totalPush = 0;
                for (int i = 0; i < planes.Length; i++)
                {
                    ref CollisionPlane plane = ref planes[i];
                    double push = -plane.GetSeparation(delta) - tolerance;
                    double previousPush = plane.Push;
                    plane.Push = Math.Clamp(previousPush + push, 0, plane.PushLimit);
                    push = plane.Push - previousPush;
                    delta.X += push * plane.Normal.X;
                    delta.Y += push * plane.Normal.Y;
                    delta.Z += push * plane.Normal.Z;
                    totalPush += Math.Abs(push);
                }

                if (totalPush < tolerance)
                {
                    iterationCount++;
                    break;
                }
            }

            return new CollisionPlaneSolverResult(delta, iterationCount);
        }

        /// <summary>
        /// Removes components directed into planes which contributed to the latest solve.
        /// </summary>
        public static FastVec3d ClipVector(FastVec3d vector, ReadOnlySpan<CollisionPlane> planes)
        {
            for (int i = 0; i < planes.Length; i++)
            {
                ref readonly CollisionPlane plane = ref planes[i];
                if (plane.Push == 0) continue;

                double intoPlane = vector.X * plane.Normal.X + vector.Y * plane.Normal.Y + vector.Z * plane.Normal.Z;
                if (intoPlane >= 0) continue;

                vector.X -= intoPlane * plane.Normal.X;
                vector.Y -= intoPlane * plane.Normal.Y;
                vector.Z -= intoPlane * plane.Normal.Z;
            }

            return vector;
        }
    }
}
