using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArmStudio.Core
{
    /// <summary>Transport abstraction. Events may arrive on a worker thread.</summary>
    public interface IRobotTransport : IDisposable
    {
        bool IsConnected { get; }
        bool CanMove { get; }
        string Name { get; }
        RobotPose CurrentPose { get; }
        JointState CurrentJoints { get; }
        bool CartesianPoseKnown { get; }
        bool JointsKnown { get; }
        event Action<string> MessageReceived;
        event Action<RobotPose> PoseChanged;
        event Action<JointState> JointsChanged;
        Task ConnectAsync(string port, int baud, CancellationToken cancellationToken);
        Task DisconnectAsync();
        Task ExecuteAsync(string command, CancellationToken cancellationToken);
        Task StopAsync();
    }

    /// <summary>Numeric Cartesian teaching simulator; no physical outputs.</summary>
    public sealed class SimulationTransport : IRobotTransport
    {
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _execution = new SemaphoreSlim(1, 1);
        private RobotPose _pose = new RobotPose(0, 0, 200, 0, 0, 0);
        private JointState _joints = new JointState();
        private bool _cartesianPoseKnown;
        private bool _jointsKnown;
        private bool _connected;
        private bool _enabled;
        private bool _disposed;
        private CancellationTokenSource _active;

        public event Action<string> MessageReceived;
        public event Action<RobotPose> PoseChanged;
        public event Action<JointState> JointsChanged;
        public bool IsConnected { get { lock (_sync) return _connected; } }
        public bool CanMove { get { return IsConnected; } }
        public string Name { get { return "Simulador cartesiano"; } }
        public RobotPose CurrentPose { get { lock (_sync) return _pose.Clone(); } }
        public JointState CurrentJoints { get { lock (_sync) return _joints.Clone(); } }
        public bool CartesianPoseKnown { get { lock (_sync) return _connected && _cartesianPoseKnown; } }
        public bool JointsKnown { get { lock (_sync) return _connected && _jointsKnown; } }

        public Task ConnectAsync(string port, int baud, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_connected) throw new InvalidOperationException("El simulador ya está conectado.");
                if (_active != null) throw new InvalidOperationException("Espera a que termine la operación anterior.");
                _pose = new RobotPose(0, 0, 200, 0, 0, 0);
                _joints = new JointState();
                _cartesianPoseKnown = true;
                _jointsKnown = false;
                _connected = true;
                _enabled = false;
            }
            Emit("SIM · Conectado. Motores deshabilitados. Posición numérica inicial: Z=200 mm.");
            EmitPose(CurrentPose);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            lock (_sync)
            {
                _connected = false;
                _enabled = false;
                if (_active != null) _active.Cancel();
            }
            Emit("SIM · Desconectado.");
            return Task.CompletedTask;
        }

        public async Task ExecuteAsync(string command, CancellationToken cancellationToken)
        {
            if (String.IsNullOrWhiteSpace(command)) throw new ArgumentException("Comando vacío.", "command");
            string[] words = command.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string name = words[0].ToUpperInvariant();
            if ((name == "ENABLE" && words.Length == 2 && words[1] == "0") || (name == "RESET" && words.Length == 1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureConnected();
                await StopAsync().ConfigureAwait(false);
                Emit(name == "RESET" ? "SIM · Restablecido; posición conservada, motores OFF." : "SIM · Motores OFF.");
                return;
            }
            if (!await _execution.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Ya hay una operación en curso.");
            CancellationTokenSource linked = null;
            try
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    if (!_connected) throw new InvalidOperationException("Conecta el simulador primero.");
                    if (name == "ENABLE" && words.Length == 2 && words[1] == "1")
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _enabled = true;
                    }
                    else
                    {
                        if (!_enabled) throw new InvalidOperationException("Habilita los motores antes de ejecutar.");
                        linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        _active = linked;
                    }
                }
                if (linked == null)
                {
                    Emit("SIM · Motores ON.");
                    return;
                }
                Emit("SIM TX · " + command);
                if (name == "RESET_JOINTS" || name == "RESET_CARTESIAN")
                {
                    RequireCount(words, 1);
                    lock (_sync)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (!_connected || !_enabled) throw new OperationCanceledException(linked.Token);
                        if (name == "RESET_JOINTS")
                        {
                            _joints = new JointState();
                            _jointsKnown = true;
                            _cartesianPoseKnown = false;
                        }
                        else
                        {
                            _pose = new RobotPose(0, 0, 200, 0, 0, 0);
                            _cartesianPoseKnown = true;
                            _jointsKnown = false;
                        }
                    }
                    if (name == "RESET_JOINTS")
                    {
                        EmitJoints(CurrentJoints);
                        Emit("SIM · Práctica independiente de ejes iniciada a 0°. TCP no calculado; sin cinemática definida.");
                    }
                    else
                    {
                        EmitPose(CurrentPose);
                        Emit("SIM · Práctica cartesiana iniciada en Z=200 mm. Ángulos articulares no calculados.");
                    }
                }
                else if (name == "JOG_J")
                {
                    RequireCount(words, 4);
                    int axis;
                    if (!Int32.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out axis) || axis < 1 || axis > 6)
                        throw new ArgumentException("JOG_J necesita un eje entre 1 y 6.");
                    axis--;
                    double delta = Number(words[2]);
                    double jointSpeed = Number(words[3]);
                    if (jointSpeed <= 0 || jointSpeed > 180) throw new ArgumentException("La velocidad articular debe ser mayor que 0 y hasta 180 grados/s.");
                    JointState start;
                    lock (_sync)
                    {
                        if (!_jointsKnown) throw new InvalidOperationException("Inicia la práctica de ejes antes del movimiento articular.");
                        start = _joints.Clone();
                    }
                    double origin = start.GetAxis(axis);
                    double destination = origin + delta;
                    start.WithAxis(axis, destination); // Reject the entire request before changing any angle.
                    int samples = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / 0.5));
                    for (int index = 1; index <= samples; index++)
                    {
                        double millis = Math.Max(1, Math.Abs(delta) / samples / jointSpeed * 1000.0);
                        await DelayCancellableAsync(millis, linked.Token).ConfigureAwait(false);
                        var sample = start.WithAxis(axis, index == samples ? destination : origin + delta * index / samples);
                        lock (_sync)
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            if (!_connected || !_enabled) throw new OperationCanceledException(linked.Token);
                            _joints = sample.Clone();
                            if (delta != 0) _cartesianPoseKnown = false;
                        }
                        EmitJoints(sample);
                    }
                }
                else if (name == "WAIT")
                {
                    RequireCount(words, 2);
                    double seconds = Number(words[1]);
                    if (seconds < 0 || seconds > 3600) throw new ArgumentException("La espera debe estar entre 0 y 3600 segundos.");
                    await Task.Delay(TimeSpan.FromSeconds(seconds), linked.Token).ConfigureAwait(false);
                }
                else
                {
                    lock (_sync)
                    {
                        if ((name == "MOVE_L" || name == "MOVE_C") && !_cartesianPoseKnown)
                            throw new InvalidOperationException("Inicia la práctica cartesiana antes de ejecutar MoveL o MoveC; el TCP no se deduce de los ángulos sin cinemática.");
                    }
                    List<RobotPose> samples;
                    RobotPose start = CurrentPose;
                    double speed;
                    if (name == "MOVE_L")
                    {
                        RequireCount(words, 8);
                        RobotPose target = ReadPose(words, 1);
                        speed = ReadSpeed(words[7]);
                        samples = MotionGeometry.SampleLinear(start, target, 100);
                    }
                    else if (name == "MOVE_C")
                    {
                        RequireCount(words, 14);
                        RobotPose via = ReadPose(words, 1);
                        RobotPose target = ReadPose(words, 7);
                        speed = ReadSpeed(words[13]);
                        samples = MotionGeometry.SampleCircular(start, via, target, 120);
                    }
                    else throw new ArgumentException("Comando de simulación no reconocido: " + name);
                    // Validate the entire arc before changing any numeric position.
                    foreach (RobotPose sample in samples) ValidatePose(sample);
                    RobotPose previous = start;
                    foreach (RobotPose sample in samples)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        double translation = Distance(previous, sample);
                        double angular = Math.Max(Math.Abs(previous.A - sample.A), Math.Max(Math.Abs(previous.B - sample.B), Math.Abs(previous.C - sample.C)));
                        // Pure orientation changes remain visible. This is not a dynamic robot model.
                        double millis = Math.Max(1, Math.Max(translation, angular) / speed * 1000.0);
                        // Chunk long delays so even very small valid speeds remain cancellable
                        // without overflowing Task.Delay's Int32 millisecond range.
                        await DelayCancellableAsync(millis, linked.Token).ConfigureAwait(false);
                        lock (_sync)
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            if (!_connected || !_enabled) throw new OperationCanceledException(linked.Token);
                            _pose = sample.Clone();
                            if (translation != 0 || angular != 0) _jointsKnown = false;
                        }
                        EmitPose(sample);
                        previous = sample;
                    }
                }
                Emit("SIM RX · OK " + name);
            }
            catch (OperationCanceledException)
            {
                lock (_sync) _enabled = false;
                Emit("SIM · Operación cancelada; motores OFF.");
                throw;
            }
            finally
            {
                lock (_sync)
                {
                    if (Object.ReferenceEquals(_active, linked)) _active = null;
                }
                if (linked != null) linked.Dispose();
                _execution.Release();
            }
        }

        public Task StopAsync()
        {
            lock (_sync)
            {
                _enabled = false;
                if (_active != null) _active.Cancel();
            }
            Emit("SIM · STOP solicitado; motores OFF.");
            return Task.CompletedTask;
        }

        private void EnsureConnected()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (!_connected) throw new InvalidOperationException("Conecta el simulador primero.");
            }
        }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException("SimulationTransport"); }
        private static void RequireCount(string[] words, int count)
        {
            if (words.Length != count) throw new ArgumentException("Número de argumentos incorrecto.");
        }
        private static double Number(string text)
        {
            double value;
            if (!Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || Double.IsNaN(value) || Double.IsInfinity(value))
                throw new ArgumentException("Número no válido: " + text);
            return value;
        }
        private static double ReadSpeed(string text)
        {
            double speed = Number(text);
            if (speed <= 0 || speed > 500) throw new ArgumentException("La velocidad debe ser mayor que 0 y hasta 500 mm/s.");
            return speed;
        }
        private static RobotPose ReadPose(string[] words, int offset)
        {
            RobotPose pose = new RobotPose(Number(words[offset]), Number(words[offset + 1]), Number(words[offset + 2]), Number(words[offset + 3]), Number(words[offset + 4]), Number(words[offset + 5]));
            ValidatePose(pose);
            return pose;
        }
        private static void ValidatePose(RobotPose pose)
        {
            double[] values = { pose.X, pose.Y, pose.Z, pose.A, pose.B, pose.C };
            for (int index = 0; index < values.Length; index++)
            {
                double limit = index < 3 ? 500 : 180;
                if (Double.IsNaN(values[index]) || Double.IsInfinity(values[index]) || Math.Abs(values[index]) > limit)
                    throw new ArgumentException("Pose fuera de los límites del simulador (XYZ ±500 mm, ABC ±180°).");
            }
        }
        private static double Distance(RobotPose first, RobotPose second)
        {
            double x = first.X - second.X, y = first.Y - second.Y, z = first.Z - second.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }
        private static async Task DelayCancellableAsync(double millis, CancellationToken cancellationToken)
        {
            while (millis > 250)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                millis -= 250;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(millis), cancellationToken).ConfigureAwait(false);
        }
        private void Emit(string message) { var handler = MessageReceived; if (handler != null) handler(message); }
        private void EmitPose(RobotPose pose) { var handler = PoseChanged; if (handler != null) handler(pose.Clone()); }
        private void EmitJoints(JointState joints) { var handler = JointsChanged; if (handler != null) handler(joints.Clone()); }
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _connected = false;
                _enabled = false;
                if (_active != null) _active.Cancel();
            }
            // The semaphore is intentionally not disposed while an operation may unwind.
        }
    }

    /// <summary>
    /// USB/serial diagnostic transport. It never accepts motion commands.
    /// A matching controller greeting is required before reporting connected.
    /// </summary>
    public sealed class SerialRobotTransport : IRobotTransport
    {
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _requests = new SemaphoreSlim(1, 1);
        private SerialPort _port;
        private CancellationTokenSource _readerCancellation;
        private Task _reader;
        private TaskCompletionSource<string> _pending;
        private Func<string, bool> _matches;
        private bool _connected;
        private bool _disposed;
        private long _sequence;

        public event Action<string> MessageReceived;
        public event Action<RobotPose> PoseChanged { add { } remove { } }
        public event Action<JointState> JointsChanged { add { } remove { } }
        public bool IsConnected { get { lock (_sync) return _connected; } }
        public bool CanMove { get { return false; } }
        public string Name { get { return "USB / serie · diagnóstico"; } }
        public RobotPose CurrentPose { get { return new RobotPose(0, 0, 0, 0, 0, 0); } }
        public JointState CurrentJoints { get { return new JointState(); } }
        public bool CartesianPoseKnown { get { return false; } }
        public bool JointsKnown { get { return false; } }

        public async Task ConnectAsync(string port, int baud, CancellationToken cancellationToken)
        {
            if (String.IsNullOrWhiteSpace(port)) throw new ArgumentException("Selecciona un puerto serie.", "port");
            if (baud < 9600 || baud > 230400) throw new ArgumentException("Velocidad serie no válida.", "baud");
            if (!await _requests.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Hay otra operación de conexión o diagnóstico en curso.");
            try
            {
                lock (_sync)
                {
                    if (_disposed) throw new ObjectDisposedException("SerialRobotTransport");
                    if (_port != null) throw new InvalidOperationException("El puerto ya está abierto.");
                }
                var serial = new SerialPort(port, baud, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None, DtrEnable = false, RtsEnable = false,
                    Encoding = Encoding.ASCII, NewLine = "\n", ReadTimeout = 100, WriteTimeout = 750
                };
                lock (_sync) _port = serial;
                // SerialPort has no cancellable Open API. A background worker keeps the UI responsive.
                Task opening = Task.Run(() => serial.Open());
                try { await WithTimeout(opening, 3000, cancellationToken).ConfigureAwait(false); }
                catch
                {
                    // Dispose also after a late Open, so timed-out opens cannot leak a port.
                    _ = opening.ContinueWith(ignored => { try { serial.Dispose(); } catch { } }, TaskScheduler.Default);
                    throw;
                }
                cancellationToken.ThrowIfCancellationRequested();
                serial.DiscardInBuffer();
                var readerCancellation = new CancellationTokenSource();
                lock (_sync) _readerCancellation = readerCancellation;
                _reader = Task.Run(() => ReadLoop(serial, readerCancellation.Token));
                Emit("SERIE · Puerto abierto. Esperando arranque del microcontrolador…");
                // Many USB boards reset when opened, even with DTR deasserted.
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
                string greeting = await SendAsync("HELLO 1", value => value.StartsWith("ARMSTUDIO ", StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
                if (greeting != "ARMSTUDIO 1 DIAGNOSTIC")
                    throw new IOException("Firmware incompatible: " + greeting);
                lock (_sync)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Object.ReferenceEquals(_port, serial) || !serial.IsOpen) throw new IOException("El puerto se cerró durante la conexión.");
                    _connected = true;
                }
                Emit("SERIE · Conectado y verificado. Firmware de diagnóstico; movimiento deshabilitado.");
            }
            catch
            {
                ClosePort(new IOException("La conexión serie no pudo completarse."));
                throw;
            }
            finally { _requests.Release(); }
        }

        public async Task ExecuteAsync(string command, CancellationToken cancellationToken)
        {
            string operation = (command ?? "").Trim().ToUpperInvariant();
            if (operation != "PING" && operation != "STATUS")
                throw new InvalidOperationException("El firmware de diagnóstico solo admite PING y STATUS; no controla motores.");
            if (!await _requests.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Ya hay una petición serie en curso.");
            try
            {
                lock (_sync)
                {
                    if (_disposed) throw new ObjectDisposedException("SerialRobotTransport");
                    if (!_connected) throw new InvalidOperationException("Conecta y verifica el puerto primero.");
                }
                string sequence = "N" + Interlocked.Increment(ref _sequence).ToString(CultureInfo.InvariantCulture);
                string response = await SendAsync(sequence + " " + operation,
                    value => value.StartsWith("OK " + sequence + " ", StringComparison.Ordinal) || value.StartsWith("ERR " + sequence + " ", StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
                string expected = "OK " + sequence + (operation == "PING" ? " PONG" : " DIAGNOSTIC");
                if (response != expected) throw new IOException("Respuesta del controlador no válida: " + response);
            }
            catch (Exception error)
            {
                // A timed-out request leaves uncertain framing/state. Require a fresh handshake.
                ClosePort(error);
                throw;
            }
            finally { _requests.Release(); }
        }

        public Task StopAsync()
        {
            // There are no motors in this firmware. Never claim a UART message is a hardware stop.
            Emit("SERIE · STOP local; el firmware de diagnóstico no tiene salidas de movimiento.");
            return Task.CompletedTask;
        }

        public async Task DisconnectAsync()
        {
            Task reader;
            lock (_sync) reader = _reader;
            ClosePort(new IOException("Puerto desconectado."));
            if (reader != null)
            {
                // ReadTimeout is short, and the extra bound protects against platform driver issues.
                await Task.WhenAny(reader, Task.Delay(500)).ConfigureAwait(false);
            }
            Emit("SERIE · Desconectado.");
        }

        private async Task<string> SendAsync(string message, Func<string, bool> matches, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            SerialPort serial;
            lock (_sync)
            {
                serial = _port;
                if (serial == null || !serial.IsOpen) throw new IOException("Puerto serie cerrado.");
                if (_pending != null) throw new InvalidOperationException("Petición serie anterior pendiente.");
                _pending = completion;
                _matches = matches;
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Emit("TX · " + message);
                await Task.Run(() => serial.WriteLine(message), cancellationToken).ConfigureAwait(false);
                await WithTimeout(completion.Task, 2000, cancellationToken).ConfigureAwait(false);
                return await completion.Task.ConfigureAwait(false);
            }
            finally
            {
                lock (_sync)
                {
                    if (Object.ReferenceEquals(_pending, completion)) { _pending = null; _matches = null; }
                }
            }
        }

        private void ReadLoop(SerialPort serial, CancellationToken token)
        {
            var line = new StringBuilder();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int character;
                    try { character = serial.ReadByte(); }
                    catch (TimeoutException) { continue; }
                    if (character < 0) throw new IOException("El controlador cerró el puerto.");
                    if (character == '\r') continue;
                    if (character != '\n')
                    {
                        if (character < 32 || character > 126) throw new IOException("Datos no ASCII en el protocolo serie.");
                        if (line.Length >= 255) throw new IOException("Trama serie demasiado larga.");
                        line.Append((char)character);
                        continue;
                    }
                    string message = line.ToString();
                    line.Clear();
                    if (message.Length == 0) continue;
                    Emit("RX · " + message);
                    lock (_sync)
                    {
                        if (!Object.ReferenceEquals(_port, serial)) return;
                        if (_pending != null && _matches != null && _matches(message)) _pending.TrySetResult(message);
                    }
                }
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    ClosePort(error, serial);
                    Emit("SERIE · Error de lectura: " + error.Message);
                }
            }
        }

        private static async Task WithTimeout(Task task, int milliseconds, CancellationToken cancellationToken)
        {
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task delay = Task.Delay(milliseconds, timer.Token);
                Task completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
                if (completed != task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("El controlador no respondió dentro del tiempo permitido.");
                }
                timer.Cancel();
                await task.ConfigureAwait(false);
            }
        }

        private void ClosePort(Exception reason, SerialPort expected = null)
        {
            SerialPort serial;
            CancellationTokenSource readerCancellation;
            lock (_sync)
            {
                if (expected != null && !Object.ReferenceEquals(_port, expected)) return;
                serial = _port;
                _port = null;
                _connected = false;
                readerCancellation = _readerCancellation;
                _readerCancellation = null;
                if (_pending != null) _pending.TrySetException(reason);
                _pending = null;
                _matches = null;
            }
            if (readerCancellation != null) { readerCancellation.Cancel(); readerCancellation.Dispose(); }
            if (serial != null)
            {
                try { serial.Close(); } catch { }
                try { serial.Dispose(); } catch { }
            }
        }
        private void Emit(string message) { var handler = MessageReceived; if (handler != null) handler(message); }
        public void Dispose()
        {
            lock (_sync) { if (_disposed) return; _disposed = true; }
            ClosePort(new ObjectDisposedException("SerialRobotTransport"));
        }
    }
}
