using System;
using System.Globalization;
using System.Windows.Forms;
using AutoHdrProcessTool.Localization;

namespace AutoHdrProcessTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Strings.Initialize(CultureInfo.CurrentUICulture);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
