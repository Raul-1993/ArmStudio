using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArmStudio.Core
{
    /// <summary>A deliberately small RAPID-inspired teaching language, not an ABB RAPID interpreter.</summary>
    public static class ProgramParser
    {
        private static readonly Regex Identifier = new Regex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex Module = new Regex(@"^MODULE\s+[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Procedure = new Regex(@"^PROC\s+main\s*\(\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsValidIdentifier(string name) { return name != null && Identifier.IsMatch(name); }

        public static ParseResult Parse(string source, IEnumerable<RobotPoint> points)
        {
            var result = new ParseResult();
            var table = new Dictionary<string, RobotPoint>(StringComparer.OrdinalIgnoreCase);
            if (points == null)
                result.Diagnostics.Add(new ProgramDiagnostic(0, "No hay una tabla de puntos."));
            else
                foreach (var point in points)
                {
                    if (point == null || !IsValidIdentifier(point.Name))
                    { result.Diagnostics.Add(new ProgramDiagnostic(0, "Cada punto necesita un nombre válido: letras, números y guion bajo; debe empezar por letra o guion bajo.")); continue; }
                    if (table.ContainsKey(point.Name))
                    { result.Diagnostics.Add(new ProgramDiagnostic(0, "El punto '" + point.Name + "' está duplicado.")); continue; }
                    try { MotionGeometry.ValidatePose(point.Pose); }
                    catch (ArgumentException ex) { result.Diagnostics.Add(new ProgramDiagnostic(0, "Punto '" + point.Name + "': " + ex.Message)); }
                    table[point.Name] = point;
                }

            var lines = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool moduleOpen = false, procOpen = false, moduleSeen = false, procSeen = false, moduleClosed = false, procClosed = false;
            int moduleLine = 0, procLine = 0;
            for (int index = 0; index < lines.Length; index++)
            {
                int lineNumber = index + 1;
                string line = lines[index];
                int comment = line.IndexOf('!');
                if (comment >= 0) line = line.Substring(0, comment);
                line = line.Trim();
                if (line.Length == 0) continue;
                if (Module.IsMatch(line))
                {
                    if (moduleSeen || procSeen || result.Instructions.Count > 0)
                        result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "MODULE debe aparecer una sola vez, al comienzo del programa."));
                    moduleSeen = true; moduleOpen = true; moduleLine = lineNumber; continue;
                }
                if (Procedure.IsMatch(line))
                {
                    if (procSeen || moduleClosed || result.Instructions.Count > 0)
                        result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "Solo se admite un procedimiento main(), antes de las instrucciones."));
                    procSeen = true; procOpen = true; procLine = lineNumber; continue;
                }
                if (line.Equals("ENDPROC", StringComparison.OrdinalIgnoreCase))
                {
                    if (!procOpen) result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "ENDPROC sin PROC main()."));
                    procOpen = false; procClosed = true; continue;
                }
                if (line.Equals("ENDMODULE", StringComparison.OrdinalIgnoreCase))
                {
                    if (!moduleOpen) result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "ENDMODULE sin MODULE."));
                    if (procOpen) result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "Falta ENDPROC antes de ENDMODULE."));
                    moduleOpen = false; moduleClosed = true; continue;
                }
                if (moduleClosed || procClosed)
                { result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "La instrucción está fuera del programa o del procedimiento main().")); continue; }

                if (!line.EndsWith(";", StringComparison.Ordinal))
                { result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "Falta ';' al final de la instrucción.")); continue; }
                line = line.Substring(0, line.Length - 1).Trim();
                int separator = line.IndexOfAny(new[] { ' ', '\t' });
                string command = separator < 0 ? line : line.Substring(0, separator);
                string arguments = separator < 0 ? "" : line.Substring(separator + 1).Trim();
                var instruction = new MotionInstruction { LineNumber = lineNumber, Zone = "fine", Tool = "tool0" };
                if (command.Equals("WaitTime", StringComparison.OrdinalIgnoreCase))
                {
                    double seconds;
                    if (!TryNumber(arguments, out seconds) || seconds < 0 || seconds > 3600)
                    { result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "WaitTime necesita segundos entre 0 y 3600, usando punto decimal.")); continue; }
                    instruction.Kind = MotionKind.WaitTime; instruction.Seconds = seconds;
                    result.Instructions.Add(instruction); continue;
                }
                bool circular = command.Equals("MoveC", StringComparison.OrdinalIgnoreCase);
                if (!circular && !command.Equals("MoveL", StringComparison.OrdinalIgnoreCase))
                { result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "Instrucción no admitida: '" + command + "'. Usa MoveL, MoveC o WaitTime.")); continue; }
                var fields = arguments.Split(',').Select(part => part.Trim()).ToArray();
                if (fields.Length != (circular ? 5 : 4))
                {
                    result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, circular ?
                        "Formato: MoveC pIntermedio, pDestino, v50, fine, tool0;" :
                        "Formato: MoveL pDestino, v50, fine, tool0;")); continue;
                }
                int offset = circular ? 1 : 0;
                bool badPoint = false;
                for (int pointIndex = 0; pointIndex <= offset; pointIndex++)
                    if (!table.ContainsKey(fields[pointIndex]))
                    { result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "El punto '" + fields[pointIndex] + "' no existe en la tabla.")); badPoint = true; }
                if (badPoint) continue;
                double speed;
                string speedField = fields[1 + offset];
                if (speedField.Length < 2 || char.ToLowerInvariant(speedField[0]) != 'v' || !TryNumber(speedField.Substring(1), out speed) || speed <= 0 || speed > 500)
                { result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "Velocidad no válida. Usa v seguido de un valor mayor que 0 y hasta 500 mm/s.")); continue; }
                string zone = fields[2 + offset].ToLowerInvariant();
                if (zone != "fine" && zone != "z0")
                { result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "Solo se admiten las zonas fine y z0; ambas detienen el movimiento en cada punto.")); continue; }
                if (!fields[3 + offset].Equals("tool0", StringComparison.OrdinalIgnoreCase))
                { result.Diagnostics.Add(new ProgramDiagnostic(lineNumber, "Solo se admite tool0 en esta versión.")); continue; }
                instruction.Kind = circular ? MotionKind.MoveC : MotionKind.MoveL;
                instruction.Target = table[fields[offset]].Name;
                instruction.Via = circular ? table[fields[0]].Name : null;
                instruction.Speed = speed; instruction.Zone = zone;
                result.Instructions.Add(instruction);
            }
            if (procOpen) result.Diagnostics.Add(new ProgramDiagnostic(procLine, "PROC main() no tiene ENDPROC."));
            if (moduleOpen) result.Diagnostics.Add(new ProgramDiagnostic(moduleLine, "MODULE no tiene ENDMODULE."));
            if (result.Instructions.Count == 0 && result.Diagnostics.Count == 0)
                result.Diagnostics.Add(new ProgramDiagnostic(0, "El programa no contiene instrucciones."));
            return result;
        }

        private static bool TryNumber(string value, out double number)
        {
            return double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number);
        }
    }
}
