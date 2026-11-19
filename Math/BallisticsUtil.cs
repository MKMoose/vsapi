using System;

namespace Vintagestory.API.MathTools;

public static class BallisticsUtil
{
    /// <summary>
    /// Based on a solution from "AI for Games" (3rd edition) by Ian Millington. Ignores air resistance.
    /// </summary>
    public static bool TrySolve(Vec3d origin, Vec3d target, double speed, Vec3d gravity, out Vec3d aimDirection)
    {
        Vec3d delta = target - origin;

        // Calculate the real-valued a,b,c coefficients of a conventional quadratic equation
        double a = gravity.LengthSq();
        double b = -4 * (gravity.Dot(delta) + speed * speed);
        double c = 4 * delta.LengthSq();

        // Check for no real solutions
        double b2minus4ac = b * b - 4 * a * c;
        if (b2minus4ac < 0)
        {
            aimDirection = Vec3d.Zero;
            return false;
        }

        // Find the candidate squared times for the long and short trajectories
        double rootLong = (-b + Math.Sqrt(b2minus4ac)) / (2 * a);
        double rootShort = (-b - Math.Sqrt(b2minus4ac)) / (2 * a);

        // Discard invalid roots (negative time squared) before taking the square root to avoid NaN
        double timeLong = rootLong >= 0 ? Math.Sqrt(rootLong) : -1;
        double timeShort = rootShort >= 0 ? Math.Sqrt(rootShort) : -1;

        // Find time to target (ttt)
        double ttt;
        if (timeLong < 0)
        {
            if (timeShort < 0)
            {
                aimDirection = Vec3d.Zero;
                return false;
            }

            ttt = timeShort;
        }
        else
        {
            ttt = timeShort < 0 ? timeLong : Math.Min(timeLong, timeShort);
        }

        aimDirection = (delta * 2 - gravity * (ttt * ttt)) / (2 * speed * ttt);
        return true;
    }
}
