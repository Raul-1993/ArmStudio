using System;
using System.Collections.Generic;

namespace ArmStudio.Core
{
    /// <summary>
    /// Numeric Cartesian interpolation for the training controller. Samples exclude
    /// the start pose and include the exact destination. ABC are training Euler
    /// values interpolated numerically, not a robot-specific orientation solver.
    /// </summary>
    public static class MotionGeometry
    {
        private const double Epsilon = 1e-9;
        private const double TwoPi = 2.0 * Math.PI;

        public static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        public static void ValidatePose(RobotPose pose)
        {
            if (pose == null) throw new ArgumentException("La posición no puede ser nula.", "pose");
            double[] values = { pose.X, pose.Y, pose.Z, pose.A, pose.B, pose.C };
            for (int i = 0; i < values.Length; i++)
            {
                double limit = i < 3 ? 500.0 : 180.0;
                if (!IsFinite(values[i]) || Math.Abs(values[i]) > limit)
                    throw new ArgumentException(i < 3
                        ? "Las coordenadas XYZ deben estar entre -500 y 500 mm."
                        : "Los valores ABC deben estar entre -180 y 180 grados.", "pose");
            }
        }

        public static List<RobotPose> SampleLinear(RobotPose start, RobotPose end, int segments)
        {
            ValidatePose(start);
            ValidatePose(end);
            ValidateSegments(segments, 1);
            var result = new List<RobotPose>(segments);
            for (int i = 1; i <= segments; i++)
                result.Add(i == segments ? end.Clone() : Lerp(start, end, (double)i / segments));
            return result;
        }

        /// <summary>
        /// Samples the unique 3D circle from start through via to end. At least two
        /// segments are required; a sample is allocated to each side of via, so
        /// the exact via pose is always present, even for a major arc.
        /// </summary>
        public static List<RobotPose> SampleCircular(RobotPose start, RobotPose via, RobotPose end, int segments)
        {
            ValidatePose(start);
            ValidatePose(via);
            ValidatePose(end);
            ValidateSegments(segments, 2);

            Vec s = Position(start), m = Position(via), e = Position(end);
            Vec u = m - s, v = e - s;
            double uLength = u.Length, vLength = v.Length;
            if (uLength < Epsilon || vLength < Epsilon || (e - m).Length < Epsilon)
                throw new ArgumentException("MoveC necesita tres posiciones XYZ diferentes.");
            Vec cross = Vec.Cross(u, v);
            double crossLength = cross.Length;
            if (crossLength / (uLength * vLength) < 1e-8)
                throw new ArgumentException("MoveC no admite puntos alineados o casi alineados.");

            Vec normal = cross / crossLength;
            Vec planeX = u / uLength;
            Vec planeY = Vec.Cross(normal, planeX);
            double vx = Vec.Dot(v, planeX), vy = Vec.Dot(v, planeY);
            double cx = uLength * 0.5;
            double cy = (vLength * vLength - 2.0 * cx * vx) / (2.0 * vy);
            Vec center = s + planeX * cx + planeY * cy;
            double radius = (s - center).Length;
            if (!IsFinite(radius) || radius < Epsilon)
                throw new ArgumentException("No se puede construir el arco MoveC.");

            Vec radialX = (s - center) / radius;
            Vec radialY = Vec.Cross(normal, radialX);
            double viaAngle = PositiveAngle(m - center, radialX, radialY);
            double endAngle = PositiveAngle(e - center, radialX, radialY);
            if (viaAngle > endAngle)
            {
                radialY = radialY * -1.0;
                viaAngle = PositiveAngle(m - center, radialX, radialY);
                endAngle = PositiveAngle(e - center, radialX, radialY);
            }
            if (viaAngle < Epsilon || endAngle - viaAngle < Epsilon)
                throw new ArgumentException("El arco MoveC es demasiado pequeño o inestable.");

            // Check continuous XYZ extrema as well as samples. A coarse sampling
            // must not silently skip an excursion outside the simulation bounds.
            ValidateArcBounds(center, radialX, radialY, radius, endAngle);

            int beforeVia = Math.Max(1, Math.Min(segments - 1,
                (int)Math.Round(segments * viaAngle / endAngle)));
            int afterVia = segments - beforeVia;
            var result = new List<RobotPose>(segments);
            for (int i = 1; i <= beforeVia; i++)
            {
                if (i == beforeVia) result.Add(via.Clone());
                else result.Add(ArcPose(center, radialX, radialY, radius,
                    viaAngle * i / beforeVia, start, via, (double)i / beforeVia));
            }
            for (int i = 1; i <= afterVia; i++)
            {
                if (i == afterVia) result.Add(end.Clone());
                else result.Add(ArcPose(center, radialX, radialY, radius,
                    viaAngle + (endAngle - viaAngle) * i / afterVia,
                    via, end, (double)i / afterVia));
            }
            return result;
        }

