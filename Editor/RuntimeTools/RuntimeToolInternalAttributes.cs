using System;

namespace DotCraft.Editor.RuntimeTools
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    internal sealed class AgentToolSchemaHintAttribute : Attribute
    {
        public int Minimum { get; set; } = int.MinValue;

        public string[] EnumValues { get; set; }
    }
}
