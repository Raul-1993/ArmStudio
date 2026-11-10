using System;

namespace ArmStudio.Core
{
    /// <summary>
    /// Independent teaching joint coordinates. Limits follow the reference image,
    /// not the mechanics of the future printed robot. Indices are zero based.
    /// </summary>
    public sealed class JointState
    {
        private readonly double[] values;

        public JointState() { values = new double[6]; }

        public JointState(double j1, double j2, double j3, double j4, double j5, double j6)
        {
            values = new[] { j1, j2, j3, j4, j5, j6 };
            for (int i = 0; i < 6; i++) Validate(i, values[i]);
        }

        public double GetAxis(int axis) { CheckAxis(axis); return values[axis]; }
        public JointState Clone() { return new JointState(values[0], values[1], values[2], values[3], values[4], values[5]); }

        public JointState WithAxis(int axis, double value)
        {
            Validate(axis, value);
            var result = Clone(); result.values[axis] = value; return result;
        }

        public static double Min(int axis)
        {
            CheckAxis(axis);
            switch (axis) { case 0: return -165; case 1: return -110; case 2: return -110; case 3: return -160; case 4: return -120; default: return -400; }
        }

        public static double Max(int axis)
        {
            CheckAxis(axis);
            switch (axis) { case 0: return 165; case 1: return 110; case 2: return 70; case 3: return 160; case 4: return 120; default: return 400; }
        }

        private static void CheckAxis(int axis)
        {
            if (axis < 0 || axis > 5) throw new ArgumentOutOfRangeException("axis", "El índice de articulación debe estar entre 0 y 5.");
        }

        private static void Validate(int axis, double value)
        {
            CheckAxis(axis);
            if (Double.IsNaN(value) || Double.IsInfinity(value) || value < Min(axis) || value > Max(axis))
                throw new ArgumentOutOfRangeException("value", "J" + (axis + 1) + " debe estar entre " + Min(axis) + " y " + Max(axis) + " grados.");
        }
    }
}
