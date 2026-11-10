using System;
using System.Drawing;
using System.Windows.Forms;
using ArmStudio.Core;

namespace ArmStudio.WinForms
{
    internal sealed class PointDialog : Form
    {
        private readonly TextBox identifier = new TextBox();
        private readonly NumericUpDown[] coordinates = new NumericUpDown[6];
        internal RobotPoint Point { get; private set; }
        internal PointDialog(RobotPoint original, bool isNew)
        {
            Text = isNew ? "Nuevo punto" : "Editar " + original.Name; ClientSize = new Size(390, 410);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
            BackColor = Theme.Surface; ForeColor = Theme.Text; Font = Theme.Ui(); Padding = new Padding(22);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            for (int i = 0; i < 9; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 9));
            grid.Controls.Add(Theme.Label("Identificador"), 0, 0); identifier.Text = original.Name; identifier.ReadOnly = !isNew; identifier.Dock = DockStyle.Fill; identifier.BackColor = Theme.Raised; identifier.ForeColor = Theme.Text; grid.Controls.Add(identifier, 1, 0);
            var values = new[] { original.Pose.X, original.Pose.Y, original.Pose.Z, original.Pose.A, original.Pose.B, original.Pose.C };
            for (int i = 0; i < 6; i++)
            {
                grid.Controls.Add(Theme.Label("XYZABC"[i] + (i < 3 ? "  [mm]" : "  [°]")), 0, i + 1);
                coordinates[i] = new NumericUpDown { DecimalPlaces = 2, Minimum = i < 3 ? -500 : -180, Maximum = i < 3 ? 500 : 180, Increment = 1, Dock = DockStyle.Fill, BackColor = Theme.Raised, ForeColor = Theme.Text, Value = (decimal)values[i] }; grid.Controls.Add(coordinates[i], 1, i + 1);
            }
            var hint = Theme.Label("Límites de práctica, pendientes de la mecánica real.", 8, Theme.Muted); hint.Dock = DockStyle.Fill; grid.Controls.Add(hint, 0, 7); grid.SetColumnSpan(hint, 2);
            var cancel = Theme.Button("Cancelar"); cancel.Dock = DockStyle.Fill; cancel.DialogResult = DialogResult.Cancel; grid.Controls.Add(cancel, 0, 8);
            var save = Theme.Button("Guardar punto", true); save.Dock = DockStyle.Fill; save.Click += delegate
            {
                string name = identifier.Text.Trim();
                if (!ProgramParser.IsValidIdentifier(name)) { MessageBox.Show(this, "Usa letras, números y guion bajo. Empieza con una letra o guion bajo. Máximo 64 caracteres."); return; }
                Point = new RobotPoint(name, new RobotPose((double)coordinates[0].Value, (double)coordinates[1].Value, (double)coordinates[2].Value, (double)coordinates[3].Value, (double)coordinates[4].Value, (double)coordinates[5].Value)); DialogResult = DialogResult.OK; Close();
            }; grid.Controls.Add(save, 1, 8); Controls.Add(grid); AcceptButton = save; CancelButton = cancel;
        }
    }
}