        private static RobotPose ArcPose(Vec center, Vec x, Vec y, double radius,
            double angle, RobotPose from, RobotPose to, double t)
        {
            Vec p = center + (x * Math.Cos(angle) + y * Math.Sin(angle)) * radius;
            var pose = new RobotPose(ClampRoundoff(p.X), ClampRoundoff(p.Y), ClampRoundoff(p.Z),
                Mix(from.A, to.A, t), Mix(from.B, to.B, t), Mix(from.C, to.C, t));
            ValidatePose(pose);
            return pose;
        }

        private static double ClampRoundoff(double value)
        {
            if (value > 500.0 && value < 500.0 + 1e-7) return 500.0;
            if (value < -500.0 && value > -500.0 - 1e-7) return -500.0;
            return value;
        }

        private static void ValidateArcBounds(Vec c, Vec x, Vec y, double radius, double endAngle)
        {
            double[] centers = { c.X, c.Y, c.Z };
            double[] xs = { x.X, x.Y, x.Z }, ys = { y.X, y.Y, y.Z };
            for (int axis = 0; axis < 3; axis++)
            {
                double stationary = Math.Atan2(ys[axis], xs[axis]);
                for (int opposite = 0; opposite < 2; opposite++)
                {
                    double angle = NormalizeAngle(stationary + opposite * Math.PI);
                    if (angle <= endAngle + Epsilon)
                    {
                        double value = centers[axis] + radius *
                            (xs[axis] * Math.Cos(angle) + ys[axis] * Math.Sin(angle));
                        if (!IsFinite(value) || Math.Abs(value) > 500.0 + 1e-7)
                            throw new ArgumentException("El arco MoveC sale de los límites de simulación XYZ (±500 mm).");
                    }
                }
            }
        }

        private static double PositiveAngle(Vec value, Vec x, Vec y)
        {
            return NormalizeAngle(Math.Atan2(Vec.Dot(value, y), Vec.Dot(value, x)));
        }

        private static double NormalizeAngle(double angle)
        {
            angle %= TwoPi;
            return angle < 0.0 ? angle + TwoPi : angle;
        }

        private static void ValidateSegments(int segments, int minimum)
        {
            if (segments < minimum || segments > 100000)
                throw new ArgumentOutOfRangeException("segments", "Número de muestras fuera del intervalo permitido.");
        }

        private static RobotPose Lerp(RobotPose a, RobotPose b, double t)
        {
            return new RobotPose(Mix(a.X, b.X, t), Mix(a.Y, b.Y, t), Mix(a.Z, b.Z, t),
                Mix(a.A, b.A, t), Mix(a.B, b.B, t), Mix(a.C, b.C, t));
        }

        private static double Mix(double a, double b, double t) { return a + (b - a) * t; }
        private static Vec Position(RobotPose pose) { return new Vec(pose.X, pose.Y, pose.Z); }

        private struct Vec
        {
            internal double X, Y, Z;
            internal Vec(double x, double y, double z) { X = x; Y = y; Z = z; }
            internal double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
            public static Vec operator +(Vec a, Vec b) { return new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
            public static Vec operator -(Vec a, Vec b) { return new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
            public static Vec operator *(Vec a, double k) { return new Vec(a.X * k, a.Y * k, a.Z * k); }
            public static Vec operator /(Vec a, double k) { return new Vec(a.X / k, a.Y / k, a.Z / k); }
            internal static double Dot(Vec a, Vec b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
            internal static Vec Cross(Vec a, Vec b)
            {
                return new Vec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            }
        }
    }
}
