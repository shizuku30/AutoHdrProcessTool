using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using AutoHdrProcessTool.Localization;

namespace AutoHdrProcessTool.Services
{
    internal sealed class AutoHdrIntensityService
    {
        private const uint AutoHdrStrengthQuery = 11;
        private const uint LoadLibrarySearchSystem32 = 0x00000800;

        public bool TryGetCapability(out string message)
        {
            if (IntPtr.Size != 8)
            {
                message = Strings.Get("CapabilityX64Required");
                return false;
            }

            string helperPath = Path.Combine(Environment.SystemDirectory, "directxdatabasehelper.dll");
            if (!File.Exists(helperPath))
            {
                message = Strings.Get("CapabilityHelperMissing");
                return false;
            }

            IntPtr module = IntPtr.Zero;
            try
            {
                module = LoadSystemHelper();
                if (GetProcAddress(module, new IntPtr(101)) == IntPtr.Zero ||
                    GetProcAddress(module, new IntPtr(111)) == IntPtr.Zero)
                {
                    message = Strings.Get("CapabilityEntryPointsMissing");
                    return false;
                }

                string version = FileVersionInfo.GetVersionInfo(helperPath).FileVersion;
                message = string.IsNullOrWhiteSpace(version)
                    ? Strings.Get("CapabilityAvailable")
                    : Strings.Format("CapabilityAvailableVersion", version);
                return true;
            }
            catch (Exception exception) when (exception is Win32Exception || exception is BadImageFormatException)
            {
                message = Strings.Format("CapabilityLoadFailed", exception.Message);
                return false;
            }
            finally
            {
                if (module != IntPtr.Zero)
                {
                    FreeLibrary(module);
                }
            }
        }

        public uint GetLiveStrength(string imagePath)
        {
            Ensure64BitProcess();
            imagePath = NormalizeImagePath(imagePath);

            IntPtr module = LoadSystemHelper();
            IntPtr path = IntPtr.Zero;
            IntPtr output = IntPtr.Zero;
            try
            {
                QueryUserSettings query = GetOrdinalDelegate<QueryUserSettings>(module, 101);
                path = Marshal.StringToHGlobalUni(imagePath);
                output = Marshal.AllocHGlobal(sizeof(uint));
                Marshal.WriteInt32(output, 0);

                int result = query(AutoHdrStrengthQuery, path, IntPtr.Zero, output, sizeof(uint));
                ThrowOnFailure(result, "QueryUserSettings(ordinal 101)");
                uint strength = unchecked((uint)Marshal.ReadInt32(output));
                if (strength > 100)
                {
                    throw new InvalidOperationException(Strings.Get("ErrorReturnedStrengthOutOfRange"));
                }

                return strength;
            }
            finally
            {
                if (output != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(output);
                }

                if (path != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(path);
                }

                FreeLibrary(module);
            }
        }

        public void SetLiveStrength(string imagePath, int processId, uint strength)
        {
            Ensure64BitProcess();
            ValidateStrength(strength);
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processId));
            }

            imagePath = NormalizeImagePath(imagePath);
            IntPtr module = LoadSystemHelper();
            IntPtr path = IntPtr.Zero;
            IntPtr payload = IntPtr.Zero;
            try
            {
                WriteUserSetting write = GetOrdinalDelegate<WriteUserSetting>(module, 111);
                path = Marshal.StringToHGlobalUni(imagePath);
                payload = Marshal.AllocHGlobal(sizeof(uint) * 2);
                Marshal.WriteInt32(payload, 0, unchecked((int)strength));
                Marshal.WriteInt32(payload, sizeof(uint), processId);

                int result = write(
                    AutoHdrStrengthQuery,
                    IntPtr.Zero,
                    payload,
                    sizeof(uint) * 2,
                    path,
                    0,
                    IntPtr.Zero);
                ThrowOnFailure(result, "WriteUserSetting(ordinal 111)");
            }
            finally
            {
                if (payload != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(payload);
                }

                if (path != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(path);
                }

                FreeLibrary(module);
            }
        }

        private static string NormalizeImagePath(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException(Strings.Get("ErrorExecutablePathRequired"), nameof(imagePath));
            }

            return Path.GetFullPath(imagePath);
        }

        private static void ValidateStrength(uint strength)
        {
            if (strength > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(strength), Strings.Get("ErrorStrengthRange"));
            }
        }

        private static void Ensure64BitProcess()
        {
            if (IntPtr.Size != 8)
            {
                throw new PlatformNotSupportedException(Strings.Get("ErrorHelperRequiresX64"));
            }
        }

        private static IntPtr LoadSystemHelper()
        {
            IntPtr module = LoadLibraryEx(
                "directxdatabasehelper.dll",
                IntPtr.Zero,
                LoadLibrarySearchSystem32);
            if (module == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), Strings.Get("ErrorLoadSystemHelper"));
            }

            return module;
        }

        private static T GetOrdinalDelegate<T>(IntPtr module, int ordinal) where T : class
        {
            IntPtr address = GetProcAddress(module, new IntPtr(ordinal));
            if (address == IntPtr.Zero)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    Strings.Format("ErrorHelperOrdinalMissing", ordinal.ToString(CultureInfo.InvariantCulture)));
            }

            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        private static void ThrowOnFailure(int result, string operation)
        {
            if (result < 0)
            {
                throw new COMException(
                    Strings.Format(
                        "ErrorOperationFailedHResult",
                        operation,
                        result.ToString("X8", CultureInfo.InvariantCulture)),
                    result);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int QueryUserSettings(
            uint queryType,
            IntPtr imagePath,
            IntPtr logCallback,
            IntPtr output,
            uint outputSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteUserSetting(
            uint queryType,
            IntPtr logCallback,
            IntPtr data,
            uint dataSize,
            IntPtr imagePath,
            int reserved6,
            IntPtr reserved7);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);
    }
}
