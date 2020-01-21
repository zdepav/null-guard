using System;
using System.Reflection;

namespace NullGuard {

    public class NullSafetyViolationException : Exception {
        
        public NullSafetyViolationException() { }

        public NullSafetyViolationException(string message) : base(message) { }

        public NullSafetyViolationException(string message, Exception innerException) : base(message, innerException) { }
    }
}
