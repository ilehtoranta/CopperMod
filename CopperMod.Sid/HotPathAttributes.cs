using System;

namespace CopperMod.Sid
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property)]
    internal sealed class HotPathAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property)]
    internal sealed class HotPathAllocationAllowedAttribute : Attribute
    {
        public HotPathAllocationAllowedAttribute(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
