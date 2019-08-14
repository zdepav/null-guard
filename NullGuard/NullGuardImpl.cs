using System;
using System.Reflection;
using System.Reflection.Emit;

namespace NullGuard {

    internal class NullGuardImpl {

        public static readonly AssemblyBuilder AssemblyBuilder;

        public static readonly ModuleBuilder ModuleBuilder;

        public static readonly ConstructorInfo ViolationExceptionConstructor;

        static NullGuardImpl() {
            var an = new AssemblyName("NullGuard_impl");
            AssemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            ModuleBuilder = AssemblyBuilder.DefineDynamicModule("MainModule");
            ViolationExceptionConstructor = typeof(NullSafetyViolationException).GetConstructor(new[] { typeof(string) });
        }

    }

}
