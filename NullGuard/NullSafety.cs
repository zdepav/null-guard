// ReSharper disable InconsistentlySynchronizedField

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;

namespace NullGuard {

    public static class NullSafety<T> where T : class {

        private delegate T GuardCreator(T instance);

        private static GuardCreator guardCreator;

        // ReSharper disable once StaticMemberInGenericType
        private static readonly object _lock = new object();

        public static T Guard(T instance) {
            if (instance is null)
                return null;
            if (guardCreator != null)
                return guardCreator(instance);
            Prepare();
            return guardCreator(instance);
        }

        public static void Prepare() {
            lock (_lock) {
                if (guardCreator is null) {
                    guardCreator = BuildCreator();
                }
            }
        }

        private static GuardCreator BuildCreator() {
            var type = typeof(T);

            if (!type.IsInterface)
                throw new InvalidOperationException("Null safety guards are only supported for interfaces.");
            if (!type.GetCustomAttributes(false).Any(a => a is NullSafeAttribute))
                throw new InvalidOperationException("Null safety guards can only be used on interfaces with the NullSafe attribute.");

            var tb = NullGuardImpl.ModuleBuilder.DefineType(
                $"NullGuard_impl_{Regex.Replace(type.Namespace, @"[^0-9a-zA-Z_]", "_")}_{Regex.Replace(type.Name, @"[^0-9a-zA-Z_]", "_")}",
                TypeAttributes.Public |
                TypeAttributes.Class |
                TypeAttributes.AutoClass |
                TypeAttributes.AnsiClass |
                TypeAttributes.AutoLayout |
                TypeAttributes.Sealed,
                null,
                new[] { type }
            );

            var fb = tb.DefineField("__inst", type, FieldAttributes.Public);

            foreach (var prop in type.GetProperties()) {
                var nullable = GetNullability(prop.CustomAttributes, "property");
                AddCheckedProperty(type, tb, fb, prop, nullable);
            }
            foreach (var fn in type.GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(m => !m.IsSpecialName)) {
                var nullable = GetNullability(fn.CustomAttributes, "method");
                if (fn.ReturnType.IsByRef)
                    throw new InvalidOperationException("Nullability attributes can not be used on void methods.");
                if (nullable.HasValue && fn.ReturnType == typeof(void))
                    throw new InvalidOperationException("Nullability attributes can not be used on void methods.");
                AddCheckedMethod(type, tb, fb, fn, nullable);
            }

            tb.DefineDefaultConstructor(
                MethodAttributes.Public |
                MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName
            );

            var implType = tb.CreateType();

            var param = Expression.Parameter(type, "instance");
            var local = Expression.Variable(implType, "ret");
            return Expression.Lambda<GuardCreator>(
                Expression.Block(
                    new[] { local },
                    Expression.Assign(local, Expression.New(implType.GetConstructor(new Type[0]))), // implType ret = new implType();
                    Expression.Assign(Expression.Field(local, implType.GetField("__inst")), param), // ret.__inst = instance;
                    local                                                                           // return ret;
                ),
                param
            ).Compile();
        }

        private static void AddCheckedProperty(Type type, TypeBuilder tb, FieldBuilder fb, PropertyInfo prop, bool? nullable) {
            var propParams = prop.CanRead
                ? prop.GetMethod
                      .GetParameters()
                : prop.SetMethod
                      .GetParameters()
                      .Take(prop.SetMethod.GetParameters().Length)
                      .ToArray();
            var property = tb.DefineProperty(
                prop.Name,
                prop.Attributes,
                prop.PropertyType,
                propParams.Select(p => p.ParameterType).ToArray()
            );
            if (prop.CanRead) {
                var getter = prop.GetMethod;
                bool getterNullable;
                if (nullable.HasValue) {
                    getterNullable = nullable.Value;
                    CheckNullabilityNotSet(getter.CustomAttributes, "getter");
                } else {
                    getterNullable = GetNullability(getter.CustomAttributes, "getter") ?? false;
                }
                AddCheckedGetter(type, tb, fb, prop, property, getter, propParams, getterNullable);
            }
            if (prop.CanWrite) {
                var setter = prop.SetMethod;
                bool setterNullable;
                if (nullable.HasValue) {
                    setterNullable = nullable.Value;
                    CheckNullabilityNotSet(setter.CustomAttributes, "setter");
                } else {
                    setterNullable = GetNullability(setter.CustomAttributes, "setter") ?? false;
                }
                AddCheckedSetter(type, tb, fb, prop, property, setter, propParams, setterNullable);
            }
        }

