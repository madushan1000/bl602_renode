using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Timers;
using Endianess = ELFSharp.ELF.Endianess;
using Antmicro.Renode.Peripherals.CPU;
using System.Collections.Generic;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Utilities.Binding;
using System;
using Antmicro.Renode.Utilities;
using Antmicro.Renode.Peripherals.IRQControllers;

namespace Antmicro.Renode.Peripherals.CPU {
    public class RiscV32CLIC: RiscV32 {
        public RiscV32CLIC(Machine machine, string cpuType, IRiscVTimeProvider timeProvider, CoreLocalInterruptController clic) : base(timeProvider, cpuType, machine, 0, PrivilegeArchitecture.Priv1_10)
        {
            this.clic = clic;
            CSRValidation = CSRValidationLevel.None;

            var registersMap = new Dictionary<long, DoubleWordRegister>();
            registersMap[(long)CSRs.MCAUSE] = new DoubleWordRegister(this)
                .WithValueField(0, 9, name: "EXCCODE", valueProviderCallback: _ => {
                    this.Log(LogLevel.Warning, $"MCAUSE EXCCODE is getting read. Sending IrqId: {IrqId}");
                    return IrqId;
                })
                .WithValueField(10, 5, name: "Reserved")
                .WithValueField(16, 8, name: "MPIL")
                .WithValueField(24, 3, name: "Reserved")
                .WithFlag(27, name: "MPIE", valueProviderCallback: _ => BitHelper.IsBitSet((ulong)MSTATUS, 7))
                .WithValueField(28, 2, name: "MPP", valueProviderCallback: _ => (uint)BitHelper.GetValue((ulong)MSTATUS, 11, 2))
                .WithFlag(30, name: "MINHV", valueProviderCallback: _=> false)
                .WithFlag(31, name: "Interrupt", valueProviderCallback: _ => isInterruptPending);
                //{
                //     if (IrqId == 11) //ecall to M-mode
                //     {
                //         return false;
                //     }
                //     else //External interrupts
                //     {
                //         return true;
                //     }
                // });

            var registers = new DoubleWordRegisterCollection(this, registersMap);

            TlibSetReturnOnException(1);

            RegisterCSR((ulong)CSRs.MCAUSE, () => registers.Read((long)CSRs.MCAUSE), value => registers.Write((long)CSRs.MCAUSE, (uint)value));
            InstallCustomInstruction(pattern: "00000000000000000000000001110011", handler: HandleEcallInstruction);
            InstallCustomInstruction(pattern: "00110000001000000000000001110011", handler: HandleMretInstruction);
            //InstallCustomInstruction(pattern: "0000100-----00000---ddddd0001011", handler: HandleWaitirqInstruction);
            
        }

        // protected override bool ExecutionFinished(TranslationCPU.ExecutionResult result)
        // {
        //     this.Log(LogLevel.Warning, "PC@0x{1:X}: Execution finished with result: {0}", result, PC.RawValue);

        //     lock(irqLock)
        //     {
        //         switch((Result)result)
        //         {
        //             case Result.IllegalInstruction:
        //             case Result.EBreak:
        //             case Result.ECall:
        //                 this.Log(LogLevel.Warning, "caught ECALL");
        //                 //pendingInterrupts |= (1u << EBreakECallIllegalInstructionInterruptSource);
        //                 break;

        //             case Result.LoadAddressMisaligned:
        //             case Result.StoreAddressMisaligned:
        //                 //pendingInterrupts |= (1u << UnalignedMemoryAccessInterruptSource);
        //                 this.Log(LogLevel.Warning, "LoadAddressMisaligned StoreAddressMisaligned result: {0}", result);
        //                 break;

        //             case (Result)TranslationCPU.ExecutionResult.Ok:
        //                 // to avoid warning
        //                 this.Log(LogLevel.Warning, "caught OK");
        //                 break;

        //             default:
        //                 this.Log(LogLevel.Warning, "Unexpected execution result: {0}", result);
        //                 break;
        //         }

