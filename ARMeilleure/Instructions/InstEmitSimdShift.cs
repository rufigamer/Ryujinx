// https://github.com/intel/ARM_NEON_2_x86_SSE/blob/master/NEON_2_SSE.h

using ARMeilleure.Decoders;
using ARMeilleure.IntermediateRepresentation;
using ARMeilleure.Translation;
using System;
using System.Diagnostics;
using System.Reflection;

using static ARMeilleure.Instructions.InstEmitHelper;
using static ARMeilleure.Instructions.InstEmitSimdHelper;
using static ARMeilleure.IntermediateRepresentation.Operand.Factory;

namespace ARMeilleure.Instructions
{
    using Func2I = Func<Operand, Operand, Operand>;

    static partial class InstEmit
    {
#region "Masks"
        private static readonly long[] _masks_SliSri = new long[] // Replication masks.
        {
            0x0101010101010101L, 0x0001000100010001L, 0x0000000100000001L, 0x0000000000000001L
        };
#endregion

        public static void Rshrn_V(ArmEmitterContext context)
        {
            if (Optimizations.UseSsse3)
            {
                OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

                int shift = GetImmShr(op);

                long roundConst = 1L << (shift - 1);

                Operand d = GetVec(op.Rd);
                Operand n = GetVec(op.Rn);

                Operand dLow = context.VectorZeroUpper64(d);

                Operand mask = default;

                switch (op.Size + 1)
                {
                    case 1: mask = X86GetAllElements(context, (int)roundConst * 0x00010001); break;
                    case 2: mask = X86GetAllElements(context, (int)roundConst); break;
                    case 3: mask = X86GetAllElements(context,      roundConst); break;
                }

                Intrinsic addInst = X86PaddInstruction[op.Size + 1];

                Operand res = context.AddIntrinsic(addInst, n, mask);

                Intrinsic srlInst = X86PsrlInstruction[op.Size + 1];

                res = context.AddIntrinsic(srlInst, res, Const(shift));

                Operand mask2 = X86GetAllElements(context, EvenMasks[op.Size]);

                res = context.AddIntrinsic(Intrinsic.X86Pshufb, res, mask2);

                Intrinsic movInst = op.RegisterSize == RegisterSize.Simd128
                    ? Intrinsic.X86Movlhps
                    : Intrinsic.X86Movhlps;

                res = context.AddIntrinsic(movInst, dLow, res);

                context.Copy(d, res);
            }
            else
            {
                EmitVectorShrImmNarrowOpZx(context, round: true);
            }
        }

        public static void Shl_S(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            int shift = GetImmShl(op);

            EmitScalarUnaryOpZx(context, (op1) => context.ShiftLeft(op1, Const(shift)));
        }

        public static void Shl_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            int shift = GetImmShl(op);

            if (Optimizations.UseSse2 && op.Size > 0)
            {
                Operand n = GetVec(op.Rn);

                Intrinsic sllInst = X86PsllInstruction[op.Size];

                Operand res = context.AddIntrinsic(sllInst, n, Const(shift));

                if (op.RegisterSize == RegisterSize.Simd64)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(GetVec(op.Rd), res);
            }
            else
            {
                EmitVectorUnaryOpZx(context, (op1) => context.ShiftLeft(op1, Const(shift)));
            }
        }

        public static void Shll_V(ArmEmitterContext context)
        {
            OpCodeSimd op = (OpCodeSimd)context.CurrOp;

            int shift = 8 << op.Size;

            if (Optimizations.UseSse41)
            {
                Operand n = GetVec(op.Rn);

                if (op.RegisterSize == RegisterSize.Simd128)
                {
                    n = context.AddIntrinsic(Intrinsic.X86Psrldq, n, Const(8));
                }

                Intrinsic movsxInst = X86PmovsxInstruction[op.Size];

                Operand res = context.AddIntrinsic(movsxInst, n);

                Intrinsic sllInst = X86PsllInstruction[op.Size + 1];

                res = context.AddIntrinsic(sllInst, res, Const(shift));

                context.Copy(GetVec(op.Rd), res);
            }
            else
            {
                EmitVectorShImmWidenBinaryZx(context, (op1, op2) => context.ShiftLeft(op1, op2), shift);
            }
        }

        public static void Shrn_V(ArmEmitterContext context)
        {
            if (Optimizations.UseSsse3)
            {
                OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

                int shift = GetImmShr(op);

                Operand d = GetVec(op.Rd);
                Operand n = GetVec(op.Rn);

                Operand dLow = context.VectorZeroUpper64(d);

                Intrinsic srlInst = X86PsrlInstruction[op.Size + 1];

                Operand nShifted = context.AddIntrinsic(srlInst, n, Const(shift));

                Operand mask = X86GetAllElements(context, EvenMasks[op.Size]);

                Operand res = context.AddIntrinsic(Intrinsic.X86Pshufb, nShifted, mask);

                Intrinsic movInst = op.RegisterSize == RegisterSize.Simd128
                    ? Intrinsic.X86Movlhps
                    : Intrinsic.X86Movhlps;

                res = context.AddIntrinsic(movInst, dLow, res);

                context.Copy(d, res);
            }
            else
            {
                EmitVectorShrImmNarrowOpZx(context, round: false);
            }
        }

