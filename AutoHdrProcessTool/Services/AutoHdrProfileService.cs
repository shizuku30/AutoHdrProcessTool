using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using AutoHdrProcessTool.Localization;
using AutoHdrProcessTool.Models;
using Microsoft.Win32;

namespace AutoHdrProcessTool.Services
{
    internal sealed class AutoHdrProfileService
    {
        private const string Direct3DKeyPath = @"Software\Microsoft\Direct3D";
        private const string NameValue = "Name";
        private const string BehaviorsValue = "D3DBehaviors";
        private const string ManagedMarkerValue = "ManagedByAutoHdrProcessTool";
        private const string UpgradeOverride = "BufferUpgradeOverride";
        private const string Upgrade10Bit = "BufferUpgradeEnable10Bit";

        public AutoHdrRuleState GetState(string imagePath)
        {
            imagePath = NormalizeImagePath(imagePath);
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey direct3D = root.OpenSubKey(Direct3DKeyPath, false))
            {
                if (direct3D == null)
                {
                    return AutoHdrRuleState.Disabled;
                }

                string keyName = FindMatchingKeyName(direct3D, imagePath);
                if (keyName == null)
                {
                    return AutoHdrRuleState.Disabled;
                }

                using (RegistryKey profile = direct3D.OpenSubKey(keyName, false))
                {
                    if (profile == null)
                    {
                        return AutoHdrRuleState.Disabled;
                    }

                    string behaviors = profile.GetValue(
                        BehaviorsValue,
                        string.Empty,
                        RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    Dictionary<string, string> values = ReadBehaviorValues(behaviors);

                    string overrideValue;
                    bool enabled = values.TryGetValue(UpgradeOverride, out overrideValue) && overrideValue == "1";
                    string tenBitValue;
                    bool enable10Bit = values.TryGetValue(Upgrade10Bit, out tenBitValue) && tenBitValue == "1";
                    return new AutoHdrRuleState(enabled, enable10Bit, keyName);
                }
            }
        }

        public void Enable(string imagePath, bool enable10Bit)
        {
            imagePath = NormalizeImagePath(imagePath);
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey direct3D = root.CreateSubKey(Direct3DKeyPath, true))
            {
                if (direct3D == null)
                {
                    throw new InvalidOperationException(Strings.Get("ErrorOpenDirect3DProfile"));
                }

                string keyName = FindMatchingKeyName(direct3D, imagePath);
                bool createdByTool = keyName == null;
                if (createdByTool)
                {
                    keyName = BuildKeyName(imagePath);
                }

                using (RegistryKey profile = direct3D.CreateSubKey(keyName, true))
                {
                    if (profile == null)
                    {
                        throw new InvalidOperationException(Strings.Get("ErrorCreateAutoHdrProfile"));
                    }

                    string oldBehaviors = profile.GetValue(
                        BehaviorsValue,
                        string.Empty,
                        RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    string newBehaviors = MergeBehaviors(oldBehaviors, true, enable10Bit);
                    profile.SetValue(NameValue, imagePath, RegistryValueKind.String);
                    profile.SetValue(BehaviorsValue, newBehaviors, RegistryValueKind.String);
                    if (createdByTool)
                    {
                        profile.SetValue(ManagedMarkerValue, 1, RegistryValueKind.DWord);
                    }
                }
            }
        }

