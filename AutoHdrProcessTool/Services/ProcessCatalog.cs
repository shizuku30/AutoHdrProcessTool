using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AutoHdrProcessTool.Localization;
using AutoHdrProcessTool.Models;
using Microsoft.Win32.SafeHandles;

namespace AutoHdrProcessTool.Services
{
    internal sealed class ProcessCatalog
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;

        public IList<ProcessEntry> GetProcesses()
        {
            var entries = new List<ProcessEntry>();
            Process[] processes = Process.GetProcesses();

            foreach (Process process in processes)
            {
                try
                {
                    string imagePath;
                    bool pathAvailable = TryGetImagePath(process.Id, out imagePath);
                    entries.Add(new ProcessEntry
                    {
                        ProcessId = process.Id,
                        ProcessName = SafeProcessName(process),
                        ImagePath = imagePath,
                        PathAvailable = pathAvailable,
                        AutoHdrStatus = pathAvailable
                            ? Strings.Get("StatusUnchecked")
                            : Strings.Get("StatusUnavailable")
                    });
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the snapshot was being built.
                }
                finally
                {
                    process.Dispose();
                }
            }

            entries.Sort((left, right) =>
            {
                int nameResult = string.Compare(left.ProcessName, right.ProcessName, StringComparison.CurrentCultureIgnoreCase);
                return nameResult != 0 ? nameResult : left.ProcessId.CompareTo(right.ProcessId);
            });
            return entries;
        }

        public bool TryGetImagePath(int processId, out string imagePath)
        {
            imagePath = null;
            using (SafeProcessHandle handle = OpenProcess(ProcessQueryLimitedInformation, false, processId))
            {
                if (handle.IsInvalid)
                {
                    return false;
                }

                int capacity = 32768;
                var buffer = new StringBuilder(capacity);
                if (!QueryFullProcessImageName(handle, 0, buffer, ref capacity))
                {
                    return false;
                }

                imagePath = buffer.ToString();
                return !string.IsNullOrWhiteSpace(imagePath);
            }
        }

        public void EnsureProcessMatches(int processId, string expectedImagePath)
        {
            string currentImagePath;
            if (!TryGetImagePath(processId, out currentImagePath))
            {
                throw new InvalidOperationException(Strings.Get("ErrorProcessExitedOrDenied"));
            }

            if (!string.Equals(currentImagePath, expectedImagePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Strings.Get("ErrorPidReused"));
            }
        }

        private static string SafeProcessName(Process process)
        {
            try
            {
                return process.ProcessName;
            }
            catch (Win32Exception)
            {
                return Strings.Get("UnknownProcess");
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(
            uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(
            SafeProcessHandle process,
            int flags,
            StringBuilder imagePath,
            ref int size);
    }
}
