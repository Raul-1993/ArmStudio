using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ArmStudio.Core;

namespace ArmStudio.WinForms
{
    public sealed class MainForm : Form
    {
        private RobotProject project = DemoProject.Create();
        private IRobotTransport transport;
        private CancellationTokenSource execution;
        private CancellationTokenSource connectionOperation;
        private readonly CodeEditor code = new CodeEditor();
        private readonly ComboBox mode = Combo(new[] { "Simulación", "USB / Serie" }, 145);
        private readonly ComboBox port = Combo(new string[0], 100);
        private readonly ComboBox baud = Combo(new[] { "115200", "57600", "9600" }, 94);
        private readonly ComboBox increment = Combo(new[] { "1", "5", "10", "25" }, 68);
        private readonly Button connect = Theme.Button("Conectar", true);
        private readonly Button enable = Theme.Button("Habilitar movimientos");
        private readonly Button play = Theme.Button("▶  Ejecutar", true);
        private readonly Button step = Theme.Button("Paso a paso");
        private readonly Button pause = Theme.Button("Pausa al final");
        private readonly Button reset = Theme.Button("Rearmar parada");
        private readonly Button diagnostic = Theme.Button("Probar USB");
        private readonly Label connectionState = Theme.Label("●  Sin conexión", 10, Theme.Muted);
        private readonly Label runState = Theme.Label("LISTO PARA EDITAR", 9, Theme.Accent, FontStyle.Bold);
        private readonly Label sessionState = Theme.Label("SIMULACIÓN", 9, Theme.Accent, FontStyle.Bold);
        private readonly Label progressText = Theme.Label("Ninguna instrucción ejecutada", 9, Theme.Muted);
        private readonly Label fileLabel = Theme.Label("Practica_01.armstudio", 10, Theme.Text, FontStyle.Bold);
        private readonly Label positionLabel = Theme.Label("Posición simulada · base / tool0", 9, Theme.Muted);
        private readonly Label speedLabel = Theme.Label("25 %", 10, Theme.Accent, FontStyle.Bold);
        private readonly Label footer = Theme.Label("SESIÓN LOCAL     /     Sin conexión     /     Unidades: mm · grados", 8, Theme.Muted);
        private readonly Label[] axisValues = new Label[6];
        private readonly List<Button> jogButtons = new List<Button>();
        private readonly List<Control> projectControls = new List<Control>();
        private readonly RichTextBox eventLog = new RichTextBox();
        private readonly DataGridView pointGrid = new DataGridView();
        private readonly TabControl bottomTabs = new DarkTabs();
        private readonly TabControl motionTabs = new DarkTabs();
        private readonly JointJogPanel jointPanel = new JointJogPanel();
        private readonly Button cartesianPractice = Theme.Button("Iniciar práctica XYZ");
        private readonly TrackBar speed = new TrackBar();
        private readonly ProgressBar progress = new ProgressBar();
        private bool busy, moving, motors, stopped, pauseRequested, dirty, closing, loading;
        private int nextInstruction;
        private string currentPath;
        private readonly List<string> logLines = new List<string>();

        public MainForm()
        {
            Text = "ArmStudio | Mando de control robótico";
            Name = "MainForm";
            ClientSize = new Size(1440, 944);
            MinimumSize = new Size(1200, 880);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.Ui();
            AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true;
            BuildLayout();
            code.Source = project.Source;
            code.SourceChanged += delegate { if (loading) return; project.Source = code.Source; dirty = true; nextInstruction = 0; code.ActiveLine = 0; progress.Value = 0; UpdateTitle(); UpdateControls(); };
            RefreshPoints(); RefreshPorts();
            connect.Name = "ConnectButton"; enable.Name = "EnableButton"; play.Name = "PlayButton";
            step.Name = "StepButton"; pause.Name = "PauseButton"; reset.Name = "ResetButton";
            code.Name = "ProgramEditor"; pointGrid.Name = "PointGrid"; mode.Name = "ConnectionMode";
            connect.Click += async delegate { await ConnectToggleAsync(); };
            enable.Click += async delegate { await ToggleMotorsAsync(); };
            play.Click += async delegate { if (execution != null && pauseRequested) { pauseRequested = false; SetState("EJECUTANDO"); UpdateControls(); } else await RunProgramAsync(false); };
            step.Click += async delegate { await RunProgramAsync(true); };
            pause.Click += delegate { pauseRequested = true; SetState("PAUSA AL TERMINAR INSTRUCCIÓN"); Log("Pausa solicitada al finalizar la instrucción actual."); UpdateControls(); };
            reset.Click += async delegate { await RearmAsync(); };
            diagnostic.Click += async delegate { await TestUsbAsync(); };
            mode.SelectedIndexChanged += delegate { UpdateControls(); };
            speed.ValueChanged += delegate { speedLabel.Text = speed.Value + " %"; };
            jointPanel.JogRequested += async (axis, direction) => { await JogJointAsync(axis, direction); };
            jointPanel.StartPracticeRequested += async delegate { await StartPracticeAsync(true); };
            cartesianPractice.Click += async delegate { await StartPracticeAsync(false); };
            motionTabs.SelectedIndexChanged += delegate { UpdateControls(); };
            FormClosing += OnClosing;
            KeyDown += OnKeyDown;
            Log("ArmStudio preparado. Conecta el simulador para practicar sin hardware.");
            UpdateTitle();
            UpdateControls();
        }