        private static void AddCheckedGetter(Type type, TypeBuilder tb, FieldBuilder fb, PropertyInfo prop, PropertyBuilder property, MethodInfo getter, ParameterInfo[] propParams, bool getterNullable) {
            var getMethod = tb.DefineMethod(
                getter.Name,
                MethodAttributes.Public | MethodAttributes.Virtual,
                getter.CallingConvention,
                getter.ReturnType,
                propParams.Select(p => p.ParameterType).ToArray()
            );
            var ilg = getMethod.GetILGenerator();
            ilg.Emit(OpCodes.Ldarg_0);
            ilg.Emit(OpCodes.Ldfld, fb);
            if (propParams.Length > 0) {
                for (var i = 0; i < propParams.Length; i++) {
                    var arg = propParams[i];
                    if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
                        throw new InvalidOperationException("Nullability attributes can not be used on methods arguments with non-nullable type.");
                    if (GetNullability(arg.CustomAttributes, "property argument") != true)
                        AddPropertyArgumentCheck(type, prop, false, arg, i, ilg);
                    ilg.Emit(OpCodes.Ldarg, i + 1);
                }
            }
            ilg.Emit(OpCodes.Callvirt, getter);
            if (!getterNullable)
                AddPropertyReturnValueCheck(type, prop, ilg);
            ilg.Emit(OpCodes.Ret);
            property.SetGetMethod(getMethod);
            tb.DefineMethodOverride(getMethod, getter);
        }

        private static void AddPropertyReturnValueCheck(Type type, PropertyInfo prop, ILGenerator ilg) {
            var local = ilg.DeclareLocal(prop.PropertyType);
            ilg.Emit(OpCodes.Stloc, local);
            if (Nullable.GetUnderlyingType(prop.PropertyType) != null) {
                ilg.Emit(prop.PropertyType.IsByRef ? OpCodes.Ldloc : OpCodes.Ldloca, local);
                ilg.Emit(OpCodes.Call, prop.PropertyType.GetMethod("get_HasValue"));
            } else {
                ilg.Emit(OpCodes.Ldloc, local);
                if (prop.PropertyType.IsByRef)
                    ilg.Emit(OpCodes.Ldind_Ref);
            }
            AddIfNullThenException(ilg, $"Null safety violation: {type.FullName}.get_{prop.Name} returned null.");
            ilg.Emit(OpCodes.Ldloc, local);
        }

        private static void AddCheckedSetter(Type type, TypeBuilder tb, FieldBuilder fb, PropertyInfo prop, PropertyBuilder property, MethodInfo setter, ParameterInfo[] propParams, bool setterNullable) {
            var setMethod = tb.DefineMethod(
                setter.Name,
                MethodAttributes.Public | MethodAttributes.Virtual,
                setter.CallingConvention,
                setter.ReturnType,
                propParams.Select(p => p.ParameterType).Append(prop.PropertyType).ToArray()
            );
            var ilg = setMethod.GetILGenerator();
            ilg.Emit(OpCodes.Ldarg_0);
            ilg.Emit(OpCodes.Ldfld, fb);
            if (propParams.Length > 0) {
                for (var i = 0; i < propParams.Length; i++) {
                    var arg = propParams[i];
                    if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
                        throw new InvalidOperationException("Nullability attributes can not be used on methods arguments with non-nullable type.");
                    if (GetNullability(arg.CustomAttributes, "property argument") != true)
                        AddPropertyArgumentCheck(type, prop, false, arg, i, ilg);
                    ilg.Emit(OpCodes.Ldarg, i + 1);
                }
            }
            if (!setterNullable)
                AddPropertySetterValueCheck(type, prop, propParams.Length, ilg);
            ilg.Emit(OpCodes.Ldarg, propParams.Length + 1);
            ilg.Emit(OpCodes.Callvirt, setter);
            ilg.Emit(OpCodes.Ret);
            property.SetSetMethod(setMethod);
            tb.DefineMethodOverride(setMethod, setter);
        }

