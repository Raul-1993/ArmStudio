using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ArmStudio.Core
{
    public static class ProjectStore
    {
        private const long MaxFileBytes = 2 * 1024 * 1024;

        /// <summary>Stores source and point table together; syntax errors may be saved as work in progress.</summary>
        public static void Save(string path, RobotProject project)
        {
            Validate(project);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Selecciona un archivo de proyecto.", "path");
            byte[] data;
            var serializer = new DataContractJsonSerializer(typeof(RobotProject));
            using (var buffer = new MemoryStream())
            { serializer.WriteObject(buffer, project); data = buffer.ToArray(); }
            if (data.LongLength > MaxFileBytes) throw new InvalidDataException("El proyecto supera el límite de 2 MB.");

            // Write next to the destination, then replace it, so a failed serialization never destroys a project.
            string fullPath = Path.GetFullPath(path);
            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(data, 0, data.Length); stream.Flush(true); }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static RobotProject Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Selecciona un archivo de proyecto.", "path");
            RobotProject project;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxFileBytes) throw new InvalidDataException("El proyecto supera el límite de 2 MB.");
                try { project = (RobotProject)new DataContractJsonSerializer(typeof(RobotProject)).ReadObject(stream); }
                catch (SerializationException ex) { throw new InvalidDataException("El archivo no es un proyecto JSON válido de Arm Studio.", ex); }
            }
            Validate(project);
            return project;
        }

        private static void Validate(RobotProject project)
        {
            if (project == null) throw new InvalidDataException("El proyecto está vacío.");
            if (string.IsNullOrWhiteSpace(project.Name) || project.Name.Length > 120)
                throw new InvalidDataException("El nombre de proyecto debe tener entre 1 y 120 caracteres.");
            if (project.Source == null || project.Source.Length > 500000)
                throw new InvalidDataException("El texto del programa está vacío o supera los 500 000 caracteres.");
            if (project.Points == null || project.Points.Count > 1000)
                throw new InvalidDataException("La tabla de puntos está ausente o supera los 1000 puntos.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var point in project.Points)
            {
                if (point == null || !ProgramParser.IsValidIdentifier(point.Name))
                    throw new InvalidDataException("La tabla contiene un punto sin nombre válido.");
                if (!names.Add(point.Name)) throw new InvalidDataException("El punto '" + point.Name + "' está duplicado.");
                try { MotionGeometry.ValidatePose(point.Pose); }
                catch (ArgumentException ex) { throw new InvalidDataException("Punto '" + point.Name + "': " + ex.Message, ex); }
            }
        }
    }
}