        private void BuildLayout()
        {
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 18, 26), Padding = new Padding(18, 30, 18, 16) };
            shell.Controls.Add(sidebar, 0, 0);
            var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            nav.Controls.Add(Theme.Label("◈", 24, Theme.Accent, FontStyle.Bold));
            nav.Controls.Add(Theme.Label("ARMSTUDIO", 12, Theme.Text, FontStyle.Bold));
            nav.Controls.Add(Theme.Label("ROBOT CONTROL", 8, Theme.Muted));
            nav.Controls.Add(new Panel { Height = 44, Width = 130 });
            AddNav(nav, "01   Programación", delegate { code.Editor.Focus(); }, true);
            AddNav(nav, "02   Movimiento", delegate { motionTabs.SelectedIndex = 1; jointPanel.Focus(); });
            AddNav(nav, "03   Puntos", delegate { bottomTabs.SelectedIndex = 0; pointGrid.Focus(); });
            AddNav(nav, "04   Conexión", delegate { mode.Focus(); });
            AddNav(nav, "05   Eventos", delegate { bottomTabs.SelectedIndex = 1; });
            sidebar.Controls.Add(nav);
            var sideFoot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 138, FlowDirection = FlowDirection.TopDown };
            var help = Theme.Button("Guía rápida"); help.Width = 142; help.Click += delegate { ShowHelp(); }; sideFoot.Controls.Add(help);
            sideFoot.Controls.Add(Theme.Label("DESARROLLO PERSONAL", 8, Theme.Muted));
            sideFoot.Controls.Add(Theme.Label("Teach pendant · v0.2", 9, Theme.Muted));
            sideFoot.Controls.Add(Theme.Label("C# / Windows Forms", 8, Theme.Muted)); sidebar.Controls.Add(sideFoot);

            var main = Rows(84, 66, -1, 32); main.Padding = new Padding(22, 14, 22, 0); shell.Controls.Add(main, 1, 0);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            var titles = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            titles.Controls.Add(Theme.Label("ESPACIO DE TRABAJO  /  MainModule", 8, Theme.Muted));
            titles.Controls.Add(Theme.Label("Programación del robot", 23, Theme.Text, FontStyle.Bold));
            titles.Controls.Add(Theme.Label("Enseña puntos, escribe trayectorias y ejecuta cada movimiento.", 10, Theme.Muted));
            header.Controls.Add(titles, 0, 0);
            var headerStatus = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8, 8, 0, 0) };
            headerStatus.Controls.Add(sessionState); headerStatus.Controls.Add(Theme.Label("Sin representación 3D del brazo", 9, Theme.Muted));
            header.Controls.Add(headerStatus, 1, 0); main.Controls.Add(header, 0, 0);

            var connectionCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 8, 8), Margin = new Padding(0, 3, 0, 10) };
            var connectionFlow = Flow(); connectionFlow.Controls.Add(Theme.Label("CONEXIÓN", 8, Theme.Muted, FontStyle.Bold));
            connectionFlow.Controls.Add(mode); connectionFlow.Controls.Add(port);
            var refresh = Theme.Button("↻"); refresh.Width = 36; refresh.Click += delegate { RefreshPorts(); }; connectionFlow.Controls.Add(refresh); projectControls.Add(refresh);
            connectionFlow.Controls.Add(baud); connect.Width = 112; connectionFlow.Controls.Add(connect); diagnostic.Width = 98; connectionFlow.Controls.Add(diagnostic); connectionFlow.Controls.Add(connectionState);
            connectionCard.Controls.Add(connectionFlow); main.Controls.Add(connectionCard, 0, 1);

            var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 362)); main.Controls.Add(workspace, 0, 2);
            var left = Rows(-1, 280); left.Margin = new Padding(0, 0, 14, 0); workspace.Controls.Add(left, 0, 0);
            left.Controls.Add(BuildEditor(), 0, 0); left.Controls.Add(BuildBottom(), 0, 1);
            workspace.Controls.Add(BuildMotion(), 1, 0);
            footer.Dock = DockStyle.Fill; footer.TextAlign = ContentAlignment.MiddleLeft; main.Controls.Add(footer, 0, 3);
        }

        private Control BuildEditor()
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12) };
            var layout = Rows(28, 42, -1, 28, 46); card.Controls.Add(layout);
            var title = Flow(); title.Controls.Add(fileLabel); title.Controls.Add(Theme.Label("  /  main", 9, Theme.Muted)); title.Controls.Add(runState); layout.Controls.Add(title, 0, 0);
            var toolbar = Flow();
            AddProjectButton(toolbar, "Abrir", delegate { OpenProject(); }, 62);
            AddProjectButton(toolbar, "Guardar", delegate { SaveProject(false); }, 92);
            AddProjectButton(toolbar, "+ MoveL", delegate { InsertInstruction(false); }, 95);
            AddProjectButton(toolbar, "+ MoveC", delegate { InsertInstruction(true); }, 95);
            var validate = Theme.Button("Validar"); validate.Width = 76; validate.Click += delegate { ValidateProgram(true); }; toolbar.Controls.Add(validate);
            layout.Controls.Add(toolbar, 0, 1);
            code.Dock = DockStyle.Fill; layout.Controls.Add(code, 0, 2);
            progressText.Dock = DockStyle.Fill; progressText.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(progressText, 0, 3);
            var run = Flow(); play.Width = 128; step.Width = 116; pause.Width = 116; run.Controls.Add(play); run.Controls.Add(step); run.Controls.Add(pause);
            var rewind = Theme.Button("↶ Inicio"); rewind.Width = 88; rewind.Click += delegate { nextInstruction = 0; code.ActiveLine = 0; progress.Value = 0; progressText.Text = "Puntero al inicio del programa"; }; run.Controls.Add(rewind); projectControls.Add(rewind);
            layout.Controls.Add(run, 0, 4); return card;
        }

        private Control BuildBottom()
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(9), Margin = Padding.Empty };
            bottomTabs.Dock = DockStyle.Fill; bottomTabs.Font = Theme.Ui(9); bottomTabs.Padding = new Point(14, 6);
            var pointsTab = new TabPage("Puntos guardados") { BackColor = Theme.Surface, Padding = new Padding(5) };
            var eventsTab = new TabPage("Registro de eventos") { BackColor = Theme.Surface, Padding = new Padding(5) };
            bottomTabs.TabPages.Add(pointsTab); bottomTabs.TabPages.Add(eventsTab); card.Controls.Add(bottomTabs);
            var pointLayout = Rows(37, -1); pointsTab.Controls.Add(pointLayout);
            var actions = Flow();
            AddProjectButton(actions, "+ Nuevo", delegate { EditPoint(true); }, 84);
            AddProjectButton(actions, "Editar", delegate { EditPoint(false); }, 72);
            AddProjectButton(actions, "Eliminar", delegate { DeletePoint(); }, 80);
            actions.Controls.Add(Theme.Label("Base · mm / °", 9, Theme.Muted)); pointLayout.Controls.Add(actions, 0, 0);
            pointGrid.Dock = DockStyle.Fill; pointGrid.ReadOnly = true; pointGrid.AllowUserToAddRows = false; pointGrid.AllowUserToDeleteRows = false;
            pointGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; pointGrid.MultiSelect = false; pointGrid.AutoGenerateColumns = false;
            Theme.StyleGrid(pointGrid);
            foreach (var name in new[] { "Punto", "X", "Y", "Z", "A", "B", "C" }) pointGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, Name = name, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = name == "Punto" ? 165 : 100 });
            pointGrid.CellDoubleClick += delegate { if (!moving && !busy) EditPoint(false); };
            pointLayout.Controls.Add(pointGrid, 0, 1);
            var eventLayout = Rows(34, -1); eventsTab.Controls.Add(eventLayout);
            var export = Theme.Button("Exportar registro"); export.Width = 145; export.Click += delegate { ExportLog(); }; eventLayout.Controls.Add(export, 0, 0);
            eventLog.Dock = DockStyle.Fill; eventLog.ReadOnly = true; eventLog.BorderStyle = BorderStyle.None; eventLog.BackColor = Theme.Background; eventLog.ForeColor = Theme.Muted; eventLog.Font = new Font(FontFamily.GenericMonospace, 9); eventLog.WordWrap = false;
            eventLayout.Controls.Add(eventLog, 0, 1); return card;
        }

        private Control BuildMotion()
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty };
            var layout = Rows(33, 24, 36, 306, 27, 26, 34, 20, 40, 36, -1); card.Controls.Add(layout);
            layout.Controls.Add(Theme.Label("Movimiento manual", 16, Theme.Text, FontStyle.Bold), 0, 0);
            layout.Controls.Add(positionLabel, 0, 1); enable.Dock = DockStyle.Fill; layout.Controls.Add(enable, 0, 2);
            motionTabs.Name = "MotionTabs"; motionTabs.Dock = DockStyle.Fill; motionTabs.Margin = new Padding(0, 6, 0, 4);
            var cartesianTab = new TabPage("Cartesiano") { BackColor = Theme.Surface, Padding = new Padding(3) };
            var jointTab = new TabPage("Ejes J1–J6") { BackColor = Theme.Surface, Padding = new Padding(3) };
            motionTabs.TabPages.Add(cartesianTab); motionTabs.TabPages.Add(jointTab); layout.Controls.Add(motionTabs, 0, 3);
            var cartesianLayout = Rows(32, -1, 32); cartesianTab.Controls.Add(cartesianLayout);
            var units = Flow(); units.Controls.Add(Theme.Label("PASO", 8, Theme.Muted)); units.Controls.Add(increment); increment.SelectedIndex = 1; units.Controls.Add(Theme.Label("mm / °", 9, Theme.Muted)); cartesianLayout.Controls.Add(units, 0, 0);
            var axes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 6, Margin = Padding.Empty };
            axes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34)); axes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); axes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48)); axes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            for (int i = 0; i < 6; i++)
            {
                axes.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6));
                var axis = i; var name = "XYZABC"[i].ToString(); var label = Theme.Label(name, 12, i < 3 ? Theme.Accent : Theme.Blue, FontStyle.Bold); label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft; axes.Controls.Add(label, 0, i);
                axisValues[i] = Theme.Label(i == 2 ? "200.00" : "0.00", 13, Theme.Text); axisValues[i].Font = Theme.Mono(12); axisValues[i].Dock = DockStyle.Fill; axisValues[i].TextAlign = ContentAlignment.MiddleRight; axes.Controls.Add(axisValues[i], 1, i);
                foreach (var direction in new[] { -1, 1 })
                {
                    var dir = direction; var button = Theme.Button(direction < 0 ? "−" : "+"); button.Dock = DockStyle.Fill; button.AccessibleName = "Mover " + name + (dir < 0 ? " negativo" : " positivo"); button.Click += async delegate { await JogAsync(axis, dir); }; jogButtons.Add(button); axes.Controls.Add(button, dir < 0 ? 2 : 3, i);
                }
            }
            cartesianLayout.Controls.Add(axes, 0, 1);
            cartesianPractice.Name = "StartCartesianPractice"; cartesianPractice.Dock = DockStyle.Fill; cartesianLayout.Controls.Add(cartesianPractice, 0, 2);
            jointPanel.Dock = DockStyle.Fill; jointTab.Controls.Add(jointPanel);
            var speedRow = Flow(); speedRow.Controls.Add(Theme.Label("VELOCIDAD", 8, Theme.Muted)); speedRow.Controls.Add(speedLabel); layout.Controls.Add(speedRow, 0, 4);
            speed.Minimum = 10; speed.Maximum = 100; speed.Value = 25; speed.AutoSize = false; speed.TickStyle = TickStyle.None; speed.Dock = DockStyle.Fill; speed.BackColor = Theme.Surface; layout.Controls.Add(speed, 0, 5);
            var teach = Theme.Button("+  Capturar posición XYZ actual"); teach.Dock = DockStyle.Fill; teach.Name = "TeachButton"; teach.Click += delegate { TeachPoint(); }; projectControls.Add(teach); layout.Controls.Add(teach, 0, 6);
            progress.Dock = DockStyle.Fill; progress.Height = 8; progress.Style = ProgressBarStyle.Continuous; progress.Margin = new Padding(0, 6, 0, 6); layout.Controls.Add(progress, 0, 7);
            var stop = Theme.Button("■   PARADA DE SOFTWARE", false, true); stop.Name = "StopButton"; stop.Dock = DockStyle.Fill; stop.Click += async delegate { await StopAllAsync(); }; layout.Controls.Add(stop, 0, 8);
            reset.Dock = DockStyle.Fill; layout.Controls.Add(reset, 0, 9);
            var note = Theme.Label("Límites de práctica · sin cinemática del brazo.\nUSB: diagnóstico, sin movimiento de motores.", 8, Theme.Muted); note.Dock = DockStyle.Fill; note.Padding = new Padding(0, 6, 0, 0); layout.Controls.Add(note, 0, 10); return card;
        }

        private async Task ConnectToggleAsync()
        {
            if (busy) return; busy = true; connectionOperation = new CancellationTokenSource(); UpdateControls();
            try
            {
                if (transport != null && transport.IsConnected)
                {
                    await StopAllAsync(); await transport.DisconnectAsync(); transport.Dispose(); transport = null; stopped = false;
                    Log("Conexión cerrada."); SetState("DESCONECTADO");
                }
                else
                {
                    if (transport != null) transport.Dispose();
                    transport = mode.SelectedIndex == 0 ? (IRobotTransport)new SimulationTransport() : new SerialRobotTransport();
                    var session = transport;
                    transport.MessageReceived += message => Post(delegate { if (transport == session) { Log(message); UpdateControls(); } });
                    transport.PoseChanged += pose => Post(delegate { if (transport == session) { if (transport.CartesianPoseKnown) ShowPose(pose); UpdateControls(); } });
                    transport.JointsChanged += joints => Post(delegate { if (transport == session) UpdateControls(); });
                    if (mode.SelectedIndex != 0 && port.SelectedItem == null) throw new InvalidOperationException("No hay un puerto COM seleccionado. Conecta la placa y actualiza los puertos.");
                    await transport.ConnectAsync(port.Text, int.Parse(baud.Text, CultureInfo.InvariantCulture), connectionOperation.Token);
                    motors = false; stopped = false; nextInstruction = 0; code.ActiveLine = 0;
                    progress.Value = 0; progressText.Text = "Ninguna instrucción ejecutada";
                    if (transport.CanMove) ShowPose(transport.CurrentPose);
                    Log(transport.CanMove ? "Simulador conectado. Habilita los movimientos para comenzar." : "USB conectado y verificado. Sesión de diagnóstico: el firmware no controla motores.");
                    SetState(transport.CanMove ? "SIMULADOR CONECTADO" : "USB · DIAGNÓSTICO");
                }
            }
            catch (Exception ex) { if (transport != null) { transport.Dispose(); transport = null; } Report(ex); }
            finally { connectionOperation.Dispose(); connectionOperation = null; busy = false; UpdateControls(); }
        }

        private async Task ToggleMotorsAsync()
        {
            if (!Ready() || moving) return;
            busy = true; UpdateControls();
            try { await transport.ExecuteAsync(motors ? "ENABLE 0" : "ENABLE 1", CancellationToken.None); motors = !motors; Log(motors ? "Movimientos habilitados en simulación." : "Movimientos deshabilitados."); }
            catch (Exception ex) { Report(ex); }
            finally { busy = false; UpdateControls(); }
        }

        private async Task TestUsbAsync()
        {
            if (busy || transport == null || !transport.IsConnected || transport.CanMove) return;
            busy = true; connectionOperation = new CancellationTokenSource(); UpdateControls();
            try { await transport.ExecuteAsync("PING", connectionOperation.Token); await transport.ExecuteAsync("STATUS", connectionOperation.Token); Log("Diagnóstico USB correcto: PONG y estado DIAGNOSTIC recibidos."); bottomTabs.SelectedIndex = 1; }
            catch (Exception ex) { Report(ex); }
            finally { connectionOperation.Dispose(); connectionOperation = null; busy = false; UpdateControls(); }
        }

        private ParseResult ValidateProgram(bool announce)
        {
            var result = ProgramParser.Parse(code.Source, project.Points);
            if (!result.Success)
            {
                foreach (var error in result.Diagnostics) Log("VALIDACIÓN · línea " + error.LineNumber + ": " + error.Message);
                bottomTabs.SelectedIndex = 1; code.ActiveLine = result.Diagnostics[0].LineNumber; SetState("REVISAR PROGRAMA");
                progressText.Text = result.Diagnostics.Count + " error(es). Consulta el registro de eventos.";
            }
            else if (announce) { Log("Validación correcta: " + result.Instructions.Count + " instrucciones. fine / z0 = parada exacta."); progressText.Text = result.Instructions.Count + " instrucciones válidas · herramienta tool0"; SetState("PROGRAMA VÁLIDO"); }
            return result;
        }

        private async Task RunProgramAsync(bool single)
        {
            if (!CartesianReady() || !motors || moving || stopped) return;
            var parsed = ValidateProgram(false); if (!parsed.Success) return;
            if (parsed.Instructions.Count == 0) { Log("El programa no contiene instrucciones ejecutables."); return; }
            // Preflight uses the actual current simulated pose, so invalid arcs never execute partially.
            try { Preflight(parsed.Instructions); } catch (Exception ex) { Report(ex); return; }
            if (nextInstruction >= parsed.Instructions.Count) nextInstruction = 0;
            moving = true; pauseRequested = false; execution = new CancellationTokenSource(); var cancellation = execution;
            UpdateControls(); SetState(single ? "EJECUCIÓN PASO A PASO" : "EJECUTANDO");
            try
            {
                do
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    while (pauseRequested) { SetState("EN PAUSA · INSTRUCCIÓN FINALIZADA"); await Task.Delay(80, cancellation.Token); }
                    var instruction = parsed.Instructions[nextInstruction]; code.ActiveLine = instruction.LineNumber;
                    Log("L" + instruction.LineNumber + " · " + instruction.Kind + (instruction.Target == null ? "" : " → " + instruction.Target));
                    await transport.ExecuteAsync(ToCommand(instruction), cancellation.Token);
                    nextInstruction++;
                    progress.Value = nextInstruction * 100 / parsed.Instructions.Count;
                    progressText.Text = "Instrucción " + nextInstruction + " de " + parsed.Instructions.Count + " completada";
                } while (!single && nextInstruction < parsed.Instructions.Count);
                SetState(nextInstruction == parsed.Instructions.Count ? "PROGRAMA COMPLETADO" : "PASO COMPLETADO");
                if (nextInstruction == parsed.Instructions.Count) Log("Programa completado en simulación.");
            }
            catch (OperationCanceledException) { Log("Ejecución interrumpida. Puntero al inicio; posición conservada."); nextInstruction = 0; }
            catch (Exception ex) { await FailMotionAsync(ex); }
            finally { cancellation.Dispose(); if (execution == cancellation) execution = null; moving = false; pauseRequested = false; UpdateControls(); }
        }

        private void Preflight(IList<MotionInstruction> instructions)
        {
            var pose = transport.CurrentPose.Clone(); int first = nextInstruction >= instructions.Count ? 0 : nextInstruction;
            for (int i = first; i < instructions.Count; i++)
            {
                var instruction = instructions[i]; if (instruction.Kind == MotionKind.WaitTime) continue;
                var target = FindPoint(instruction.Target).Pose;
                if (instruction.Kind == MotionKind.MoveC) MotionGeometry.SampleCircular(pose, FindPoint(instruction.Via).Pose, target, 40);
                else MotionGeometry.SampleLinear(pose, target, 2);
                pose = target;
            }
        }

        private string ToCommand(MotionInstruction instruction)
        {
            if (instruction.Kind == MotionKind.WaitTime) return "WAIT " + WireNumber(instruction.Seconds);
            var destination = PoseText(FindPoint(instruction.Target).Pose);
            var effectiveSpeed = WireNumber(instruction.Speed * speed.Value / 100.0);
            return instruction.Kind == MotionKind.MoveC ? "MOVE_C " + PoseText(FindPoint(instruction.Via).Pose) + " " + destination + " " + effectiveSpeed : "MOVE_L " + destination + " " + effectiveSpeed;
        }

        private async Task JogAsync(int axis, int direction)
        {
            if (!CartesianReady() || !motors || moving || stopped) return;
            moving = true; execution = new CancellationTokenSource(); var cancellation = execution; UpdateControls();
            try
            {
                var pose = transport.CurrentPose.Clone(); var values = new[] { pose.X, pose.Y, pose.Z, pose.A, pose.B, pose.C };
                var target = pose.WithAxis(axis, values[axis] + direction * double.Parse(increment.Text, CultureInfo.InvariantCulture));
                MotionGeometry.ValidatePose(target); SetState("MOVIMIENTO MANUAL");
                await transport.ExecuteAsync("MOVE_L " + PoseText(target) + " " + WireNumber(50 * speed.Value / 100.0), cancellation.Token);
                nextInstruction = 0; progress.Value = 0; SetState("MOVIMIENTO COMPLETADO");
            }
            catch (OperationCanceledException) { Log("Movimiento manual interrumpido."); }
            catch (Exception ex) { await FailMotionAsync(ex); }
            finally { cancellation.Dispose(); if (execution == cancellation) execution = null; moving = false; UpdateControls(); }
        }

        private async Task StartPracticeAsync(bool joints)
        {
            if (!Ready() || !motors || moving || stopped || busy) return;
            busy = true; UpdateControls();
            try
            {
                await transport.ExecuteAsync(joints ? "RESET_JOINTS" : "RESET_CARTESIAN", CancellationToken.None);
                nextInstruction = 0; code.ActiveLine = 0; progress.Value = 0;
                progressText.Text = joints ? "Práctica de articulaciones · TCP no calculado" : "Práctica cartesiana · origen simulado restaurado";
                SetState(joints ? "PRÁCTICA DE EJES" : "PRÁCTICA CARTESIANA");
            }
            catch (Exception ex) { Report(ex); }
            finally { busy = false; UpdateControls(); }
        }

        private async Task JogJointAsync(int axis, int direction)
        {
            if (!Ready() || !transport.JointsKnown || !motors || moving || stopped || busy) return;
            var current = transport.CurrentJoints.GetAxis(axis);
            var target = Math.Max(JointState.Min(axis), Math.Min(JointState.Max(axis), current + direction * jointPanel.StepDegrees));
            var delta = target - current; if (Math.Abs(delta) < 1e-10) return;
            moving = true; execution = new CancellationTokenSource(); var cancellation = execution; UpdateControls();
            try
            {
                SetState("MOVIENDO EJE J" + (axis + 1));
                await transport.ExecuteAsync("JOG_J " + (axis + 1) + " " + WireNumber(delta) + " " + WireNumber(30 * speed.Value / 100.0), cancellation.Token);
                nextInstruction = 0; code.ActiveLine = 0; progress.Value = 0;
                SetState("EJE J" + (axis + 1) + " · MOVIMIENTO COMPLETADO");
            }
            catch (OperationCanceledException) { Log("Movimiento articular interrumpido."); }
            catch (Exception ex) { await FailMotionAsync(ex); }
            finally { cancellation.Dispose(); if (execution == cancellation) execution = null; moving = false; UpdateControls(); }
        }

        private async Task StopAllAsync()
        {
            stopped = true; motors = false; pauseRequested = false; nextInstruction = 0;
            if (execution != null) execution.Cancel();
            SetState("PARADA ACTIVADA");
            if (transport != null) try { await transport.StopAsync(); } catch (Exception ex) { Log("Parada: " + ex.Message); }
            progress.Value = 0; Log("Parada de software activada. Rearma y habilita para continuar."); UpdateControls();
        }

        private async Task RearmAsync()
        {
            if (moving || busy) return;
            try { if (Ready()) await transport.ExecuteAsync("RESET", CancellationToken.None); stopped = false; motors = false; SetState("PARADA REARMADA"); Log("Parada rearmada. Los movimientos siguen deshabilitados."); }
            catch (Exception ex) { Report(ex); }
            UpdateControls();
        }

        private async Task FailMotionAsync(Exception ex) { await StopAllAsync(); Report(ex); }
        private bool Ready() { return transport != null && transport.IsConnected && transport.CanMove; }
        private bool CartesianReady() { return Ready() && transport.CartesianPoseKnown; }
        private RobotPoint FindPoint(string name) { return project.Points.First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); }

        private void ShowPose(RobotPose pose)
        {
            var values = new[] { pose.X, pose.Y, pose.Z, pose.A, pose.B, pose.C };
            for (int i = 0; i < values.Length; i++) axisValues[i].Text = values[i].ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void UpdateControls()
        {
            bool connected = transport != null && transport.IsConnected; bool canMove = Ready();
            connect.Text = connected ? "Desconectar" : "Conectar"; connect.Enabled = !busy && !moving;
            diagnostic.Enabled = connected && !canMove && !busy && !moving;
            mode.Enabled = !connected && !busy && !moving; port.Enabled = mode.SelectedIndex == 1 && mode.Enabled; baud.Enabled = port.Enabled;
            connectionState.Text = connected ? (canMove ? "●  Conectado" : "●  Diagnóstico") : "●  Sin conexión";
            connectionState.ForeColor = connected ? Theme.Accent : Theme.Muted;
            sessionState.Text = mode.SelectedIndex == 0 ? "SIMULACIÓN" : "USB / DIAGNÓSTICO";
            bool cartesianKnown = canMove && transport.CartesianPoseKnown;
            bool jointsKnown = canMove && transport.JointsKnown;
            positionLabel.Text = mode.SelectedIndex != 0 ? "USB · sin telemetría de posición" : motionTabs.SelectedIndex == 1 ? "Articulaciones simuladas · grados" : cartesianKnown ? "Posición simulada · base / tool0" : "XYZ no calculado · inicia práctica XYZ";
            if (!cartesianKnown) { foreach (var value in axisValues) if (value != null) value.Text = "—"; }
            else ShowPose(transport.CurrentPose);
            enable.Enabled = canMove && !stopped && !busy && !moving; enable.Text = motors ? "●  Movimientos habilitados" : "Habilitar movimientos";
            enable.ForeColor = motors ? Theme.Accent : Theme.Text;
            bool permitted = canMove && motors && !stopped && !busy && !moving;
            play.Enabled = (permitted && cartesianKnown) || (execution != null && pauseRequested && !stopped); play.Text = pauseRequested ? "▶  Continuar" : "▶  Ejecutar";
            step.Enabled = permitted && cartesianKnown; pause.Enabled = execution != null && moving && !pauseRequested && !stopped;
            reset.Enabled = stopped && !moving && !busy; code.Editor.ReadOnly = moving || busy;
            increment.Enabled = !moving && !busy; speed.Enabled = !moving && !busy;
            foreach (var button in jogButtons) button.Enabled = permitted && cartesianKnown;
            jointPanel.SetState(canMove ? transport.CurrentJoints : null, jointsKnown, permitted && jointsKnown, permitted);
            cartesianPractice.Enabled = permitted;
            cartesianPractice.Text = cartesianKnown ? "Reiniciar práctica XYZ" : "Iniciar práctica XYZ";
            foreach (var control in projectControls) control.Enabled = !moving && !busy;
            var teach = FindControl(this, "TeachButton"); if (teach != null) teach.Enabled = cartesianKnown && !moving && !busy;
            footer.Text = "SESIÓN LOCAL     /     " + (connected ? transport.Name : "Sin conexión") + "     /     mm · grados     /     tool0";
        }

        private void RefreshPoints()
        {
            pointGrid.Rows.Clear(); foreach (var point in project.Points)
            {
                var pose = point.Pose; pointGrid.Rows.Add(point.Name, Number(pose.X), Number(pose.Y), Number(pose.Z), Number(pose.A), Number(pose.B), Number(pose.C));
            }
        }

        private void TeachPoint()
        {
            if (!CartesianReady() || moving || busy) return;
            using (var dialog = new PointDialog(new RobotPoint(NextPointName(), transport.CurrentPose.Clone()), true))
            { if (dialog.ShowDialog(this) == DialogResult.OK) AddOrReplacePoint(dialog.Point, null); }
        }

        private void EditPoint(bool create)
        {
            if (moving || busy) return; var selected = SelectedPoint(); if (!create && selected == null) return;
            var original = create ? new RobotPoint(NextPointName(), new RobotPose(0, 0, 200, 0, 0, 0)) : new RobotPoint(selected.Name, selected.Pose.Clone());
            using (var dialog = new PointDialog(original, create)) { if (dialog.ShowDialog(this) == DialogResult.OK) AddOrReplacePoint(dialog.Point, create ? null : selected); }
        }

        private void AddOrReplacePoint(RobotPoint point, RobotPoint old)
        {
            if (project.Points.Any(p => p != old && string.Equals(p.Name, point.Name, StringComparison.OrdinalIgnoreCase))) { Log("Ya existe un punto llamado " + point.Name + "."); bottomTabs.SelectedIndex = 1; return; }
            if (old == null) project.Points.Add(point); else { old.Pose = point.Pose; } // Editing preserves the identifier used in the program.
            dirty = true; nextInstruction = 0; RefreshPoints(); UpdateTitle(); Log("Punto guardado: " + point.Name + ".");
        }

        private RobotPoint SelectedPoint() { return pointGrid.CurrentRow == null ? null : project.Points.FirstOrDefault(p => p.Name == Convert.ToString(pointGrid.CurrentRow.Cells[0].Value)); }
        private string NextPointName() { int i = 1; while (project.Points.Any(p => string.Equals(p.Name, "p" + i, StringComparison.OrdinalIgnoreCase))) i++; return "p" + i; }
        private void DeletePoint()
        {
            var point = SelectedPoint(); if (point == null) return;
            if (System.Text.RegularExpressions.Regex.IsMatch(code.Source, @"\b" + System.Text.RegularExpressions.Regex.Escape(point.Name) + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) { Log("El punto " + point.Name + " está referenciado en el programa. Retira esa referencia antes de eliminarlo."); bottomTabs.SelectedIndex = 1; return; }
            project.Points.Remove(point); dirty = true; nextInstruction = 0; RefreshPoints(); UpdateTitle();
        }

        private void InsertInstruction(bool circular)
        {
            string selected = SelectedPoint() == null ? "p1" : SelectedPoint().Name;
            code.Editor.SelectedText = circular ? "MoveC pVia, " + selected + ", v50, fine, tool0;\n" : "MoveL " + selected + ", v50, fine, tool0;\n";
            code.Editor.Focus();
        }

        private void OpenProject()
        {
            if (moving || busy || !ConfirmDiscard()) return;
            using (var dialog = new OpenFileDialog { Filter = "Proyecto ArmStudio (*.armstudio)|*.armstudio|JSON (*.json)|*.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { var loaded = ProjectStore.Load(dialog.FileName); loading = true; project = loaded; code.Source = loaded.Source; currentPath = dialog.FileName; dirty = false; nextInstruction = 0; code.ActiveLine = 0; progress.Value = 0; RefreshPoints(); UpdateTitle(); Log("Proyecto abierto: " + project.Name); }
                catch (Exception ex) { Report(ex); } finally { loading = false; UpdateControls(); }
            }
        }

        private bool SaveProject(bool saveAs)
        {
            if (moving || busy) return false;
            if (currentPath == null || saveAs)
            {
                using (var dialog = new SaveFileDialog { Filter = "Proyecto ArmStudio (*.armstudio)|*.armstudio", FileName = project.Name + ".armstudio", DefaultExt = "armstudio" })
                { if (dialog.ShowDialog(this) != DialogResult.OK) return false; currentPath = dialog.FileName; }
            }
            try { project.Source = code.Source; ProjectStore.Save(currentPath, project); dirty = false; UpdateTitle(); Log("Proyecto guardado: " + Path.GetFileName(currentPath)); return true; }
            catch (Exception ex) { Report(ex); return false; }
        }

        private bool ConfirmDiscard()
        {
            if (!dirty) return true;
            var answer = MessageBox.Show(this, "Hay cambios sin guardar. ¿Quieres guardarlos?", "Proyecto modificado", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            return answer == DialogResult.No || (answer == DialogResult.Yes && SaveProject(false));
        }

        private async void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (closing) return;
            // Stop before the save dialog; a modal dialog must never leave a program running.
            e.Cancel = true;
            if (connectionOperation != null) connectionOperation.Cancel();
            await StopAllAsync();
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while ((execution != null || busy) && DateTime.UtcNow < deadline) await Task.Delay(20);
            if (execution != null || busy) { Log("La cancelación sigue pendiente. Espera un momento y vuelve a cerrar."); return; }
            if (!ConfirmDiscard()) return;
            closing = true;
            try { if (transport != null) { await transport.DisconnectAsync(); transport.Dispose(); } }
            catch (Exception ex) { Log("Cierre del puerto: " + ex.Message); }
            Close();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; _ = StopAllAsync(); }
            if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; SaveProject(e.Shift); }
            if (e.Control && e.KeyCode == Keys.O) { e.SuppressKeyPress = true; OpenProject(); }
        }

        private void ExportLog()
        {
            using (var dialog = new SaveFileDialog { Filter = "Registro de texto (*.log)|*.log", FileName = "ArmStudio-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".log" })
            { if (dialog.ShowDialog(this) == DialogResult.OK) try { File.WriteAllLines(dialog.FileName, logLines); } catch (Exception ex) { Report(ex); } }
        }

        private void RefreshPorts()
        {
            try { var previous = port.Text; port.Items.Clear(); port.Items.AddRange(SerialPort.GetPortNames().OrderBy(p => p).ToArray()); if (port.Items.Count > 0) port.SelectedIndex = Math.Max(0, port.Items.IndexOf(previous)); }
            catch (Exception ex) { Log("No se pudieron enumerar los puertos: " + ex.Message); }
        }

        private void ShowHelp()
        {
            MessageBox.Show(this, "PRIMERA PRÁCTICA\n\n1. Selecciona Simulación y pulsa Conectar.\n2. Pulsa Habilitar movimientos.\n3. Valida el programa y usa Paso a paso o Ejecutar.\n4. Pausa al final termina la instrucción actual. Esc activa la parada de software.\n5. Rearmar parada no habilita movimientos automáticamente.\n\nPROGRAMACIÓN\nMoveL p1, v50, fine, tool0;\nMoveC pVia, p2, v50, z0, tool0;\nWaitTime 0.5;\n\nXYZ en mm; ABC en grados Euler de práctica. Los ángulos se interpolan por componente. fine y z0 se detienen exactamente en el punto. No se admiten bucles, blending ni RAPID completo.\n\nUSB / SERIE\nCarga ArmStudioDiagnostic.ino en una placa compatible y usa 115200 baudios. Esta versión verifica la conexión y no controla motores reales.\n\nCtrl+S: guardar · Ctrl+O: abrir · Esc: parar", "Guía rápida · ArmStudio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Log(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text; logLines.Add(line);
            if (logLines.Count > 600) logLines.RemoveAt(0);
            eventLog.Lines = logLines.ToArray(); eventLog.SelectionStart = eventLog.TextLength; eventLog.ScrollToCaret();
            if (transport != null && !transport.IsConnected && motors) { motors = false; UpdateControls(); }
        }
        private void Report(Exception ex) { Log("ERROR · " + ex.Message); bottomTabs.SelectedIndex = 1; SetState("REVISAR EVENTOS"); }
        private void SetState(string text) { runState.Text = "  " + text; }
        private void UpdateTitle() { fileLabel.Text = (currentPath == null ? project.Name + ".armstudio" : Path.GetFileName(currentPath)) + (dirty ? " *" : ""); Text = "ArmStudio | " + project.Name + (dirty ? " *" : ""); }
        private void Post(Action action) { if (IsDisposed || Disposing || !IsHandleCreated) return; if (InvokeRequired) { try { BeginInvoke(action); } catch (InvalidOperationException) { } } else action(); }
        private static string Number(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        private static string WireNumber(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string PoseText(RobotPose pose) { return string.Join(" ", new[] { pose.X, pose.Y, pose.Z, pose.A, pose.B, pose.C }.Select(WireNumber)); }

        private static ComboBox Combo(string[] items, int width)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, BackColor = Theme.Raised, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Font = Theme.Ui(10), Margin = new Padding(4, 4, 4, 0) };
            box.Items.AddRange(items); if (items.Length > 0) box.SelectedIndex = 0; return box;
        }
        private static FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty }; }
        private static TableLayoutPanel Rows(params float[] heights)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length, Margin = Padding.Empty, Padding = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); foreach (var height in heights) panel.RowStyles.Add(new RowStyle(height < 0 ? SizeType.Percent : SizeType.Absolute, height < 0 ? 100 : height)); return panel;
        }
        private void AddNav(FlowLayoutPanel panel, string text, Action click, bool selected = false) { var button = Theme.Button(text, selected); button.Width = 144; button.Height = 43; button.Margin = new Padding(0, 3, 0, 3); button.Font = Theme.Ui(9); button.TextAlign = ContentAlignment.MiddleLeft; button.Click += delegate { click(); }; panel.Controls.Add(button); }
        private void AddProjectButton(FlowLayoutPanel panel, string text, Action click, int width) { var button = Theme.Button(text); button.Width = width; button.Height = 30; button.Click += delegate { if (!moving && !busy) click(); }; panel.Controls.Add(button); projectControls.Add(button); }
        private static Control FindControl(Control root, string name) { if (root.Name == name) return root; foreach (Control control in root.Controls) { var found = FindControl(control, name); if (found != null) return found; } return null; }
    }
}
