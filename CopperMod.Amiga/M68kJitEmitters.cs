using System;
using System.Reflection;
using System.Reflection.Emit;

namespace CopperMod.Amiga
{
    internal static class M68kEaEmitter
    {
        public static void EmitEaArguments(ILGenerator il, M68kDecodedEa ea)
        {
            il.Emit(OpCodes.Ldc_I4, (int)ea.Kind);
            il.Emit(OpCodes.Ldc_I4, ea.Register);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)ea.ExtensionAddress));
            il.Emit(OpCodes.Ldc_I4, ea.Extension0);
            il.Emit(OpCodes.Ldc_I4, ea.Extension1);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)ea.Immediate));
        }
    }

    internal static class M68kOperationEmitter
    {
        private static readonly MethodInfo ExecuteDecodedOperation =
            typeof(M68kJitCore).GetMethod(
                "ExecuteCompiledDecodedOperation",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new MissingMethodException(typeof(M68kJitCore).FullName, "ExecuteCompiledDecodedOperation");

        public static void Emit(ILGenerator il, M68kDecodedInstruction instruction)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Operation);
            il.Emit(OpCodes.Ldc_I4, (int)instruction.Size);
            M68kEaEmitter.EmitEaArguments(il, instruction.Source);
            M68kEaEmitter.EmitEaArguments(il, instruction.Destination);
            il.Emit(OpCodes.Ldc_I4, instruction.Register);
            il.Emit(OpCodes.Ldc_I4, instruction.QuickValue);
            il.Emit(OpCodes.Ldc_I4, instruction.Condition);
            il.Emit(OpCodes.Ldc_I4, instruction.Displacement);
            il.Emit(OpCodes.Ldc_I4, instruction.Variant);
            il.Emit(OpCodes.Ldc_I4, instruction.RegisterMask);
            il.Emit(OpCodes.Ldc_I4, unchecked((int)instruction.BranchBase));
            il.Emit(OpCodes.Call, ExecuteDecodedOperation);
        }
    }
}
