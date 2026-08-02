using AutoHdrProcessTool.Localization;

namespace AutoHdrProcessTool.Models
{
    internal sealed class ProcessEntry
    {
        public int ProcessId { get; set; }

        public string ProcessName { get; set; }

        public string ImagePath { get; set; }

        public bool PathAvailable { get; set; }

        public string AutoHdrStatus { get; set; }

        public string DisplayPath
        {
            get { return PathAvailable ? ImagePath : Strings.Get("ProcessPathUnavailable"); }
        }
    }
}
