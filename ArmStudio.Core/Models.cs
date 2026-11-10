using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace ArmStudio.Core
{
    [DataContract]
    public sealed class RobotPose
    {
        [DataMember(Order = 1)] public double X { get; set; }
        [DataMember(Order = 2)] public double Y { get; set; }
        [DataMember(Order = 3)] public double Z { get; set; }
        [DataMember(Order = 4)] public double A { get; set; }
        [DataMember(Order = 5)] public double B { get; set; }
        [DataMember(Order = 6)] public double C { get; set; }

        public RobotPose() { }
        public RobotPose(double x, double y, double z, double a, double b, double c)
        { X = x; Y = y; Z = z; A = a; B = b; C = c; }

        public RobotPose Clone() { return new RobotPose(X, Y, Z, A, B, C); }

        public RobotPose WithAxis(int axis, double value)
        {
            var result = Clone();
            switch (axis)
            {
                case 0: result.X = value; break;
                case 1: result.Y = value; break;
                case 2: result.Z = value; break;
                case 3: result.A = value; break;
                case 4: result.B = value; break;
                case 5: result.C = value; break;
                default: throw new ArgumentOutOfRangeException("axis", "El eje debe estar entre 0 y 5.");
            }
            return result;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "X {0:0.00}  Y {1:0.00}  Z {2:0.00} mm | A {3:0.0}  B {4:0.0}  C {5:0.0}°", X, Y, Z, A, B, C);
        }
    }

    [DataContract]
    public sealed class RobotPoint
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public RobotPose Pose { get; set; }

        public RobotPoint() { Name = "pNew"; Pose = new RobotPose(); }
        public RobotPoint(string name, RobotPose pose) { Name = name; Pose = pose; }
    }

    public enum MotionKind { MoveL, MoveC, WaitTime }

    public sealed class MotionInstruction
    {
        public MotionKind Kind { get; set; }
        public int LineNumber { get; set; }
        public string Target { get; set; }
        public string Via { get; set; }
        public double Speed { get; set; }
        public string Zone { get; set; }
        public string Tool { get; set; }
        public double Seconds { get; set; }
    }

    public sealed class ProgramDiagnostic
    {
        public int LineNumber { get; set; }
        public string Message { get; set; }
        public ProgramDiagnostic() { }
        public ProgramDiagnostic(int lineNumber, string message) { LineNumber = lineNumber; Message = message; }
        public override string ToString() { return LineNumber > 0 ? "Línea " + LineNumber + ": " + Message : Message; }
    }

    public sealed class ParseResult
    {
        public List<MotionInstruction> Instructions { get; set; } = new List<MotionInstruction>();
        public List<ProgramDiagnostic> Diagnostics { get; set; } = new List<ProgramDiagnostic>();
        public bool Success { get { return Diagnostics.Count == 0 && Instructions.Count > 0; } }
    }

    [DataContract]
    public sealed class RobotProject
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public string Source { get; set; }
        [DataMember(Order = 3)] public List<RobotPoint> Points { get; set; }
        public RobotProject() { Name = "Mi programa"; Source = ""; Points = new List<RobotPoint>(); }
    }

    public static class DemoProject
    {
        public static RobotProject Create()
        {
            return new RobotProject
            {
                Name = "Contorno de práctica",
                Source = "! ARM STUDIO · Práctica de trayectoria\r\n" +
                    "! Posiciones simuladas en mm y grados.\r\n" +
                    "MODULE Practice\r\n" +
                    "    PROC main()\r\n" +
                    "        MoveL pHome, v50, fine, tool0;\r\n" +
                    "        MoveL p1, v50, fine, tool0;\r\n" +
                    "        MoveC pVia, p2, v50, z0, tool0;\r\n" +
                    "        MoveL p3, v50, fine, tool0;\r\n" +
                    "        WaitTime 0.5;\r\n" +
                    "        MoveL pHome, v50, fine, tool0;\r\n" +
                    "    ENDPROC\r\n" +
                    "ENDMODULE\r\n",
                Points = new List<RobotPoint>
                {
                    new RobotPoint("pHome", new RobotPose(0, 0, 200, 0, 0, 0)),
                    new RobotPoint("p1", new RobotPose(120, 0, 200, 0, 0, 0)),
                    new RobotPoint("pVia", new RobotPose(150, 30, 200, 0, 0, 0)),
                    new RobotPoint("p2", new RobotPose(120, 60, 200, 0, 0, 0)),
                    new RobotPoint("p3", new RobotPose(0, 60, 200, 0, 0, 0))
                }
            };
        }
    }
}
