namespace AutoHdrProcessTool.Models
{
    internal sealed class AutoHdrRuleState
    {
        public static readonly AutoHdrRuleState Disabled = new AutoHdrRuleState(false, false, null);

        public AutoHdrRuleState(bool enabled, bool enable10Bit, string registryKeyName)
        {
            Enabled = enabled;
            Enable10Bit = enable10Bit;
            RegistryKeyName = registryKeyName;
        }

        public bool Enabled { get; }

        public bool Enable10Bit { get; }

        public string RegistryKeyName { get; }
    }
}