        private static void AddPropertySetterValueCheck(Type type, PropertyInfo prop, int propParamsLength, ILGenerator ilg) {
            if (Nullable.GetUnderlyingType(prop.PropertyType) != null) {
                ilg.Emit(OpCodes.Ldarga, propParamsLength + 1);
                ilg.Emit(OpCodes.Call, prop.PropertyType.GetMethod("get_HasValue"));
            } else {
                ilg.Emit(OpCodes.Ldarg, propParamsLength + 1);
            }
            AddIfNullThenException(ilg, $"Null safety violation: Value assigned to {type.FullName}.set_{prop.Name} is null.");
        }

        private static void AddPropertyArgumentCheck(Type type, PropertyInfo prop, bool setter, ParameterInfo arg, int argIndex, ILGenerator ilg) {
            if (Nullable.GetUnderlyingType(prop.PropertyType) != null) {
                ilg.Emit(OpCodes.Ldarga, argIndex + 1);
                ilg.Emit(OpCodes.Call, prop.PropertyType.GetMethod("get_HasValue"));
            } else {
                ilg.Emit(OpCodes.Ldarg, argIndex + 1);
            }
            AddIfNullThenException(ilg, $"Null safety violation: Input argument {arg.Name} of {type.FullName}.{(setter ? 's' : 'g')}et_{prop.Name} is null.");
        }

        private static void AddCheckedMethod(Type type, TypeBuilder tb, FieldBuilder fb, MethodInfo fn, bool? nullable) {
            var nullableReturnValue = nullable ?? false;
            var args = fn.GetParameters();
            var method = tb.DefineMethod(
                fn.Name,
                MethodAttributes.Public | MethodAttributes.Virtual,
                fn.CallingConvention,
                fn.ReturnType,
                args.Select(a => a.ParameterType).ToArray()
            );
            var ilg = method.GetILGenerator();
            ilg.Emit(OpCodes.Ldarg_0);
            ilg.Emit(OpCodes.Ldfld, fb);
            if (args.Length > 0) {
                for (var i = 0; i < args.Length; i++) {
                    var arg = args[i];
                    if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
                        throw new InvalidOperationException("Nullability attributes can not be used on methods arguments with non-nullable type.");
                    if (GetNullability(arg.CustomAttributes, "method argument") != true)
                        AddMethodArgumentInCheck(type, fn, arg, i, ilg);
                    ilg.Emit(OpCodes.Ldarg, i + 1);
                }
                ilg.Emit(OpCodes.Callvirt, fn);
                for (var i = 0; i < args.Length; i++) {
                    var arg = args[i];
                    if (GetNullability(arg.CustomAttributes, "method argument") != true)
                        AddMethodArgumentOutCheck(type, fn, arg, i, ilg);
                }
            } else {
                ilg.Emit(OpCodes.Callvirt, fn);
            }
            if (!nullableReturnValue)
                AddMethodReturnValueCheck(type, fn, ilg);
            ilg.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(method, fn);
        }