        //         // if(IrqIsPending(out var interruptsToHandle))
        //         // {
        //         //     qRegisters[0] = PC;
        //         //     qRegisters[1] = interruptsToHandle;
        //         //     pendingInterrupts &= ~interruptsToHandle;
        //         //     PC = resetVectorAddress;
        //         //     interruptsMasked = true;

        //         //     this.Log(LogLevel.Noisy, "Entering interrupt, return address: 0x{0:X}, interrupts: 0x{1:X}", qRegisters[0], qRegisters[1]);
        //         // }
        //     }

        //     return base.ExecutionFinished(result);
        // }
        public override void OnGPIO(int number, bool value) {
            this.Log(LogLevel.Warning, "External interrupt #{0} set to {1}", number, value);
            lock(irqLock)
            {
                bool isInterruptsEnabled = BitHelper.IsBitSet((ulong)MSTATUS, 3); //MIE
                if (number == 11) //from CLIC
                {
                    isInterruptPending = true;
                    if(value)
                        irqId = clic.pendingIrq.Id;
                    if(isInterruptsEnabled)
                        doInterrupt();
                }
                else if (number == 7) //timer
                {
                    isInterruptPending = true;
                    IrqId = 7;
                    if(isInterruptsEnabled)
                        doInterrupt();
                    base.OnGPIO(number, value);
                }
                else {
                   //base.OnGPIO(number, value);
                }
            }
        }
        private void HandleEcallInstruction(UInt64 opcode)
        {
            this.Log(LogLevel.Warning, "Ecall invoked");
            lock(irqLock) {
                isInterruptPending = false;
                irqId = 11;
                doInterrupt();
            }
        }

        private void HandleMretInstruction(UInt64 opcode)
        {
            this.Log(LogLevel.Warning, $"Mret invoked PC: {PC} MEPC: {MEPC}");
            isInterruptPending = false;
            var mstatus = (ulong)MSTATUS;
            var mpie = BitHelper.IsBitSet(mstatus, 7);
            BitHelper.SetBit(ref mstatus, 3, mpie);
            MSTATUS = mstatus;

            PCWritten();
            PC = MEPC;
            TlibSetReturnRequest();
        }
        // private void HandleWaitirqInstruction(UInt64 opcode)
        // {
        //     TlibEnterWfi();
        // }
        private void doInterrupt()
        {
            this.Log(LogLevel.Warning, $"irqId: {irqId} PC: {PC} MEPC: {MEPC}");

            var mstatus = (ulong)MSTATUS;
            var mie = BitHelper.IsBitSet(mstatus, 3);
            BitHelper.SetBit(ref mstatus, 7, mie);
            BitHelper.SetBit(ref mstatus, 3, false);
            MSTATUS = mstatus;

            MEPC = PC; //backup PC
            PCWritten();
            PC = MTVEC;

            TlibSetReturnRequest();
            //if (TlibTsWfi() != 0) {
                //TlibCleanWfiProcState();
            //} 
        }
        [Import]
        private FuncInt32Int32 TlibSetReturnOnException;
        // [Import]
        // private Action TlibCleanWfiProcState;
        // [Import]
        // private FuncInt32 TlibTsWfi;
        private readonly object irqLock = new object();
        public uint IrqId {
            get 
            {
                lock(irqLock)
                {
                    return irqId;
                }
            }
            set
            {
                this.Log(LogLevel.Warning, $"irqid is: {irqId} setting irqId to {value}");
                lock(irqLock)
                { 
                    irqId = value;
                }
            }
        }
        private CoreLocalInterruptController clic;
        private uint irqId;
        private bool isInterruptPending;
        //public new RegisterValue MIE => 0; //Not in clic mode
        //public new RegisterValue MIP => 0; //Not in clic mode
        private enum CSRs
        {
            //MSTATUS = 0x300
            MCAUSE = 0x342
        }

        private enum Result
        {
            IllegalInstruction = 0x2,
            EBreak = 0x3,
            LoadAddressMisaligned = 0x4,
            StoreAddressMisaligned = 0x6,
            ECall = 0x8
        }
    }
}