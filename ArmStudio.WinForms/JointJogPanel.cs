using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ArmStudio.Core;

namespace ArmStudio.WinForms
{
    /// <summary>Incremental joint controls; displayed limits are a teaching profile.</summary>
    public sealed class JointJogPanel : UserControl
    {
        private readonly TextBox[] angles = new TextBox[6];
        private readonly Button[] negative = new Button[6];
        private readonly Button[] positive = new Button[6];
        private readonly Panel[] rowPanels = new Panel[6];
        private readonly ComboBox stepDegrees = new ComboBox();
        private readonly Button startPractice = Theme.Button("Iniciar práctica de ejes");
        private int selectedAxis;

        public event Action<int, int> JogRequested;
        public event Action StartPracticeRequested;

        public double StepDegrees
        {
            get { return double.Parse(Convert.ToString(stepDegrees.SelectedItem), CultureInfo.InvariantCulture); }
        }

        public int SelectedAxis { get { return selectedAxis; } }

        public JointJogPanel()
        {
            Name = "JointJogPanel";
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9);
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            MinimumSize = new Size(250, 250);
            DoubleBuffered = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            Controls.Add(layout);

            var headings = RowTable();
            headings.Controls.Add(Cell("Eje", Theme.Muted, 7), 0, 0);
            headings.Controls.Add(Cell("Mín. °", Theme.Muted, 7), 1, 0);
            headings.Controls.Add(Cell("Ángulo °", Theme.Muted, 7), 2, 0);
            headings.Controls.Add(Cell("Máx. °", Theme.Muted, 7), 3, 0);
            layout.Controls.Add(headings, 0, 0);

            var rows = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int axis = 0; axis < 6; axis++)
            {
                rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 6));
                int capturedAxis = axis;
                var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 1), Padding = Padding.Empty };
                rowPanels[axis] = panel;
                var row = RowTable();
                panel.Controls.Add(row);
                rows.Controls.Add(panel, 0, axis);
                var name = Cell("J" + (axis + 1), Theme.Accent, 8, FontStyle.Bold);
                row.Controls.Add(name, 0, 0);
                row.Controls.Add(Cell(JointState.Min(axis).ToString("0.00", CultureInfo.InvariantCulture), Theme.Muted, 7), 1, 0);
                angles[axis] = new TextBox
                {
                    Name = "JointAngle" + (axis + 1), ReadOnly = true, Text = "—",
                    Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Center,
                    BackColor = Theme.Raised, ForeColor = Theme.Text,
                    BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono(9),
                    TabStop = false, Margin = new Padding(2, 3, 2, 2),
                    AccessibleName = "Ángulo actual del eje J" + (axis + 1) + " en grados"
                };
                row.Controls.Add(angles[axis], 2, 0);
                row.Controls.Add(Cell(JointState.Max(axis).ToString("0.00", CultureInfo.InvariantCulture), Theme.Muted, 7), 3, 0);
                negative[axis] = AxisButton(axis, -1);
                positive[axis] = AxisButton(axis, 1);
                row.Controls.Add(negative[axis], 4, 0);
                row.Controls.Add(positive[axis], 5, 0);
                panel.Click += delegate { SelectAxis(capturedAxis); };
                row.Click += delegate { SelectAxis(capturedAxis); };
                foreach (Control control in row.Controls)
                    control.Click += delegate { SelectAxis(capturedAxis); };
            }
            layout.Controls.Add(rows, 0, 1);

            var stepRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, WrapContents = false,
                Margin = Padding.Empty, Padding = new Padding(0, 2, 0, 0)
            };
            var stepLabel = Cell("Paso", Theme.Muted, 8);
            stepLabel.AutoSize = true;
            stepLabel.Margin = new Padding(0, 5, 8, 0);
            stepRow.Controls.Add(stepLabel);
            stepDegrees.Name = "JointStepDegrees";
            stepDegrees.DropDownStyle = ComboBoxStyle.DropDownList;
            stepDegrees.BackColor = Theme.Raised; stepDegrees.ForeColor = Theme.Text;
            stepDegrees.FlatStyle = FlatStyle.Flat; stepDegrees.Font = Theme.Ui(9);
            stepDegrees.Width = 78; stepDegrees.Margin = new Padding(0, 1, 8, 0);
            stepDegrees.Items.AddRange(new object[] { "0.1", "0.5", "1", "5", "10" });
            stepDegrees.SelectedIndex = 2;
            stepRow.Controls.Add(stepDegrees);
            var degreesLabel = Cell("grados", Theme.Muted, 8);
            degreesLabel.AutoSize = true; degreesLabel.Margin = new Padding(0, 5, 0, 0);
            stepRow.Controls.Add(degreesLabel);
            layout.Controls.Add(stepRow, 0, 2);

            startPractice.Name = "StartJointPractice";
            startPractice.Dock = DockStyle.Fill;
            startPractice.Margin = new Padding(0, 2, 0, 0);
            startPractice.Font = Theme.Ui(8, FontStyle.Bold);
            startPractice.AccessibleDescription = "Inicia una práctica numérica independiente con los seis ángulos a cero.";
            startPractice.Click += delegate { var handler = StartPracticeRequested; if (handler != null) handler(); };
            layout.Controls.Add(startPractice, 0, 3);
            SelectAxis(0);
            SetState(null, false, false, false);
        }

        public void SetState(JointState joints, bool known, bool canJog, bool canStart)
        {
            bool showValues = known && joints != null;
            for (int axis = 0; axis < 6; axis++)
            {
                double angle = showValues ? joints.GetAxis(axis) : 0;
                angles[axis].Text = showValues ? angle.ToString("0.00", CultureInfo.InvariantCulture) : "—";
                angles[axis].ForeColor = showValues ? Theme.Text : Theme.Muted;
                negative[axis].Enabled = canJog && showValues && angle > JointState.Min(axis) + 1e-9;
                positive[axis].Enabled = canJog && showValues && angle < JointState.Max(axis) - 1e-9;
            }
            startPractice.Enabled = canStart;
            startPractice.Text = showValues ? "Reiniciar práctica de ejes" : "Iniciar práctica de ejes";
            stepDegrees.Enabled = canJog || canStart;
        }

        private Button AxisButton(int axis, int direction)
        {
            var button = Theme.Button(direction < 0 ? "<" : ">");
            button.Name = (direction < 0 ? "JointMinus" : "JointPlus") + (axis + 1);
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(1, 2, 1, 2);
            button.Padding = Padding.Empty;
            button.Font = Theme.Ui(8, FontStyle.Bold);
            button.AccessibleName = "Mover eje J" + (axis + 1) + (direction < 0 ? " en sentido negativo" : " en sentido positivo");
            button.Click += delegate
            {
                SelectAxis(axis);
                var handler = JogRequested;
                if (handler != null) handler(axis, direction);
            };
            return button;
        }

        private void SelectAxis(int axis)
        {
            selectedAxis = axis;
            for (int index = 0; index < rowPanels.Length; index++)
                if (rowPanels[index] != null)
                    rowPanels[index].BackColor = index == selectedAxis ? Color.FromArgb(30, 58, 58) : Theme.Surface;
        }

        private static TableLayoutPanel RowTable()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1,
                Margin = Padding.Empty, Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return row;
        }

        private static Label Cell(string text, Color color, float size, FontStyle style = FontStyle.Regular)
        {
            var label = Theme.Label(text, size, color, style);
            label.AutoSize = false; label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            return label;
        }
    }
}
