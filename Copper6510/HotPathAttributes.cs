using System;

namespace Copper6510
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property)]
    internal sealed class HotPathAttribute : Attribute
    {
    }
}
