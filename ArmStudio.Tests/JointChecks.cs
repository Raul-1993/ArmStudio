using System;
using System.Threading;
using System.Threading.Tasks;
using ArmStudio.Core;

namespace ArmStudio.Tests
{
    /// <summary>Independent checks for the teaching joint-coordinate practice.</summary>
    public static class JointChecks
    {
        public static async Task<int> RunAsync()
        {
            int count = 0;
            var zero = new JointState();
            var changed = zero.WithAxis(5, 400);
            Require(zero.GetAxis(5) == 0 && changed.GetAxis(5) == 400, "Los estados articulares se copian sin compartir su matriz."); count++;
            Require(JointState.Min(2) == -110 && JointState.Max(2) == 70 && JointState.Min(5) == -400 && JointState.Max(5) == 400,
                "J3 conserva sus límites asimétricos y J6 su giro de ±400°."); count++;
            RejectSync(() => zero.WithAxis(0, Double.NaN)); RejectSync(() => zero.WithAxis(0, Double.PositiveInfinity));
            RejectSync(() => zero.WithAxis(6, 0)); RejectSync(() => zero.WithAxis(2, 70.001)); count++;

            using (var simulator = new SimulationTransport())
            {
                await simulator.ConnectAsync("", 115200, CancellationToken.None);
                Require(simulator.CartesianPoseKnown && !simulator.JointsKnown, "Conectar no inventa ángulos articulares a partir del TCP."); count++;
                await RejectAsync(() => simulator.ExecuteAsync("RESET_JOINTS", CancellationToken.None)); count++;
                await simulator.ExecuteAsync("ENABLE 1", CancellationToken.None);
                await RejectAsync(() => simulator.ExecuteAsync("JOG_J 1 1 30", CancellationToken.None)); count++;
                await simulator.ExecuteAsync("RESET_JOINTS", CancellationToken.None);
                Require(simulator.JointsKnown && !simulator.CartesianPoseKnown && simulator.CurrentJoints.GetAxis(0) == 0,
                    "Iniciar práctica de ejes establece una referencia independiente y no un TCP ficticio."); count++;
                var oldCartesian = simulator.CurrentPose;
                int updates = 0;
                simulator.JointsChanged += state => { Interlocked.Increment(ref updates); state.WithAxis(0, 100); };
                await simulator.ExecuteAsync("JOG_J 1 1.5 180", CancellationToken.None);
                Require(Math.Abs(simulator.CurrentJoints.GetAxis(0) - 1.5) < 1e-9 && updates >= 3,
                    "JOG_J interpola el eje seleccionado y publica sus valores."); count++;
                Require(simulator.CurrentPose.X == oldCartesian.X && simulator.CurrentPose.Z == oldCartesian.Z && !simulator.CartesianPoseKnown,
                    "Mover J1 no fabrica cinemática ni altera las coordenadas TCP conservadas."); count++;
                await RejectAsync(() => simulator.ExecuteAsync("MOVE_L 0 0 200 0 0 0 50", CancellationToken.None)); count++;
                await RejectAsync(() => simulator.ExecuteAsync("JOG_J 3 70.1 180", CancellationToken.None));
                Require(simulator.CurrentJoints.GetAxis(2) == 0, "Se rechaza todo el incremento fuera de límites antes de mover."); count++;
                await simulator.ExecuteAsync("JOG_J 6 400 180", CancellationToken.None);
                Require(simulator.CurrentJoints.GetAxis(5) == 400, "J6 admite 400° sin aplicar el límite de orientación ABC de180°."); count++;
                await RejectAsync(() => simulator.ExecuteAsync("JOG_J 6 0.01 180", CancellationToken.None)); count++;
                await RejectAsync(() => simulator.ExecuteAsync("JOG_J 0 1 30", CancellationToken.None));
                await RejectAsync(() => simulator.ExecuteAsync("JOG_J 1 NaN 30", CancellationToken.None));
                await RejectAsync(() => simulator.ExecuteAsync("JOG_J 1 1 181", CancellationToken.None)); count++;
                var motion = simulator.ExecuteAsync("JOG_J 1 100 1", CancellationToken.None);
                await Task.Delay(30);
                await RejectAsync(() => simulator.ExecuteAsync("JOG_J 2 1 30", CancellationToken.None)); count++;
                await simulator.StopAsync();
                await CancelledAsync(motion);
                double stoppedAt = simulator.CurrentJoints.GetAxis(0);
                await Task.Delay(40);
                Require(simulator.CurrentJoints.GetAxis(0) == stoppedAt, "La parada cancela y mantiene estable el ángulo articular."); count++;
                await RejectAsync(() => simulator.ExecuteAsync("JOG_J 1 1 30", CancellationToken.None)); count++;
                await simulator.ExecuteAsync("ENABLE 1", CancellationToken.None);
                using (var cancellation = new CancellationTokenSource())
                {
                    var cancelledMotion = simulator.ExecuteAsync("JOG_J 2 100 1", cancellation.Token);
                    cancellation.Cancel();
                    await CancelledAsync(cancelledMotion);
                    await RejectAsync(() => simulator.ExecuteAsync("JOG_J 2 1 30", CancellationToken.None)); count++;
                }
                await simulator.ExecuteAsync("ENABLE 1", CancellationToken.None);
                await simulator.ExecuteAsync("RESET_CARTESIAN", CancellationToken.None);
                Require(!simulator.JointsKnown && simulator.CartesianPoseKnown && simulator.CurrentPose.Z == 200,
                    "Reiniciar la práctica cartesiana invalida los ángulos independientes."); count++;
                await simulator.ExecuteAsync("MOVE_L 1 0 200 0 0 0 500", CancellationToken.None);
                Require(!simulator.JointsKnown && simulator.CartesianPoseKnown, "MoveL no presenta ángulos articulares sin cinemática inversa."); count++;
                await simulator.DisconnectAsync();
                Require(!simulator.JointsKnown && !simulator.CartesianPoseKnown, "Desconectar invalida la disponibilidad de ambas coordenadas."); count++;
                await simulator.ConnectAsync("", 115200, CancellationToken.None);
                Require(!simulator.JointsKnown && simulator.CartesianPoseKnown && simulator.CurrentJoints.GetAxis(5) == 0,
                    "Reconectar establece una nueva práctica cartesiana sin reutilizar valores articulares antiguos."); count++;
            }
            using (var serial = new SerialRobotTransport())
            {
                Require(!serial.JointsKnown && !serial.CartesianPoseKnown && !serial.CanMove,
                    "El diagnóstico serie no expone ángulos ni posición medidos."); count++;
                await RejectAsync(() => serial.ExecuteAsync("JOG_J 1 1 30", CancellationToken.None)); count++;
            }
            Console.WriteLine("OK   Articulaciones: " + count + " comprobaciones");
            return count;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
        private static void RejectSync(Action action)
        {
            try { action(); } catch (ArgumentException) { return; }
            throw new Exception("Se aceptó un estado articular no válido.");
        }
        private static async Task RejectAsync(Func<Task> action)
        {
            try { await action(); }
            catch (ArgumentException) { return; }
            catch (InvalidOperationException) { return; }
            throw new Exception("Se aceptó una orden articular que debía rechazarse.");
        }
        private static async Task CancelledAsync(Task action)
        {
            var completed = await Task.WhenAny(action, Task.Delay(2000));
            if (completed != action) throw new Exception("La cancelación articular no terminó dentro de2s.");
            try { await action; }
            catch (OperationCanceledException) { return; }
            throw new Exception("La operación articular no indicó su cancelación.");
        }
    }
}
