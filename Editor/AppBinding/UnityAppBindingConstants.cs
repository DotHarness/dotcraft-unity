namespace DotCraft.Editor.AppBinding
{
    internal static class UnityAppBindingConstants
    {
        public const string AppId = "com.dotharness.dotcraft-unity";
        public const string ToolNamespace = "unity";
        public const int LocalServerPort = 39777;

        public const string ScopeRead = "unity.read";
        public const string ScopeEdit = "unity.edit";
        public const string ScopeExecute = "unity.execute";

        public const string RiskRead = "read";
        public const string RiskMutate = "mutate";
        public const string RiskExternalWrite = "externalWrite";

        public const string ExposureDirect = "direct";
        public const string ExposureDeferred = "deferred";
    }
}
