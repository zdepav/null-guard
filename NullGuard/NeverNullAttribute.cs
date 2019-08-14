using System;

namespace NullGuard {

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Method)]
    public sealed class NeverNullAttribute : Attribute { }

}
