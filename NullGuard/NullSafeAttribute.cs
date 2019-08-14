using System;

namespace NullGuard {

    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class NullSafeAttribute : Attribute { }

}
