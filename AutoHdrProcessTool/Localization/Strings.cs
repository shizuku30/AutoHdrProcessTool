using System;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace AutoHdrProcessTool.Localization
{
    internal static class Strings
    {
        private static readonly ResourceManager EnglishResources = new ResourceManager(
            "AutoHdrProcessTool.Localization.en-us",
            typeof(Strings).Assembly);

        private static readonly ResourceManager ChineseResources = new ResourceManager(
            "AutoHdrProcessTool.Localization.zh-cn",
            typeof(Strings).Assembly);

        private static ResourceManager activeResources = EnglishResources;

        internal static CultureInfo SelectApplicationUiCulture(CultureInfo operatingSystemUiCulture)
        {
            if (operatingSystemUiCulture == null)
            {
                throw new ArgumentNullException(nameof(operatingSystemUiCulture));
            }

            return string.Equals(
                operatingSystemUiCulture.TwoLetterISOLanguageName,
                "zh",
                StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.GetCultureInfo("zh-CN")
                : CultureInfo.GetCultureInfo("en-US");
        }

        internal static void Initialize(CultureInfo operatingSystemUiCulture)
        {
            CultureInfo applicationUiCulture = SelectApplicationUiCulture(operatingSystemUiCulture);
            activeResources = applicationUiCulture.Name == "zh-CN"
                ? ChineseResources
                : EnglishResources;
            Thread.CurrentThread.CurrentUICulture = applicationUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = applicationUiCulture;
        }

        internal static string Get(string key)
        {
            string value = activeResources.GetString(key, CultureInfo.InvariantCulture);
            if (value == null)
            {
                throw new MissingManifestResourceException("Missing localized string: " + key);
            }

            return value;
        }

        internal static string Format(string key, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
        }
    }
}
