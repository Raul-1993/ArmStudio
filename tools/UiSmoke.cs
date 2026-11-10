using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using ArmStudio.Core;
using ArmStudio.WinForms;

internal static class UiSmoke
{
    private static int checks;
    private static int result;

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(50, 40) };
        form.Shown += async delegate
        {
            try { await Verify(form); await VerifyCloseDuringMotion(); }
            catch (Exception ex)
            {
                result = 1;
                Console.WriteLine("FAIL " + ex);
                Console.WriteLine(Field<RichTextBox>(form, "eventLog").Text);
            }
            finally
            {
                var transport = Field<IRobotTransport>(form, "transport");
                if (transport != null) { await transport.StopAsync(); await transport.DisconnectAsync(); transport.Dispose(); }
                form.Dispose();
                Application.ExitThread();
            }
        };
        Application.Run(form);
        Console.WriteLine("UI: " + checks + " comprobaciones; código " + result);
        return result;
    }

    private static async Task Verify(MainForm form)
    {
        var connect = Find<Button>(form, "ConnectButton");
        var enable = Find<Button>(form, "EnableButton");
        var step = Find<Button>(form, "StepButton");
        var play = Find<Button>(form, "PlayButton");
        var pause = Find<Button>(form, "PauseButton");
        var stop = Find<Button>(form, "StopButton");
        var reset = Find<Button>(form, "ResetButton");
        var mode = Find<ComboBox>(form, "ConnectionMode");
        var grid = Find<DataGridView>(form, "PointGrid");
        Check(connect.Enabled && !enable.Enabled && !step.Enabled && !play.Enabled, "Inicio desconectado sin movimientos habilitados");
        Check(grid.Rows.Count == 5, "Tabla inicial con cinco puntos de ejemplo");
        Field<TrackBar>(form, "speed").Value = 100;
        connect.PerformClick();
        await Until(() => !Field<bool>(form, "busy"), 3000);
        Check(Field<IRobotTransport>(form, "transport").IsConnected && enable.Enabled, "Botón Conectar inicia el simulador");
        Check(!play.Enabled && !step.Enabled && !mode.Enabled, "Conectar exige habilitar y bloquea cambio de modo");
        enable.PerformClick();
        await Until(() => !Field<bool>(form, "busy"), 3000);
        Check(play.Enabled && step.Enabled && Field<bool>(form, "motors"), "Habilitar permite ejecutar");
        step.PerformClick();
        await Until(() => !Field<bool>(form, "moving"), 3000);
        Check(Field<int>(form, "nextInstruction") == 1, "Paso a paso completa exactamente una instrucción");
        Field<TabControl>(form, "bottomTabs").SelectedIndex = 0;
        await Task.Delay(350);
        using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        {
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, bitmap.Size);
            bitmap.Save(PreviewPath, ImageFormat.Png);
        }
        Console.WriteLine("Vista nativa: " + PreviewPath + " (" + form.ClientSize.Width + "×" + form.ClientSize.Height + ")");

        play.PerformClick();
        await Until(() => Field<bool>(form, "moving"), 1000);
        Check(pause.Enabled && !step.Enabled && !enable.Enabled, "Durante ejecución se bloquean controles incompatibles");
        pause.PerformClick();
        await Until(() => Field<Label>(form, "runState").Text.Contains("EN PAUSA"), 6000);
        Check(Field<int>(form, "nextInstruction") == 2 && play.Enabled, "Pausa espera el final de la instrucción actual");
        var pausedPose = Field<IRobotTransport>(form, "transport").CurrentPose;
        await Task.Delay(250);
        Check(Field<int>(form, "nextInstruction") == 2 &&
            Math.Abs(Field<IRobotTransport>(form, "transport").CurrentPose.X - pausedPose.X) < 1e-8,
            "Durante pausa el puntero y la posición permanecen estables");
        play.PerformClick();
        await Until(() => !Field<bool>(form, "moving"), 15000);
        var transport = Field<IRobotTransport>(form, "transport");
        Check(Field<int>(form, "nextInstruction") == 6 && Field<Label>(form, "runState").Text.Contains("COMPLETADO"), "Continuar termina las seis instrucciones del ejemplo");
        Check(Math.Abs(transport.CurrentPose.X) < 1e-8 && Math.Abs(transport.CurrentPose.Y) < 1e-8 && Math.Abs(transport.CurrentPose.Z - 200) < 1e-8,
            "El programa completo devuelve el simulador a pHome");

        play.PerformClick();
        await Task.Delay(100);
        stop.PerformClick();
        await Until(() => !Field<bool>(form, "moving"), 3000);
        Check(Field<bool>(form, "stopped") && !Field<bool>(form, "motors") && !play.Enabled && reset.Enabled,
            "La parada interrumpe y exige rearme");
        reset.PerformClick();
        await Until(() => !Field<bool>(form, "stopped"), 3000);
        Check(!Field<bool>(form, "motors") && !play.Enabled && enable.Enabled, "Rearmar deja los movimientos deshabilitados");
        var beforeJog = transport.CurrentPose.X;
        enable.PerformClick();
        await Until(() => !Field<bool>(form, "busy"), 3000);
        Field<List<Button>>(form, "jogButtons")[1].PerformClick();
        await Until(() => !Field<bool>(form, "moving"), 3000);
        Check(Math.Abs(transport.CurrentPose.X - beforeJog - 5) < 1e-8 && Field<int>(form, "nextInstruction") == 0,
            "El botón manual X+ respeta el incremento de 5 mm y reinicia el puntero");
        await VerifyJointPanel(form);
        connect.PerformClick();
        await Until(() => Field<IRobotTransport>(form, "transport") == null && !Field<bool>(form, "busy"), 3000);
        mode.SelectedIndex = 1;
        Check(!enable.Enabled && !play.Enabled && mode.Enabled, "USB de diagnóstico no permite movimiento");
        var values = Field<Label[]>(form, "axisValues");
        Check(Array.TrueForAll(values, label => label.Text == "—"), "En modo USB no se muestran coordenadas simuladas como telemetría");
        Check(!Find<Button>(form, "StartJointPractice").Enabled && !Find<Button>(form, "JointPlus1").Enabled &&
            Find<TextBox>(form, "JointAngle1").Text == "—", "USB no muestra articulaciones simuladas ni permite moverlas");
        mode.SelectedIndex = 0;
        Check(!enable.Enabled && !play.Enabled, "Volver a simulación también requiere conectar");
    }

    private static async Task VerifyJointPanel(MainForm form)
    {
        var transport = Field<IRobotTransport>(form, "transport");
        var tabs = Field<TabControl>(form, "motionTabs");
        var start = Find<Button>(form, "StartJointPractice");
        var play = Find<Button>(form, "PlayButton");
        var stop = Find<Button>(form, "StopButton");
        var reset = Find<Button>(form, "ResetButton");
        var enable = Find<Button>(form, "EnableButton");
        tabs.SelectedIndex = 1;
        Check(start.Enabled && !Find<Button>(form, "JointPlus1").Enabled &&
            Find<TextBox>(form, "JointAngle1").Text == "—", "La práctica articular requiere inicialización explícita");
        start.PerformClick();
        await Until(() => !Field<bool>(form, "busy"), 3000);
        bool allZero = transport.JointsKnown;
        for (int i = 0; i < 6; i++) allZero &= Math.Abs(transport.CurrentJoints.GetAxis(i)) < 1e-8;
        Check(allZero && !transport.CartesianPoseKnown, "Iniciar ejes pone J1–J6 a cero y deja el TCP sin calcular");
        Check(!play.Enabled && !Find<Button>(form, "TeachButton").Enabled &&
            Array.TrueForAll(Field<Label[]>(form, "axisValues"), value => value.Text == "—"),
            "La práctica de ejes no inventa pose cartesiana ni permite enseñarla");
        Find<Button>(form, "JointPlus1").PerformClick();
        await Until(() => !Field<bool>(form, "moving"), 3000);
        bool untouched = true;
        for (int i = 1; i < 6; i++) untouched &= Math.Abs(transport.CurrentJoints.GetAxis(i)) < 1e-8;
        Check(Math.Abs(transport.CurrentJoints.GetAxis(0) - 1) < 1e-8 && untouched,
            "J1+ incrementa un grado sin mover los otros cinco ejes");
        var increment = Find<ComboBox>(form, "JointStepDegrees");
        increment.SelectedItem = "0.5";
        Find<Button>(form, "JointMinus2").PerformClick();
        await Until(() => !Field<bool>(form, "moving"), 3000);
        Check(Math.Abs(transport.CurrentJoints.GetAxis(1) + .5) < 1e-8 &&
            Find<TextBox>(form, "JointAngle2").Text == "-0.50", "El paso de 0.5 grados funciona en sentido negativo");

        await transport.ExecuteAsync("JOG_J 1 163.75 180", System.Threading.CancellationToken.None);
        await Task.Delay(50);
        increment.SelectedItem = "5";
        Find<Button>(form, "JointPlus1").PerformClick();
        await Until(() => !Field<bool>(form, "moving"), 3000);
        Check(Math.Abs(transport.CurrentJoints.GetAxis(0) - 165) < 1e-8 &&
            !Find<Button>(form, "JointPlus1").Enabled && Find<Button>(form, "JointMinus1").Enabled,
            "El incremento se recorta exactamente al límite J1 de 165 grados");
        await transport.ExecuteAsync("JOG_J 3 70 180", System.Threading.CancellationToken.None);
        await Task.Delay(50);
        Check(!Find<Button>(form, "JointPlus3").Enabled && Find<Button>(form, "JointMinus3").Enabled,
            "El límite asimétrico de J3 bloquea solo el sentido positivo a 70 grados");
        await transport.ExecuteAsync("JOG_J 6 399.75 180", System.Threading.CancellationToken.None);
        await Task.Delay(50);
        Find<Button>(form, "JointPlus6").PerformClick();
        await Until(() => !Field<bool>(form, "moving"), 3000);
        Check(Math.Abs(transport.CurrentJoints.GetAxis(5) - 400) < 1e-8 &&
            Find<TextBox>(form, "JointAngle6").Text == "400.00" && !Find<Button>(form, "JointPlus6").Enabled,
            "J6 llega a 400 grados sin normalizar ni sobrepasar su límite");
        await Task.Delay(200);
        Capture(form, Path.Combine(Path.GetDirectoryName(PreviewPath), "preview-joints.png"));

        Field<TrackBar>(form, "speed").Value = 10;
        increment.SelectedItem = "10";
        Find<Button>(form, "JointMinus6").PerformClick();
        await Until(() => Field<bool>(form, "moving"), 1000);
        Check(!start.Enabled && !Find<Button>(form, "JointPlus2").Enabled,
            "Durante un movimiento articular se bloquean reinicio y otros ejes");
        await Task.Delay(100);
        stop.PerformClick();
        await Until(() => !Field<bool>(form, "moving"), 3000);
        var stopped = transport.CurrentJoints.GetAxis(5);
        await Task.Delay(100);
        Check(Field<bool>(form, "stopped") && !Field<bool>(form, "motors") &&
            Math.Abs(transport.CurrentJoints.GetAxis(5) - stopped) < 1e-8 && !Find<Button>(form, "JointMinus6").Enabled,
            "La parada articular cancela, conserva el ángulo y deshabilita movimiento");
        reset.PerformClick();
        await Until(() => !Field<bool>(form, "stopped"), 3000);
        Check(!start.Enabled && !Find<Button>(form, "JointPlus1").Enabled && enable.Enabled,
            "Rearmar la parada articular exige habilitar de nuevo");
        enable.PerformClick();
        await Until(() => !Field<bool>(form, "busy"), 3000);
        tabs.SelectedIndex = 0;
        Find<Button>(form, "StartCartesianPractice").PerformClick();
        await Until(() => !Field<bool>(form, "busy"), 3000);
        Check(transport.CartesianPoseKnown && !transport.JointsKnown && play.Enabled &&
            Math.Abs(transport.CurrentPose.Z - 200) < 1e-8 && Math.Abs(transport.CurrentPose.X) < 1e-8,
            "La práctica XYZ restaura su origen e invalida las articulaciones desconocidas");
        Check(Find<TextBox>(form, "JointAngle1").Text == "—" && Find<TextBox>(form, "JointAngle6").Text == "—",
            "Volver a XYZ retira ángulos articulares que ya no se pueden calcular");
        Field<TrackBar>(form, "speed").Value = 100;
    }

    private static void Capture(MainForm form, string path)
    {
        using (var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        {
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, bitmap.Size);
            bitmap.Save(path, ImageFormat.Png);
        }
        Console.WriteLine("Vista nativa: " + path);
    }

    private static async Task VerifyCloseDuringMotion()
    {
        var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(60, 50) };
        form.Show();
        try
        {
            await Task.Delay(100);
            Find<Button>(form, "ConnectButton").PerformClick();
            await Until(() => !Field<bool>(form, "busy"), 3000);
            Find<Button>(form, "EnableButton").PerformClick();
            await Until(() => !Field<bool>(form, "busy"), 3000);
            var transport = Field<IRobotTransport>(form, "transport");
            Find<Button>(form, "PlayButton").PerformClick();
            await Until(() => Field<bool>(form, "moving") && transport.CurrentPose.X > 0, 3000);
            form.Close();
            await Until(() => form.IsDisposed, 5000);
            Check(!transport.IsConnected, "Cerrar durante un movimiento cancela y desconecta sin requerir un segundo cierre");
            var stopped = transport.CurrentPose.X;
            await Task.Delay(100);
            Check(Math.Abs(transport.CurrentPose.X - stopped) < 1e-8, "La posición deja de cambiar tras cerrar el formulario");
        }
        finally { if (!form.IsDisposed) form.Dispose(); }
    }

    private static T Field<T>(MainForm form, string name)
    { return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
    private static T Find<T>(Control parent, string name) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child.Name == name) return (T)child;
            var nested = Find<T>(child, name); if (nested != null) return nested;
        }
        return null;
    }
    private static string PreviewPath
    {
        get { return Environment.GetEnvironmentVariable("ARMSTUDIO_PREVIEW") ?? Path.Combine(Environment.CurrentDirectory, "preview.png"); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
        Console.WriteLine("OK " + message);
    }
    private static async Task Until(Func<bool> condition, int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > milliseconds) throw new TimeoutException("La UI no llegó al estado esperado en " + milliseconds + " ms.");
            await Task.Delay(20);
        }
    }
}