        public void Disable(string imagePath)
        {
            imagePath = NormalizeImagePath(imagePath);
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
            using (RegistryKey direct3D = root.OpenSubKey(Direct3DKeyPath, true))
            {
                if (direct3D == null)
                {
                    return;
                }

                string keyName = FindMatchingKeyName(direct3D, imagePath);
                if (keyName == null)
                {
                    return;
                }

                bool deleteManagedProfile = false;
                using (RegistryKey profile = direct3D.OpenSubKey(keyName, true))
                {
                    if (profile == null)
                    {
                        return;
                    }

                    string oldBehaviors = profile.GetValue(
                        BehaviorsValue,
                        string.Empty,
                        RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    string newBehaviors = MergeBehaviors(oldBehaviors, false, false);
                    if (string.IsNullOrEmpty(newBehaviors))
                    {
                        profile.DeleteValue(BehaviorsValue, false);
                    }
                    else
                    {
                        profile.SetValue(BehaviorsValue, newBehaviors, RegistryValueKind.String);
                    }

                    bool managed = Convert.ToInt32(profile.GetValue(ManagedMarkerValue, 0)) == 1;
                    deleteManagedProfile = managed &&
                                           string.IsNullOrEmpty(newBehaviors) &&
                                           profile.SubKeyCount == 0 &&
                                           HasOnlyToolValues(profile);
                }

                if (deleteManagedProfile)
                {
                    direct3D.DeleteSubKeyTree(keyName, false);
                }
            }
        }

        internal static string MergeBehaviors(string existing, bool enable, bool enable10Bit)
        {
            var retained = new List<string>();
            foreach (string rawSegment in (existing ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string segment = rawSegment.Trim();
                if (segment.Length == 0)
                {
                    continue;
                }

                string settingName = GetSettingName(segment);
                if (settingName.Equals(UpgradeOverride, StringComparison.OrdinalIgnoreCase) ||
                    settingName.Equals(Upgrade10Bit, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                retained.Add(segment);
            }

            if (enable)
            {
                retained.Add(UpgradeOverride + "=1");
                if (enable10Bit)
                {
                    retained.Add(Upgrade10Bit + "=1");
                }
            }

            return string.Join(";", retained);
        }

        private static Dictionary<string, string> ReadBehaviorValues(string behaviors)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawSegment in (behaviors ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string segment = rawSegment.Trim();
                int equalsIndex = segment.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                values[segment.Substring(0, equalsIndex).Trim()] = segment.Substring(equalsIndex + 1).Trim();
            }

            return values;
        }

        private static string GetSettingName(string segment)
        {
            int equalsIndex = segment.IndexOf('=');
            return equalsIndex < 0 ? segment : segment.Substring(0, equalsIndex).Trim();
        }

        private static string FindMatchingKeyName(RegistryKey direct3D, string imagePath)
        {
            foreach (string subKeyName in direct3D.GetSubKeyNames())
            {
                using (RegistryKey profile = direct3D.OpenSubKey(subKeyName, false))
                {
                    if (profile == null)
                    {
                        continue;
                    }

                    string configuredPath = profile.GetValue(
                        NameValue,
                        null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (string.Equals(configuredPath, imagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return subKeyName;
                    }
                }
            }

            return null;
        }

        private static string BuildKeyName(string imagePath)
        {
            string baseName = Path.GetFileNameWithoutExtension(imagePath) ?? "Application";
            var safeName = new StringBuilder(baseName.Length);
            foreach (char character in baseName)
            {
                safeName.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-'
                    ? character
                    : '_');
            }

            byte[] pathBytes = Encoding.UTF8.GetBytes(imagePath.ToUpperInvariant());
            byte[] hash;
            using (SHA256 sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(pathBytes);
            }

            var hashText = new StringBuilder(12);
            for (int index = 0; index < 6; index++)
            {
                hashText.Append(hash[index].ToString("x2"));
            }

            return "AutoHdrTool_" + safeName + "_" + hashText;
        }

        private static bool HasOnlyToolValues(RegistryKey profile)
        {
            foreach (string valueName in profile.GetValueNames())
            {
                if (valueName.Equals(NameValue, StringComparison.OrdinalIgnoreCase) ||
                    valueName.Equals(ManagedMarkerValue, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static string NormalizeImagePath(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException(Strings.Get("ErrorExecutablePathRequired"), nameof(imagePath));
            }

            return Path.GetFullPath(imagePath);
        }
    }
}
