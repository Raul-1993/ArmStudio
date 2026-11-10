using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArmStudio.Core;

namespace ArmStudio.Tests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;
        private static readonly RobotPose Home = new RobotPose(0, 0, 200, 0, 0, 0);

        private static int Main()
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            Console.WriteLine("ArmStudio — verificación funcional (cultura es-ES)");
            try { RunAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { failed++; Console.WriteLine("ERROR FATAL: " + ex); }
            Console.WriteLine("Resultado: " + passed + " correctas; " + failed + " fallidas.");
            return failed == 0 ? 0 : 1;
        }

        private static async Task RunAsync()
        {
            passed += await JointChecks.RunAsync();
            Test("El ejemplo completo se valida", () =>
            {
                var demo = DemoProject.Create();
                var parsed = ProgramParser.Parse(demo.Source, demo.Points);
                Require(parsed.Success, Diagnostics(parsed));
                Require(parsed.Instructions.Count >= 3, "El ejemplo debe contener movimientos.");
            });
            Test("MoveL, MoveC y WaitTime conservan argumentos y líneas", () =>
            {
                var result = Parse("! cabecera\nMoveL p1, v50, fine, tool0;\nMoveC pVia, p2, v25, z0, tool0;\nWaitTime 0.25;");
                Require(result.Success, Diagnostics(result));
                Equal(3, result.Instructions.Count);
                Equal(MotionKind.MoveL, result.Instructions[0].Kind);
                Equal(2, result.Instructions[0].LineNumber);
                Equal("p1", result.Instructions[0].Target);
                Near(50, result.Instructions[0].Speed);
                Equal(MotionKind.MoveC, result.Instructions[1].Kind);
                Equal("pVia", result.Instructions[1].Via);
                Equal("p2", result.Instructions[1].Target);
                Near(0.25, result.Instructions[2].Seconds);
            });
            Test("Comentarios, mayúsculas y envoltorio básico", () =>
            {
                var result = Parse("MODULE Practice\nPROC main()\n  movel P1, v50, fine, tool0; ! comentario\nENDPROC\nENDMODULE");
                Require(result.Success, Diagnostics(result));
                Equal(1, result.Instructions.Count);
                Equal(3, result.Instructions[0].LineNumber);
            });
            Test("Un punto inexistente informa su línea", () =>
            {
                var result = Parse("! nota\nMoveL perdido, v50, fine, tool0;");
                Require(!result.Success, "Un destino desconocido se aceptó.");
                Require(result.Diagnostics.Any(d => d.LineNumber == 2), "Falta la línea del error.");
            });
            Test("El subconjunto no acepta bucles RAPID sin implementar", () =>
                Require(!Parse("WHILE cnt < 3 DO\nENDWHILE").Success, "Se aceptó sintaxis sin implementar."));
            Test("Se exige punto y coma", () =>
                Require(!Parse("MoveL p1, v50, fine, tool0").Success, "Se aceptó una instrucción incompleta."));
            Test("Velocidades cero, negativas, excesivas y NaN se rechazan", () =>
            {
                foreach (var speed in new[] { "v0", "v-1", "v501", "vNaN", "vInfinity" })
                    Require(!Parse("MoveL p1, " + speed + ", fine, tool0;").Success, "Se aceptó " + speed);
            });
            Test("Solo se aceptan las zonas y herramienta documentadas", () =>
            {
                Require(!Parse("MoveL p1, v50, z50, tool0;").Success, "Se aceptó blending sin implementar.");
                Require(!Parse("MoveL p1, v50, fine, pinza;").Success, "Se aceptó una herramienta sin definir.");
            });
            Test("MoveC necesita punto intermedio y final", () =>
                Require(!Parse("MoveC pVia, v50, fine, tool0;").Success, "Se aceptó un arco incompleto."));
            Test("Puntos duplicados no distinguen mayúsculas", () =>
            {
                var points = Points();
                points.Add(new RobotPoint("P1", Home.Clone()));
                Require(!ProgramParser.Parse("MoveL p1, v50, fine, tool0;", points).Success, "Se aceptaron nombres ambiguos.");
            });
            Test("WithAxis devuelve una copia sin alterar la posición original", () =>
            {
                var pose = Home.Clone();
                var changed = pose.WithAxis(0, 125);
                Near(0, pose.X);
                Near(125, changed.X);
                Near(200, changed.Z);
            });
            Test("La interpolación lineal termina exactamente en el destino", () =>
            {
                var end = new RobotPose(100, -20, 250, 30, -45, 90);
                var samples = MotionGeometry.SampleLinear(Home, end, 10);
                Equal(10, samples.Count);
                Near(10, samples[0].X);
                PoseNear(end, samples[samples.Count - 1]);
                Near(0, Home.X);
            });
            Test("MoveC recorre un círculo real e incluye el punto intermedio", () =>
            {
                var start = new RobotPose(1, 0, 200, 0, 0, 0);
                var via = new RobotPose(0, 1, 200, 0, 0, 0);
                var end = new RobotPose(-1, 0, 200, 0, 0, 0);
                var samples = MotionGeometry.SampleCircular(start, via, end, 24);
                Require(samples.Count >= 2, "Faltan muestras.");
                foreach (var sample in samples)
                {
                    Near(1, Math.Sqrt(sample.X * sample.X + sample.Y * sample.Y), 1e-6);
                    Near(200, sample.Z);
                    Require(sample.Y >= -1e-6, "Se recorrió el semicírculo incorrecto.");
                }
                Require(samples.Any(p => Math.Abs(p.X) < 1e-6 && Math.Abs(p.Y - 1) < 1e-6), "El arco no pasa por el punto intermedio.");
                PoseNear(end, samples[samples.Count - 1]);
            });
            Test("El arco puede superar 180 grados siguiendo el punto intermedio", () =>
            {
                var samples = MotionGeometry.SampleCircular(
                    new RobotPose(1, 0, 200, 0, 0, 0),
                    new RobotPose(0, -1, 200, 0, 0, 0),
                    new RobotPose(0, 1, 200, 0, 0, 0), 32);
                Require(samples.Any(p => p.Y < -0.9), "No pasó por el intermedio.");
                Require(samples.Any(p => p.X < -0.9), "Tomó el arco corto incorrecto.");
                Near(1, samples.Last().Y);
            });
            Test("Arcos colineales y puntos coincidentes se rechazan", () =>
            {
                Throws<ArgumentException>(() => MotionGeometry.SampleCircular(
                    Home, new RobotPose(1, 0, 200, 0, 0, 0), new RobotPose(2, 0, 200, 0, 0, 0), 10));
                Throws<ArgumentException>(() => MotionGeometry.SampleCircular(Home, Home.Clone(), Home.Clone(), 10));
            });
            Test("Los límites se comprueban también entre las muestras de un arco", () =>
            {
                Throws<ArgumentException>(() => MotionGeometry.SampleCircular(
                    new RobotPose(490, 20, 200, 0, 0, 0),
                    new RobotPose(470, 0, 200, 0, 0, 0),
                    new RobotPose(480, Math.Sqrt(300), 200, 0, 0, 0), 2));
            });
            Test("Los límites numéricos rechazan NaN, infinito y posiciones fuera de rango", () =>
            {
                foreach (var invalid in new[] {
                    new RobotPose(double.NaN, 0, 200, 0, 0, 0),
                    new RobotPose(0, double.PositiveInfinity, 200, 0, 0, 0),
                    new RobotPose(501, 0, 200, 0, 0, 0),
                    new RobotPose(0, 0, 200, 181, 0, 0) })
                    Throws<ArgumentException>(() => MotionGeometry.ValidatePose(invalid));
                MotionGeometry.ValidatePose(new RobotPose(-500, 500, 0, -180, 180, 0));
            });
            Test("Guardar y cargar mantiene programa y coordenadas decimales", () => WithTempPath(path =>
            {
                var project = DemoProject.Create();
                project.Name = "Práctica — arco";
                project.Points[0].Pose.X = 12.375;
                ProjectStore.Save(path, project);
                var loaded = ProjectStore.Load(path);
                Equal(project.Name, loaded.Name);
                Equal(project.Source, loaded.Source);
                Equal(project.Points.Count, loaded.Points.Count);
                Near(12.375, loaded.Points[0].Pose.X);
                loaded.Points[0].Pose.X = 0;
                Near(12.375, project.Points[0].Pose.X);
            }));
            Test("Un archivo JSON dañado se rechaza", () => WithTempPath(path =>
            {
                File.WriteAllText(path, "{ esto no es JSON }");
                ThrowsAny(() => ProjectStore.Load(path));
            }));
            Test("No se guarda un proyecto con nombres duplicados o posiciones inválidas", () => WithTempPath(path =>
            {
                var project = DemoProject.Create();
                project.Points.Add(new RobotPoint(project.Points[0].Name, Home.Clone()));
                ThrowsAny(() => ProjectStore.Save(path, project));
                project = DemoProject.Create();
                project.Points[0].Pose.Z = double.NaN;
                ThrowsAny(() => ProjectStore.Save(path, project));
            }));
            Test("Cargar vuelve a validar puntos aunque el JSON sea estructuralmente válido", () => WithTempPath(path =>
            {
                ProjectStore.Save(path, DemoProject.Create());
                var original = File.ReadAllText(path);
                var duplicate = original.Replace("\"Name\":\"p1\"", "\"Name\":\"pHome\"");
                Require(duplicate != original, "No se modificó el punto de prueba.");
                File.WriteAllText(path, duplicate);
                ThrowsAny(() => ProjectStore.Load(path));
                var outOfBounds = original.Replace("\"X\":0", "\"X\":501");
                Require(outOfBounds != original, "No se modificó la coordenada de prueba.");
                File.WriteAllText(path, outOfBounds);
                ThrowsAny(() => ProjectStore.Load(path));
            }));
            await TestAsync("El simulador conecta sin habilitar motores", async () =>
            {
                using (var sim = await ConnectedSimulation())
                {
                    Require(sim.IsConnected && sim.CanMove, "No conectó el simulador.");
                    PoseNear(Home, sim.CurrentPose);
                    await ThrowsAsync<InvalidOperationException>(() => sim.ExecuteAsync("MOVE_L 1 0 200 0 0 0 100", CancellationToken.None));
                }
            });
            await TestAsync("MoveL simulado publica posición y llega al destino", async () =>
            {
                using (var sim = await ConnectedSimulation())
                {
                    int updates = 0;
                    sim.PoseChanged += pose => Interlocked.Increment(ref updates);
                    await sim.ExecuteAsync("ENABLE 1", CancellationToken.None);
                    await sim.ExecuteAsync("MOVE_L 2.5 0 200 0 0 0 500", CancellationToken.None);
                    Near(2.5, sim.CurrentPose.X);
                    Require(updates > 0, "No se publicó telemetría simulada.");
                }
            });
            await TestAsync("MoveC simulado llega al extremo de un arco válido", async () =>
            {
                using (var sim = await ConnectedSimulation())
                {
                    await sim.ExecuteAsync("ENABLE 1", CancellationToken.None);
                    await sim.ExecuteAsync("MOVE_L 1 0 200 0 0 0 500", CancellationToken.None);
                    await sim.ExecuteAsync("MOVE_C 0 1 200 0 0 0 -1 0 200 0 0 0 500", CancellationToken.None);
                    PoseNear(new RobotPose(-1, 0, 200, 0, 0, 0), sim.CurrentPose);
                }
            });
            await TestAsync("Órdenes no finitas, fuera de límites o desconocidas se rechazan", async () =>
            {
                using (var sim = await ConnectedSimulation())
                {
                    await sim.ExecuteAsync("ENABLE 1", CancellationToken.None);
                    foreach (var command in new[] {
                        "MOVE_L NaN 0 200 0 0 0 50", "MOVE_L 501 0 200 0 0 0 50",
                        "MOVE_L 0 0 200 0 0 0 0", "WAIT -1", "BOGUS" })
                        await ThrowsAnyAsync(() => sim.ExecuteAsync(command, CancellationToken.None));
                }
            });
            await TestAsync("Cancelar una orden detiene la ejecución y deshabilita motores", async () =>
            {
                using (var sim = await ConnectedSimulation())
                using (var cancel = new CancellationTokenSource())
                {
                    await sim.ExecuteAsync("ENABLE 1", CancellationToken.None);
                    var moving = sim.ExecuteAsync("MOVE_L 100 0 200 0 0 0 5", cancel.Token);
                    await Task.Delay(50);
                    cancel.Cancel();
                    await ThrowsAsync<OperationCanceledException>(() => moving);
                    await ThrowsAsync<InvalidOperationException>(() => sim.ExecuteAsync("MOVE_L 0 0 200 0 0 0 50", CancellationToken.None));
                    var stoppedX = sim.CurrentPose.X;
                    await Task.Delay(75);
                    Near(stoppedX, sim.CurrentPose.X);
                }
            });
            await TestAsync("Stop cancela el movimiento y exige habilitar de nuevo", async () =>
            {
                using (var sim = await ConnectedSimulation())
                {
                    await sim.ExecuteAsync("ENABLE 1", CancellationToken.None);
                    var moving = sim.ExecuteAsync("MOVE_L 100 0 200 0 0 0 5", CancellationToken.None);
                    await Task.Delay(50);
                    await sim.StopAsync();
                    await ThrowsAsync<OperationCanceledException>(() => moving);
                    await ThrowsAsync<InvalidOperationException>(() => sim.ExecuteAsync("MOVE_L 0 0 200 0 0 0 50", CancellationToken.None));
                }
            });
            await TestAsync("El simulador impide dos movimientos simultáneos", async () =>
            {
                using (var sim = await ConnectedSimulation())
                {
                    await sim.ExecuteAsync("ENABLE 1", CancellationToken.None);
                    var moving = sim.ExecuteAsync("MOVE_L 100 0 200 0 0 0 5", CancellationToken.None);
                    await Task.Delay(30);
                    await ThrowsAsync<InvalidOperationException>(() => sim.ExecuteAsync("MOVE_L 0 0 200 0 0 0 50", CancellationToken.None));
                    await sim.StopAsync();
                    await ThrowsAsync<OperationCanceledException>(() => moving);
                }
            });
            await TestAsync("Reconectar restablece posición y motores deshabilitados", async () =>
            {
                using (var sim = await ConnectedSimulation())
                {
                    await sim.ExecuteAsync("ENABLE 1", CancellationToken.None);
                    await sim.ExecuteAsync("MOVE_L 1 0 200 0 0 0 500", CancellationToken.None);
                    await sim.DisconnectAsync();
                    Require(!sim.IsConnected, "La conexión sigue activa.");
                    await sim.ConnectAsync("", 115200, CancellationToken.None);
                    PoseNear(Home, sim.CurrentPose);
                    await ThrowsAsync<InvalidOperationException>(() => sim.ExecuteAsync("MOVE_L 1 0 200 0 0 0 50", CancellationToken.None));
                }
            });
        }

        private static ParseResult Parse(string source) { return ProgramParser.Parse(source, Points()); }
        private static List<RobotPoint> Points()
        {
            return new List<RobotPoint> {
                new RobotPoint("pHome", Home.Clone()),
                new RobotPoint("p1", new RobotPose(1, 0, 200, 0, 0, 0)),
                new RobotPoint("pVia", new RobotPose(0, 1, 200, 0, 0, 0)),
                new RobotPoint("p2", new RobotPose(-1, 0, 200, 0, 0, 0)) };
        }
        private static string Diagnostics(ParseResult result)
        { return string.Join("; ", result.Diagnostics.Select(d => d.LineNumber + ": " + d.Message)); }
        private static async Task<SimulationTransport> ConnectedSimulation()
        {
            var sim = new SimulationTransport();
            await sim.ConnectAsync("", 115200, CancellationToken.None);
            return sim;
        }
        private static void Test(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("OK   " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + " — " + ex.Message); }
        }
        private static async Task TestAsync(string name, Func<Task> test)
        {
            try
            {
                var task = test();
                if (await Task.WhenAny(task, Task.Delay(6000)) != task) throw new TimeoutException("La prueba superó 6 segundos.");
                await task;
                passed++;
                Console.WriteLine("OK   " + name);
            }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + " — " + ex.Message); }
        }
        private static void WithTempPath(Action<string> action)
        {
            var dir = Path.Combine(Path.GetTempPath(), "armstudio-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try { action(Path.Combine(dir, "test.armstudio.json")); }
            finally { Directory.Delete(dir, true); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual)
        { Require(EqualityComparer<T>.Default.Equals(expected, actual), "Esperado " + expected + "; recibido " + actual); }
        private static void Near(double expected, double actual, double tolerance = 1e-8)
        { Require(!double.IsNaN(actual) && Math.Abs(expected - actual) <= tolerance, "Esperado " + expected + "; recibido " + actual); }
        private static void PoseNear(RobotPose expected, RobotPose actual)
        { Near(expected.X, actual.X); Near(expected.Y, actual.Y); Near(expected.Z, actual.Z); Near(expected.A, actual.A); Near(expected.B, actual.B); Near(expected.C, actual.C); }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Se esperaba " + typeof(T).Name);
        }
        private static void ThrowsAny(Action action)
        {
            try { action(); } catch { return; }
            throw new Exception("Se esperaba rechazar la operación.");
        }
        private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
        {
            try { await action(); } catch (T) { return; }
            throw new Exception("Se esperaba " + typeof(T).Name);
        }
        private static async Task ThrowsAnyAsync(Func<Task> action)
        {
            try { await action(); } catch { return; }
            throw new Exception("Se esperaba rechazar la operación.");
        }
    }
}
