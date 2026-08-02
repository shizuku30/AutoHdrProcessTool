using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutoHdrProcessTool.UI
{
    internal static class ThemeManager
    {
        private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmUseImmersiveDarkMode = 20;

        public static ThemePalette Current { get; private set; }

        public static bool IsSystemDark()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                false))
            {
                object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                return value is int && (int)value == 0;
            }
        }

        public static void Apply(Form form, bool darkMode)
        {
            Current = darkMode ? ThemePalette.Dark : ThemePalette.Light;
            form.SuspendLayout();
            try
            {
                ApplyControl(form, Current);
                ApplyTitleBar(form, darkMode);
                form.Invalidate(true);
            }
            finally
            {
                form.ResumeLayout(true);
            }
        }

        private static void ApplyControl(Control control, ThemePalette palette)
        {
            control.ForeColor = palette.Text;

            if (control is Form)
            {
                control.BackColor = palette.Background;
            }
            else if (control is GroupBox)
            {
                control.BackColor = palette.Surface;
                control.ForeColor = palette.Text;
            }
            else if (control is Panel || control is TableLayoutPanel || control is FlowLayoutPanel || control is SplitContainer)
            {
                control.BackColor = string.Equals(control.Tag as string, "surface", StringComparison.Ordinal)
                    ? palette.Surface
                    : palette.Background;
            }
            else if (control is TextBoxBase || control is NumericUpDown || control is ComboBox)
            {
                control.BackColor = palette.Control;
                control.ForeColor = palette.Text;
            }
            else if (control is Button)
            {
                ApplyButton((Button)control, palette);
            }
            else if (control is CheckBox)
            {
                var checkBox = (CheckBox)control;
                if (checkBox.Appearance == Appearance.Button)
                {
                    checkBox.FlatStyle = FlatStyle.Flat;
                    checkBox.FlatAppearance.BorderColor = palette.Border;
                    checkBox.FlatAppearance.CheckedBackColor = palette.ControlActive;
                    checkBox.BackColor = palette.Control;
                }
                else
                {
                    checkBox.BackColor = control.Parent == null ? palette.Background : control.Parent.BackColor;
                }
            }
            else if (control is Label || control is TrackBar)
            {
                control.BackColor = control.Parent == null ? palette.Background : control.Parent.BackColor;
            }
            else if (control is DataGridView)
            {
                ApplyGrid((DataGridView)control, palette);
            }
            else if (control is StatusStrip)
            {
                var status = (StatusStrip)control;
                status.BackColor = palette.Surface;
                status.ForeColor = palette.MutedText;
                status.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable(palette));
            }

            if (string.Equals(control.Tag as string, "muted", StringComparison.Ordinal))
            {
                control.ForeColor = palette.MutedText;
            }

            foreach (Control child in control.Controls)
            {
                ApplyControl(child, palette);
            }
        }

        private static void ApplyButton(Button button, ThemePalette palette)
        {
            bool accent = string.Equals(button.Tag as string, "accent", StringComparison.Ordinal);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = accent ? palette.Accent : palette.Border;
            button.FlatAppearance.MouseOverBackColor = accent ? palette.AccentHover : palette.ControlHover;
            button.FlatAppearance.MouseDownBackColor = accent ? palette.AccentPressed : palette.ControlActive;
            button.BackColor = accent ? palette.Accent : palette.Control;
            button.ForeColor = accent ? Color.White : palette.Text;
        }

        private static void ApplyGrid(DataGridView grid, ThemePalette palette)
        {
            grid.BackgroundColor = palette.Surface;
            grid.GridColor = palette.Border;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Header;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Header;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
            grid.DefaultCellStyle.BackColor = palette.Surface;
            grid.DefaultCellStyle.ForeColor = palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
            grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
            grid.AlternatingRowsDefaultCellStyle.BackColor = palette.AlternateRow;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = palette.Text;
            grid.RowHeadersDefaultCellStyle.BackColor = palette.Surface;
            grid.RowHeadersDefaultCellStyle.ForeColor = palette.Text;
            string explorerTheme = palette.IsDark ? "DarkMode_Explorer" : "Explorer";
            if (grid.IsHandleCreated)
            {
                SetWindowTheme(grid.Handle, explorerTheme, null);
            }
            else
            {
                grid.HandleCreated += (sender, args) => SetWindowTheme(grid.Handle, explorerTheme, null);
            }
        }

        private static void ApplyTitleBar(Form form, bool darkMode)
        {
            if (!form.IsHandleCreated)
            {
                form.HandleCreated += (sender, args) => ApplyTitleBar(form, darkMode);
                return;
            }

            int enabled = darkMode ? 1 : 0;
            int result = DwmSetWindowAttribute(
                form.Handle,
                DwmUseImmersiveDarkMode,
                ref enabled,
                sizeof(int));
            if (result != 0)
            {
                DwmSetWindowAttribute(
                    form.Handle,
                    DwmUseImmersiveDarkModeBefore20H1,
                    ref enabled,
                    sizeof(int));
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);

        private sealed class ThemeColorTable : ProfessionalColorTable
        {
            private readonly ThemePalette palette;

            public ThemeColorTable(ThemePalette palette)
            {
                this.palette = palette;
                UseSystemColors = false;
            }

            public override Color ToolStripBorder { get { return palette.Border; } }

            public override Color ToolStripGradientBegin { get { return palette.Surface; } }

            public override Color ToolStripGradientMiddle { get { return palette.Surface; } }

            public override Color ToolStripGradientEnd { get { return palette.Surface; } }
        }
    }

    internal sealed class ThemePalette
    {
        public static readonly ThemePalette Dark = new ThemePalette(
            true,
            Color.FromArgb(24, 26, 28),
            Color.FromArgb(32, 35, 38),
            Color.FromArgb(43, 47, 51),
            Color.FromArgb(52, 57, 62),
            Color.FromArgb(62, 67, 72),
            Color.FromArgb(239, 242, 244),
            Color.FromArgb(171, 179, 185),
            Color.FromArgb(52, 60, 64),
            Color.FromArgb(37, 40, 43),
            Color.FromArgb(54, 170, 110),
            Color.FromArgb(48, 151, 98),
            Color.FromArgb(42, 132, 86),
            Color.FromArgb(57, 62, 67),
            Color.FromArgb(64, 105, 83),
            Color.White);

        public static readonly ThemePalette Light = new ThemePalette(
            false,
            Color.FromArgb(244, 246, 247),
            Color.White,
            Color.White,
            Color.FromArgb(237, 240, 242),
            Color.FromArgb(207, 213, 218),
            Color.FromArgb(31, 35, 38),
            Color.FromArgb(93, 101, 108),
            Color.FromArgb(232, 236, 238),
            Color.FromArgb(248, 249, 250),
            Color.FromArgb(22, 132, 83),
            Color.FromArgb(18, 116, 72),
            Color.FromArgb(15, 101, 63),
            Color.FromArgb(225, 229, 232),
            Color.FromArgb(210, 239, 224),
            Color.FromArgb(20, 50, 36));

        private ThemePalette(
            bool isDark,
            Color background,
            Color surface,
            Color control,
            Color controlHover,
            Color border,
            Color text,
            Color mutedText,
            Color header,
            Color alternateRow,
            Color accent,
            Color accentHover,
            Color accentPressed,
            Color controlActive,
            Color selection,
            Color selectionText)
        {
            IsDark = isDark;
            Background = background;
            Surface = surface;
            Control = control;
            ControlHover = controlHover;
            Border = border;
            Text = text;
            MutedText = mutedText;
            Header = header;
            AlternateRow = alternateRow;
            Accent = accent;
            AccentHover = accentHover;
            AccentPressed = accentPressed;
            ControlActive = controlActive;
            Selection = selection;
            SelectionText = selectionText;
        }

        public bool IsDark { get; }
        public Color Background { get; }
        public Color Surface { get; }
        public Color Control { get; }
        public Color ControlHover { get; }
        public Color ControlActive { get; }
        public Color Border { get; }
        public Color Text { get; }
        public Color MutedText { get; }
        public Color Header { get; }
        public Color AlternateRow { get; }
        public Color Accent { get; }
        public Color AccentHover { get; }
        public Color AccentPressed { get; }
        public Color Selection { get; }
        public Color SelectionText { get; }
    }
}