        private static void AddMethodReturnValueCheck(Type type, MethodInfo fn, ILGenerator ilg) {
            var local = ilg.DeclareLocal(fn.ReturnType);
            ilg.Emit(OpCodes.Stloc, local);
            if (Nullable.GetUnderlyingType(fn.ReturnType) != null) {
                ilg.Emit(fn.ReturnType.IsByRef ? OpCodes.Ldloc : OpCodes.Ldloca, local);
                ilg.Emit(OpCodes.Call, fn.ReturnType.GetMethod("get_HasValue"));
            } else {
                ilg.Emit(OpCodes.Ldloc, local);
                if (fn.ReturnType.IsByRef)
                    ilg.Emit(OpCodes.Ldind_Ref);
            }
            AddIfNullThenException(ilg, $"Null safety violation: {type.FullName}.{fn.Name} returned null.");
            ilg.Emit(OpCodes.Ldloc, local);
        }

        private static void AddMethodArgumentInCheck(Type type, MethodInfo fn, ParameterInfo arg, int argIndex, ILGenerator ilg) {
            if (!arg.ParameterType.IsByRef || !arg.IsOut) {
                if (Nullable.GetUnderlyingType(arg.ParameterType) != null) {
                    ilg.Emit(arg.ParameterType.IsByRef ? OpCodes.Ldarg : OpCodes.Ldarga, argIndex + 1);
                    ilg.Emit(OpCodes.Call, arg.ParameterType.GetMethod("get_HasValue"));
                } else {
                    ilg.Emit(OpCodes.Ldarg, argIndex + 1);
                    if (arg.ParameterType.IsByRef)
                        ilg.Emit(OpCodes.Ldind_Ref);
                }
                AddIfNullThenException(ilg, $"Null safety violation: Input argument {arg.Name} of {type.FullName}.{fn.Name} is null.");
            }
        }

        private static void AddMethodArgumentOutCheck(Type type, MethodInfo fn, ParameterInfo arg, int argIndex, ILGenerator ilg) {
            if (arg.ParameterType.IsByRef) {
                ilg.Emit(OpCodes.Ldarg, argIndex + 1);
                if (Nullable.GetUnderlyingType(arg.ParameterType) != null) {
                    ilg.Emit(OpCodes.Call, arg.ParameterType.GetMethod("get_HasValue"));
                } else {
                    ilg.Emit(OpCodes.Ldind_Ref);
                }
                AddIfNullThenException(ilg, $"Null safety violation: Output argument {arg.Name} of {type.FullName}.{fn.Name} is null.");
            }
        }

        private static void AddIfNullThenException(ILGenerator ilg, string errorMessage) {
            var l = ilg.DefineLabel();
            ilg.Emit(OpCodes.Brtrue, l);
            ilg.Emit(OpCodes.Ldstr, errorMessage);
            ilg.Emit(OpCodes.Newobj, NullGuardImpl.ViolationExceptionConstructor);
            ilg.Emit(OpCodes.Throw);
            ilg.MarkLabel(l);
        }

        private static void CheckNullabilityNotSet(IEnumerable<CustomAttributeData> attributes, string targetName) {
            if (attributes.Any(attr => attr.AttributeType == typeof(NeverNullAttribute) || attr.AttributeType == typeof(CanBeNullAttribute)))
                throw new InvalidOperationException($"Only one nullability attribute can be used on a singe {targetName}.");
        }

        private static bool? GetNullability(IEnumerable<CustomAttributeData> attributes, string targetName) {
            var found = false;
            bool? nullable = null;
            foreach (var attr in attributes) {
                if (attr.AttributeType == typeof(NeverNullAttribute)) {
                    if (found) throw new InvalidOperationException($"Only one nullability attribute can be used on a singe {targetName}.");
                    found = true;
                    nullable = false;
                } else if (attr.AttributeType == typeof(CanBeNullAttribute)) {
                    if (found) throw new InvalidOperationException($"Only one nullability attribute can be used on a singe {targetName}.");
                    found = true;
                    nullable = true;
                }
            }
            return nullable;
        }

    }

}