        public static void Sli_S(ArmEmitterContext context)
        {
            EmitSli(context, scalar: true);
        }

        public static void Sli_V(ArmEmitterContext context)
        {
            EmitSli(context, scalar: false);
        }

        public static void Sqrshl_V(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.Signed | ShlRegFlags.Round | ShlRegFlags.Saturating);
        }

        public static void Sqrshrn_S(ArmEmitterContext context)
        {
            EmitRoundShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.ScalarSxSx);
        }

        public static void Sqrshrn_V(ArmEmitterContext context)
        {
            EmitRoundShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.VectorSxSx);
        }

        public static void Sqrshrun_S(ArmEmitterContext context)
        {
            EmitRoundShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.ScalarSxZx);
        }

        public static void Sqrshrun_V(ArmEmitterContext context)
        {
            EmitRoundShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.VectorSxZx);
        }

        public static void Sqshl_V(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.Signed | ShlRegFlags.Saturating);
        }

        public static void Sqshrn_S(ArmEmitterContext context)
        {
            EmitShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.ScalarSxSx);
        }

        public static void Sqshrn_V(ArmEmitterContext context)
        {
            EmitShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.VectorSxSx);
        }

        public static void Sqshrun_S(ArmEmitterContext context)
        {
            EmitShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.ScalarSxZx);
        }

        public static void Sqshrun_V(ArmEmitterContext context)
        {
            EmitShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.VectorSxZx);
        }

        public static void Sri_S(ArmEmitterContext context)
        {
            EmitSri(context, scalar: true);
        }

        public static void Sri_V(ArmEmitterContext context)
        {
            EmitSri(context, scalar: false);
        }

        public static void Srshl_V(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.Signed | ShlRegFlags.Round);
        }

        public static void Srshr_S(ArmEmitterContext context)
        {
            EmitScalarShrImmOpSx(context, ShrImmFlags.Round);
        }

        public static void Srshr_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            if (Optimizations.UseSse2 && op.Size > 0 && op.Size < 3)
            {
                int shift = GetImmShr(op);
                int eSize = 8 << op.Size;

                Operand n = GetVec(op.Rn);

                Intrinsic sllInst = X86PsllInstruction[op.Size];

                Operand res = context.AddIntrinsic(sllInst, n, Const(eSize - shift));

                Intrinsic srlInst = X86PsrlInstruction[op.Size];

                res = context.AddIntrinsic(srlInst, res, Const(eSize - 1));

                Intrinsic sraInst = X86PsraInstruction[op.Size];

                Operand nSra = context.AddIntrinsic(sraInst, n, Const(shift));

                Intrinsic addInst = X86PaddInstruction[op.Size];

                res = context.AddIntrinsic(addInst, res, nSra);

                if (op.RegisterSize == RegisterSize.Simd64)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(GetVec(op.Rd), res);
            }
            else
            {
                EmitVectorShrImmOpSx(context, ShrImmFlags.Round);
            }
        }

        public static void Srsra_S(ArmEmitterContext context)
        {
            EmitScalarShrImmOpSx(context, ShrImmFlags.Round | ShrImmFlags.Accumulate);
        }

        public static void Srsra_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            if (Optimizations.UseSse2 && op.Size > 0 && op.Size < 3)
            {
                int shift = GetImmShr(op);
                int eSize = 8 << op.Size;

                Operand d = GetVec(op.Rd);
                Operand n = GetVec(op.Rn);

                Intrinsic sllInst = X86PsllInstruction[op.Size];

                Operand res = context.AddIntrinsic(sllInst, n, Const(eSize - shift));

                Intrinsic srlInst = X86PsrlInstruction[op.Size];

                res = context.AddIntrinsic(srlInst, res, Const(eSize - 1));

                Intrinsic sraInst = X86PsraInstruction[op.Size];

                Operand nSra = context.AddIntrinsic(sraInst, n, Const(shift));

                Intrinsic addInst = X86PaddInstruction[op.Size];

                res = context.AddIntrinsic(addInst, res, nSra);
                res = context.AddIntrinsic(addInst, res, d);

                if (op.RegisterSize == RegisterSize.Simd64)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(d, res);
            }
            else
            {
                EmitVectorShrImmOpSx(context, ShrImmFlags.Round | ShrImmFlags.Accumulate);
            }
        }

        public static void Sshl_S(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.Scalar | ShlRegFlags.Signed);
        }

        public static void Sshl_V(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.Signed);
        }

        public static void Sshll_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            int shift = GetImmShl(op);

            if (Optimizations.UseSse41)
            {
                Operand n = GetVec(op.Rn);

                if (op.RegisterSize == RegisterSize.Simd128)
                {
                    n = context.AddIntrinsic(Intrinsic.X86Psrldq, n, Const(8));
                }

                Intrinsic movsxInst = X86PmovsxInstruction[op.Size];

                Operand res = context.AddIntrinsic(movsxInst, n);

                if (shift != 0)
                {
                    Intrinsic sllInst = X86PsllInstruction[op.Size + 1];

                    res = context.AddIntrinsic(sllInst, res, Const(shift));
                }

                context.Copy(GetVec(op.Rd), res);
            }
            else
            {
                EmitVectorShImmWidenBinarySx(context, (op1, op2) => context.ShiftLeft(op1, op2), shift);
            }
        }

        public static void Sshr_S(ArmEmitterContext context)
        {
            EmitShrImmOp(context, ShrImmFlags.ScalarSx);
        }

        public static void Sshr_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            if (Optimizations.UseSse2 && op.Size > 0 && op.Size < 3)
            {
                int shift = GetImmShr(op);

                Operand n = GetVec(op.Rn);

                Intrinsic sraInst = X86PsraInstruction[op.Size];

                Operand res = context.AddIntrinsic(sraInst, n, Const(shift));

                if (op.RegisterSize == RegisterSize.Simd64)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(GetVec(op.Rd), res);
            }
            else
            {
                EmitShrImmOp(context, ShrImmFlags.VectorSx);
            }
        }

        public static void Ssra_S(ArmEmitterContext context)
        {
            EmitScalarShrImmOpSx(context, ShrImmFlags.Accumulate);
        }

        public static void Ssra_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            if (Optimizations.UseSse2 && op.Size > 0 && op.Size < 3)
            {
                int shift = GetImmShr(op);

                Operand d = GetVec(op.Rd);
                Operand n = GetVec(op.Rn);

                Intrinsic sraInst = X86PsraInstruction[op.Size];

                Operand res = context.AddIntrinsic(sraInst, n, Const(shift));

                Intrinsic addInst = X86PaddInstruction[op.Size];

                res = context.AddIntrinsic(addInst, res, d);

                if (op.RegisterSize == RegisterSize.Simd64)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(d, res);
            }
            else
            {
                EmitVectorShrImmOpSx(context, ShrImmFlags.Accumulate);
            }
        }

        public static void Uqrshl_V(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.Round | ShlRegFlags.Saturating);
        }

        public static void Uqrshrn_S(ArmEmitterContext context)
        {
            EmitRoundShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.ScalarZxZx);
        }

        public static void Uqrshrn_V(ArmEmitterContext context)
        {
            EmitRoundShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.VectorZxZx);
        }

        public static void Uqshl_V(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.Saturating);
        }

        public static void Uqshrn_S(ArmEmitterContext context)
        {
            EmitShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.ScalarZxZx);
        }

        public static void Uqshrn_V(ArmEmitterContext context)
        {
            EmitShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.VectorZxZx);
        }

        public static void Urshl_V(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.Round);
        }

        public static void Urshr_S(ArmEmitterContext context)
        {
            EmitScalarShrImmOpZx(context, ShrImmFlags.Round);
        }

        public static void Urshr_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            if (Optimizations.UseSse2 && op.Size > 0)
            {
                int shift = GetImmShr(op);
                int eSize = 8 << op.Size;

                Operand n = GetVec(op.Rn);

                Intrinsic sllInst = X86PsllInstruction[op.Size];

                Operand res = context.AddIntrinsic(sllInst, n, Const(eSize - shift));

                Intrinsic srlInst = X86PsrlInstruction[op.Size];

                res = context.AddIntrinsic(srlInst, res, Const(eSize - 1));

                Operand nSrl = context.AddIntrinsic(srlInst, n, Const(shift));

                Intrinsic addInst = X86PaddInstruction[op.Size];

                res = context.AddIntrinsic(addInst, res, nSrl);

                if (op.RegisterSize == RegisterSize.Simd64)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(GetVec(op.Rd), res);
            }
            else
            {
                EmitVectorShrImmOpZx(context, ShrImmFlags.Round);
            }
        }

        public static void Ursra_S(ArmEmitterContext context)
        {
            EmitScalarShrImmOpZx(context, ShrImmFlags.Round | ShrImmFlags.Accumulate);
        }

        public static void Ursra_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            if (Optimizations.UseSse2 && op.Size > 0)
            {
                int shift = GetImmShr(op);
                int eSize = 8 << op.Size;

                Operand d = GetVec(op.Rd);
                Operand n = GetVec(op.Rn);

                Intrinsic sllInst = X86PsllInstruction[op.Size];

                Operand res = context.AddIntrinsic(sllInst, n, Const(eSize - shift));

                Intrinsic srlInst = X86PsrlInstruction[op.Size];

                res = context.AddIntrinsic(srlInst, res, Const(eSize - 1));

                Operand nSrl = context.AddIntrinsic(srlInst, n, Const(shift));

                Intrinsic addInst = X86PaddInstruction[op.Size];

                res = context.AddIntrinsic(addInst, res, nSrl);
                res = context.AddIntrinsic(addInst, res, d);

                if (op.RegisterSize == RegisterSize.Simd64)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(d, res);
            }
            else
            {
                EmitVectorShrImmOpZx(context, ShrImmFlags.Round | ShrImmFlags.Accumulate);
            }
        }

        public static void Ushl_S(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.Scalar);
        }

        public static void Ushl_V(ArmEmitterContext context)
        {
            EmitShlRegOp(context, ShlRegFlags.None);
        }

        public static void Ushll_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            int shift = GetImmShl(op);

            if (Optimizations.UseSse41)
            {
                Operand n = GetVec(op.Rn);

                if (op.RegisterSize == RegisterSize.Simd128)
                {
                    n = context.AddIntrinsic(Intrinsic.X86Psrldq, n, Const(8));
                }

                Intrinsic movzxInst = X86PmovzxInstruction[op.Size];

                Operand res = context.AddIntrinsic(movzxInst, n);

                if (shift != 0)
                {
                    Intrinsic sllInst = X86PsllInstruction[op.Size + 1];

                    res = context.AddIntrinsic(sllInst, res, Const(shift));
                }

                context.Copy(GetVec(op.Rd), res);
            }
            else
            {
                EmitVectorShImmWidenBinaryZx(context, (op1, op2) => context.ShiftLeft(op1, op2), shift);
            }
        }

        public static void Ushr_S(ArmEmitterContext context)
        {
            EmitShrImmOp(context, ShrImmFlags.ScalarZx);
        }

        public static void Ushr_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            if (Optimizations.UseSse2 && op.Size > 0)
            {
                int shift = GetImmShr(op);

                Operand n = GetVec(op.Rn);

                Intrinsic srlInst = X86PsrlInstruction[op.Size];

                Operand res = context.AddIntrinsic(srlInst, n, Const(shift));

                if (op.RegisterSize == RegisterSize.Simd64)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(GetVec(op.Rd), res);
            }
            else
            {
                EmitShrImmOp(context, ShrImmFlags.VectorZx);
            }
        }

        public static void Usra_S(ArmEmitterContext context)
        {
            EmitScalarShrImmOpZx(context, ShrImmFlags.Accumulate);
        }

        public static void Usra_V(ArmEmitterContext context)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            if (Optimizations.UseSse2 && op.Size > 0)
            {
                int shift = GetImmShr(op);

                Operand d = GetVec(op.Rd);
                Operand n = GetVec(op.Rn);

                Intrinsic srlInst = X86PsrlInstruction[op.Size];

                Operand res = context.AddIntrinsic(srlInst, n, Const(shift));

                Intrinsic addInst = X86PaddInstruction[op.Size];

                res = context.AddIntrinsic(addInst, res, d);

                if (op.RegisterSize == RegisterSize.Simd64)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(d, res);
            }
            else
            {
                EmitVectorShrImmOpZx(context, ShrImmFlags.Accumulate);
            }
        }

        [Flags]
        private enum ShrImmFlags
        {
            Scalar = 1 << 0,
            Signed = 1 << 1,

            Round      = 1 << 2,
            Accumulate = 1 << 3,

            ScalarSx = Scalar | Signed,
            ScalarZx = Scalar,

            VectorSx = Signed,
            VectorZx = 0
        }

        private static void EmitScalarShrImmOpSx(ArmEmitterContext context, ShrImmFlags flags)
        {
            EmitShrImmOp(context, ShrImmFlags.ScalarSx | flags);
        }

        private static void EmitScalarShrImmOpZx(ArmEmitterContext context, ShrImmFlags flags)
        {
            EmitShrImmOp(context, ShrImmFlags.ScalarZx | flags);
        }

        private static void EmitVectorShrImmOpSx(ArmEmitterContext context, ShrImmFlags flags)
        {
            EmitShrImmOp(context, ShrImmFlags.VectorSx | flags);
        }

        private static void EmitVectorShrImmOpZx(ArmEmitterContext context, ShrImmFlags flags)
        {
            EmitShrImmOp(context, ShrImmFlags.VectorZx | flags);
        }

        private static void EmitShrImmOp(ArmEmitterContext context, ShrImmFlags flags)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            Operand res = context.VectorZero();

            bool scalar     = (flags & ShrImmFlags.Scalar)     != 0;
            bool signed     = (flags & ShrImmFlags.Signed)     != 0;
            bool round      = (flags & ShrImmFlags.Round)      != 0;
            bool accumulate = (flags & ShrImmFlags.Accumulate) != 0;

            int shift = GetImmShr(op);

            long roundConst = 1L << (shift - 1);

            int elems = !scalar ? op.GetBytesCount() >> op.Size : 1;

            for (int index = 0; index < elems; index++)
            {
                Operand e = EmitVectorExtract(context, op.Rn, index, op.Size, signed);

                if (op.Size <= 2)
                {
                    if (round)
                    {
                        e = context.Add(e, Const(roundConst));
                    }

                    e = signed ? context.ShiftRightSI(e, Const(shift)) : context.ShiftRightUI(e, Const(shift));
                }
                else /* if (op.Size == 3) */
                {
                    e = EmitShrImm64(context, e, signed, round ? roundConst : 0L, shift);
                }

                if (accumulate)
                {
                    Operand de = EmitVectorExtract(context, op.Rd, index, op.Size, signed);

                    e = context.Add(e, de);
                }

                res = EmitVectorInsert(context, res, e, index, op.Size);
            }

            context.Copy(GetVec(op.Rd), res);
        }

        private static void EmitVectorShrImmNarrowOpZx(ArmEmitterContext context, bool round)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            int shift = GetImmShr(op);

            long roundConst = 1L << (shift - 1);

            int elems = 8 >> op.Size;

            int part = op.RegisterSize == RegisterSize.Simd128 ? elems : 0;

            Operand d = GetVec(op.Rd);

            Operand res = part == 0 ? context.VectorZero() : context.Copy(d);

            for (int index = 0; index < elems; index++)
            {
                Operand e = EmitVectorExtractZx(context, op.Rn, index, op.Size + 1);

                if (round)
                {
                    e = context.Add(e, Const(roundConst));
                }

                e = context.ShiftRightUI(e, Const(shift));

                res = EmitVectorInsert(context, res, e, part + index, op.Size);
            }

            context.Copy(d, res);
        }

        [Flags]
        private enum ShrImmSaturatingNarrowFlags
        {
            Scalar    = 1 << 0,
            SignedSrc = 1 << 1,
            SignedDst = 1 << 2,

            Round = 1 << 3,

            ScalarSxSx = Scalar | SignedSrc | SignedDst,
            ScalarSxZx = Scalar | SignedSrc,
            ScalarZxZx = Scalar,

            VectorSxSx = SignedSrc | SignedDst,
            VectorSxZx = SignedSrc,
            VectorZxZx = 0
        }

        private static void EmitRoundShrImmSaturatingNarrowOp(ArmEmitterContext context, ShrImmSaturatingNarrowFlags flags)
        {
            EmitShrImmSaturatingNarrowOp(context, ShrImmSaturatingNarrowFlags.Round | flags);
        }

        private static void EmitShrImmSaturatingNarrowOp(ArmEmitterContext context, ShrImmSaturatingNarrowFlags flags)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            bool scalar    = (flags & ShrImmSaturatingNarrowFlags.Scalar)    != 0;
            bool signedSrc = (flags & ShrImmSaturatingNarrowFlags.SignedSrc) != 0;
            bool signedDst = (flags & ShrImmSaturatingNarrowFlags.SignedDst) != 0;
            bool round     = (flags & ShrImmSaturatingNarrowFlags.Round)     != 0;

            int shift = GetImmShr(op);

            long roundConst = 1L << (shift - 1);

            int elems = !scalar ? 8 >> op.Size : 1;

            int part = !scalar && (op.RegisterSize == RegisterSize.Simd128) ? elems : 0;

            Operand d = GetVec(op.Rd);

            Operand res = part == 0 ? context.VectorZero() : context.Copy(d);

            for (int index = 0; index < elems; index++)
            {
                Operand e = EmitVectorExtract(context, op.Rn, index, op.Size + 1, signedSrc);

                if (op.Size <= 1 || !round)
                {
                    if (round)
                    {
                        e = context.Add(e, Const(roundConst));
                    }

                    e = signedSrc ? context.ShiftRightSI(e, Const(shift)) : context.ShiftRightUI(e, Const(shift));
                }
                else /* if (op.Size == 2 && round) */
                {
                    e = EmitShrImm64(context, e, signedSrc, roundConst, shift); // shift <= 32
                }

                e = signedSrc ? EmitSignedSrcSatQ(context, e, op.Size, signedDst) : EmitUnsignedSrcSatQ(context, e, op.Size, signedDst);

                res = EmitVectorInsert(context, res, e, part + index, op.Size);
            }

            context.Copy(d, res);
        }

        // dst64 = (Int(src64, signed) + roundConst) >> shift;
        private static Operand EmitShrImm64(
            ArmEmitterContext context,
            Operand value,
            bool signed,
            long roundConst,
            int shift)
        {
            MethodInfo info = signed
                ? typeof(SoftFallback).GetMethod(nameof(SoftFallback.SignedShrImm64))
                : typeof(SoftFallback).GetMethod(nameof(SoftFallback.UnsignedShrImm64));

            return context.Call(info, value, Const(roundConst), Const(shift));
        }

        private static void EmitVectorShImmWidenBinarySx(ArmEmitterContext context, Func2I emit, int imm)
        {
            EmitVectorShImmWidenBinaryOp(context, emit, imm, signed: true);
        }

        private static void EmitVectorShImmWidenBinaryZx(ArmEmitterContext context, Func2I emit, int imm)
        {
            EmitVectorShImmWidenBinaryOp(context, emit, imm, signed: false);
        }

        private static void EmitVectorShImmWidenBinaryOp(ArmEmitterContext context, Func2I emit, int imm, bool signed)
        {
            OpCodeSimd op = (OpCodeSimd)context.CurrOp;

            Operand res = context.VectorZero();

            int elems = 8 >> op.Size;

            int part = op.RegisterSize == RegisterSize.Simd128 ? elems : 0;

            for (int index = 0; index < elems; index++)
            {
                Operand ne = EmitVectorExtract(context, op.Rn, part + index, op.Size, signed);

                res = EmitVectorInsert(context, res, emit(ne, Const(imm)), index, op.Size + 1);
            }

            context.Copy(GetVec(op.Rd), res);
        }

        private static void EmitSli(ArmEmitterContext context, bool scalar)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            int shift = GetImmShl(op);

            ulong mask = shift != 0 ? ulong.MaxValue >> (64 - shift) : 0UL;

            if (Optimizations.UseSse2 && op.Size > 0)
            {
                Operand d = GetVec(op.Rd);
                Operand n = GetVec(op.Rn);

                Intrinsic sllInst = X86PsllInstruction[op.Size];

                Operand nShifted = context.AddIntrinsic(sllInst, n, Const(shift));

                Operand dMask = X86GetAllElements(context, (long)mask * _masks_SliSri[op.Size]);

                Operand dMasked = context.AddIntrinsic(Intrinsic.X86Pand, d, dMask);

                Operand res = context.AddIntrinsic(Intrinsic.X86Por, nShifted, dMasked);

                if ((op.RegisterSize == RegisterSize.Simd64) || scalar)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(d, res);
            }
            else
            {
                Operand res = context.VectorZero();

                int elems = !scalar ? op.GetBytesCount() >> op.Size : 1;

                for (int index = 0; index < elems; index++)
                {
                    Operand ne = EmitVectorExtractZx(context, op.Rn, index, op.Size);

                    Operand neShifted = context.ShiftLeft(ne, Const(shift));

                    Operand de = EmitVectorExtractZx(context, op.Rd, index, op.Size);

                    Operand deMasked = context.BitwiseAnd(de, Const(mask));

                    Operand e = context.BitwiseOr(neShifted, deMasked);

                    res = EmitVectorInsert(context, res, e, index, op.Size);
                }

                context.Copy(GetVec(op.Rd), res);
            }
        }

        private static void EmitSri(ArmEmitterContext context, bool scalar)
        {
            OpCodeSimdShImm op = (OpCodeSimdShImm)context.CurrOp;

            int shift = GetImmShr(op);
            int eSize = 8 << op.Size;

            ulong mask = (ulong.MaxValue << (eSize - shift)) & (ulong.MaxValue >> (64 - eSize));

            if (Optimizations.UseSse2 && op.Size > 0)
            {
                Operand d = GetVec(op.Rd);
                Operand n = GetVec(op.Rn);

                Intrinsic srlInst = X86PsrlInstruction[op.Size];

                Operand nShifted = context.AddIntrinsic(srlInst, n, Const(shift));

                Operand dMask = X86GetAllElements(context, (long)mask * _masks_SliSri[op.Size]);

                Operand dMasked = context.AddIntrinsic(Intrinsic.X86Pand, d, dMask);

                Operand res = context.AddIntrinsic(Intrinsic.X86Por, nShifted, dMasked);

                if ((op.RegisterSize == RegisterSize.Simd64) || scalar)
                {
                    res = context.VectorZeroUpper64(res);
                }

                context.Copy(d, res);
            }
            else
            {
                Operand res = context.VectorZero();

                int elems = !scalar ? op.GetBytesCount() >> op.Size : 1;

                for (int index = 0; index < elems; index++)
                {
                    Operand ne = EmitVectorExtractZx(context, op.Rn, index, op.Size);

                    Operand neShifted = shift != 64 ? context.ShiftRightUI(ne, Const(shift)) : Const(0UL);

                    Operand de = EmitVectorExtractZx(context, op.Rd, index, op.Size);

                    Operand deMasked = context.BitwiseAnd(de, Const(mask));

                    Operand e = context.BitwiseOr(neShifted, deMasked);

                    res = EmitVectorInsert(context, res, e, index, op.Size);
                }

                context.Copy(GetVec(op.Rd), res);
            }
        }

        [Flags]
        private enum ShlRegFlags
        {
            None = 0,
            Scalar = 1 << 0,
            Signed = 1 << 1,
            Round = 1 << 2,
            Saturating = 1 << 3
        }

        private static void EmitShlRegOp(ArmEmitterContext context, ShlRegFlags flags = ShlRegFlags.None)
        {
            bool scalar = flags.HasFlag(ShlRegFlags.Scalar);
            bool signed = flags.HasFlag(ShlRegFlags.Signed);
            bool round = flags.HasFlag(ShlRegFlags.Round);
            bool saturating = flags.HasFlag(ShlRegFlags.Saturating);

            OpCodeSimdReg op = (OpCodeSimdReg)context.CurrOp;

            Operand res = context.VectorZero();

            int elems = !scalar ? op.GetBytesCount() >> op.Size : 1;

            for (int index = 0; index < elems; index++)
            {
                Operand ne = EmitVectorExtract(context, op.Rn, index, op.Size, signed);
                Operand me = EmitVectorExtractSx(context, op.Rm, index << op.Size, size: 0);

                Operand e = !saturating
                    ? EmitShlReg(context, ne, context.ConvertI64ToI32(me), round, op.Size, signed)
                    : EmitShlRegSatQ(context, ne, context.ConvertI64ToI32(me), round, op.Size, signed);

                res = EmitVectorInsert(context, res, e, index, op.Size);
            }

            context.Copy(GetVec(op.Rd), res);
        }

        // long SignedShlReg(long op, int shiftLsB, bool round, int size);
        // ulong UnsignedShlReg(ulong op, int shiftLsB, bool round, int size);
        private static Operand EmitShlReg(ArmEmitterContext context, Operand op, Operand shiftLsB, bool round, int size, bool signed)
        {
            int eSize = 8 << size;

            Debug.Assert(op.Type == OperandType.I64);
            Debug.Assert(shiftLsB.Type == OperandType.I32);
            Debug.Assert(eSize == 8 || eSize == 16 || eSize == 32 || eSize == 64);

            Operand lbl1 = Label();
            Operand lblEnd = Label();

            Operand eSizeOp = Const(eSize);
            Operand zero = Const(0);
            Operand zeroL = Const(0L);

            Operand res = context.Copy(context.AllocateLocal(OperandType.I64), op);

            context.BranchIf(lbl1, shiftLsB, zero, Comparison.GreaterOrEqual);
            context.Copy(res, signed
                ? EmitSignedShrReg(context, op, context.Negate(shiftLsB), round, eSize)
                : EmitUnsignedShrReg(context, op, context.Negate(shiftLsB), round, eSize));
            context.Branch(lblEnd);

            context.MarkLabel(lbl1);
            context.BranchIf(lblEnd, shiftLsB, zero, Comparison.LessOrEqual);
            Operand shl = context.ShiftLeft(op, shiftLsB);
            Operand isGreaterOrEqual = context.ICompareGreaterOrEqual(shiftLsB, eSizeOp);
            context.Copy(res, context.ConditionalSelect(isGreaterOrEqual, zeroL, shl));
            context.Branch(lblEnd);

            context.MarkLabel(lblEnd);

            return res;
        }

        // long SignedShlRegSatQ(long op, int shiftLsB, bool round, int size);
        // ulong UnsignedShlRegSatQ(ulong op, int shiftLsB, bool round, int size);
        private static Operand EmitShlRegSatQ(ArmEmitterContext context, Operand op, Operand shiftLsB, bool round, int size, bool signed)
        {
            int eSize = 8 << size;

            Debug.Assert(op.Type == OperandType.I64);
            Debug.Assert(shiftLsB.Type == OperandType.I32);
            Debug.Assert(eSize == 8 || eSize == 16 || eSize == 32 || eSize == 64);

            Operand lbl1 = Label();
            Operand lbl2 = Label();
            Operand lblEnd = Label();

            Operand eSizeOp = Const(eSize);
            Operand zero = Const(0);

            Operand res = context.Copy(context.AllocateLocal(OperandType.I64), op);

            context.BranchIf(lbl1, shiftLsB, zero, Comparison.GreaterOrEqual);
            context.Copy(res, signed
                ? EmitSignedShrReg(context, op, context.Negate(shiftLsB), round, eSize)
                : EmitUnsignedShrReg(context, op, context.Negate(shiftLsB), round, eSize));
            context.Branch(lblEnd);

            context.MarkLabel(lbl1);
            context.BranchIf(lblEnd, shiftLsB, zero, Comparison.LessOrEqual);
            context.BranchIf(lbl2, shiftLsB, eSizeOp, Comparison.Less);
            context.Copy(res, signed
                ? EmitSignedSignSatQ(context, op, size)
                : EmitUnsignedSignSatQ(context, op, size));
            context.Branch(lblEnd);

            context.MarkLabel(lbl2);
            Operand shl = context.ShiftLeft(op, shiftLsB);
            if (eSize == 64)
            {
                Operand sarOrShr = signed
                    ? context.ShiftRightSI(shl, shiftLsB)
                    : context.ShiftRightUI(shl, shiftLsB);
                context.Copy(res, shl);
                context.BranchIf(lblEnd, sarOrShr, op, Comparison.Equal);
                context.Copy(res, signed
                    ? EmitSignedSignSatQ(context, op, size)
                    : EmitUnsignedSignSatQ(context, op, size));
            }
            else
            {
                context.Copy(res, signed
                    ? EmitSignedSrcSatQ(context, shl, size, signedDst: true)
                    : EmitUnsignedSrcSatQ(context, shl, size, signedDst: false));
            }
            context.Branch(lblEnd);

            context.MarkLabel(lblEnd);

            return res;
        }

        // shift := [1, 128]; eSize := {8, 16, 32, 64}.
        // long SignedShrReg(long op, int shift, bool round, int eSize);
        private static Operand EmitSignedShrReg(ArmEmitterContext context, Operand op, Operand shift, bool round, int eSize)
        {
            if (round)
            {
                Operand lblEnd = Label();

                Operand eSizeOp = Const(eSize);
                Operand zeroL = Const(0L);
                Operand one = Const(1);
                Operand oneL = Const(1L);

                Operand res = context.Copy(context.AllocateLocal(OperandType.I64), zeroL);

                context.BranchIf(lblEnd, shift, eSizeOp, Comparison.GreaterOrEqual);
                Operand roundConst = context.ShiftLeft(oneL, context.Subtract(shift, one));
                Operand add = context.Add(op, roundConst);
                Operand sar = context.ShiftRightSI(add, shift);
                if (eSize == 64)
                {
                    Operand shr = context.ShiftRightUI(add, shift);
                    Operand left = context.BitwiseAnd(context.Negate(op), context.BitwiseExclusiveOr(op, add));
                    Operand isLess = context.ICompareLess(left, zeroL);
                    context.Copy(res, context.ConditionalSelect(isLess, shr, sar));
                }
                else
                {
                    context.Copy(res, sar);
                }
                context.Branch(lblEnd);

                context.MarkLabel(lblEnd);

                return res;
            }
            else
            {
                Operand lblEnd = Label();

                Operand eSizeOp = Const(eSize);
                Operand zeroL = Const(0L);
                Operand negOneL = Const(-1L);

                Operand sar = context.ShiftRightSI(op, shift);
                Operand res = context.Copy(context.AllocateLocal(OperandType.I64), sar);

                context.BranchIf(lblEnd, shift, eSizeOp, Comparison.Less);
                Operand isLess = context.ICompareLess(op, zeroL);
                context.Copy(res, context.ConditionalSelect(isLess, negOneL, zeroL));
                context.Branch(lblEnd);

                context.MarkLabel(lblEnd);

                return res;
            }
        }

        // shift := [1, 128]; eSize := {8, 16, 32, 64}.
        // ulong UnsignedShrReg(ulong op, int shift, bool round, int eSize);
        private static Operand EmitUnsignedShrReg(ArmEmitterContext context, Operand op, Operand shift, bool round, int eSize)
        {
            if (round)
            {
                Operand lblEnd = Label();

                Operand zeroUL = Const(0UL);
                Operand one = Const(1);
                Operand oneUL = Const(1UL);
                Operand eSizeMaxOp = Const(64);
                Operand oneShl63UL = Const(1UL << 63);

                Operand res = context.Copy(context.AllocateLocal(OperandType.I64), zeroUL);

                context.BranchIf(lblEnd, shift, eSizeMaxOp, Comparison.Greater);
                Operand roundConst = context.ShiftLeft(oneUL, context.Subtract(shift, one));
                Operand add = context.Add(op, roundConst);
                Operand shr = context.ShiftRightUI(add, shift);
                Operand isEqual = context.ICompareEqual(shift, eSizeMaxOp);
                context.Copy(res, context.ConditionalSelect(isEqual, zeroUL, shr));
                if (eSize == 64)
                {
                    context.BranchIf(lblEnd, add, op, Comparison.GreaterOrEqualUI);
                    Operand right = context.BitwiseOr(shr, context.ShiftRightUI(oneShl63UL, context.Subtract(shift, one)));
                    context.Copy(res, context.ConditionalSelect(isEqual, oneUL, right));
                }
                context.Branch(lblEnd);

                context.MarkLabel(lblEnd);

                return res;
            }
            else
            {
                Operand lblEnd = Label();

                Operand eSizeOp = Const(eSize);
                Operand zeroUL = Const(0UL);

                Operand shr = context.ShiftRightUI(op, shift);
                Operand res = context.Copy(context.AllocateLocal(OperandType.I64), shr);

                context.BranchIf(lblEnd, shift, eSizeOp, Comparison.Less);
                context.Copy(res, zeroUL);
                context.Branch(lblEnd);

                context.MarkLabel(lblEnd);

                return res;
            }
        }
    }
}
