//
// Copyright (c) 2010-2018 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System.Collections.Generic;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;

namespace Antmicro.Renode.Peripherals.UART
{
    public class BL_UART : UARTBase, IDoubleWordPeripheral, IBytePeripheral, IKnownSize
    {
        public BL_UART(Machine machine) : base(machine)
        {
            IRQ = new GPIO();
            var registersMap = new Dictionary<long, DoubleWordRegister>
            {
                {(long)Registers.uart_fifo_config_1, new DoubleWordRegister(this)
                    .WithValueField(0, 8, FieldMode.Read, valueProviderCallback: _ =>  1)
                    .WithValueField(8, 8, FieldMode.Read, valueProviderCallback: _ => 1)
                },
                {(long)Registers.uart_fifo_wdata, new DoubleWordRegister(this)
                    .WithValueField(0, 8, FieldMode.Write, writeCallback: (_, value) => this.TransmitCharacter((byte)value))
                },
                {(long)Registers.uart_fifo_rdata, new DoubleWordRegister(this)
                    .WithValueField(0, 8, FieldMode.Read, valueProviderCallback: _ => {
                        this.Log(LogLevel.Warning, "reading text");
                        if(!TryGetCharacter(out var character))
                            {
                                this.Log(LogLevel.Warning, "Trying to read from an empty Rx FIFO.");
                            }
                            return character;
                    })
                },
                {(long)Registers.uart_int_sts, new DoubleWordRegister(this)
                    .WithFlag(1, FieldMode.Read, name: "urx_end_int", valueProviderCallback: _ => {
                            this.Log(LogLevel.Warning, "urx_end_int gettring read");
                            return true;
                        })
                },
            };

            registers = new DoubleWordRegisterCollection(this, registersMap);
        }

        public uint ReadDoubleWord(long offset)
        {
            return registers.Read(offset);
        }

        public byte ReadByte(long offset)
        {
            if(offset % 4 != 0)
            {
                // in the current configuration, only the lowest byte
                // contains a meaningful data
                return 0;
            }
            return (byte)ReadDoubleWord(offset);
        }

        public override void Reset()
        {
            base.Reset();
            registers.Reset();

            UpdateInterrupts();
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            registers.Write(offset, value);
        }
         
        public void WriteByte(long offset, byte value)
        {
            if(offset % 4 != 0)
            {
                // in the current configuration, only the lowest byte
                // contains a meaningful data
                return;
            }

            WriteDoubleWord(offset, value);
        }
        
        public long Size => 0x8c;

        public GPIO IRQ { get; private set; }

        public override Bits StopBits => Bits.One;

        public override Parity ParityBit => Parity.None;

        public override uint BaudRate => 115200;

        protected override void CharWritten()
        {
            UpdateInterrupts();
        }

        protected override void QueueEmptied()
        {
            UpdateInterrupts();
        }

        private void UpdateInterrupts()
        {
            // rxEventPending is latched
            //urx_end_int.Value = (Count != 0);
            //rxEventEnabled.Value = true;

            // tx fifo is never full, so `txEventPending` is always false
            //var eventPending = (/*rxEventEnabled.Value &&*/ urx_end_int.Value);
            //this.Log(LogLevel.Warning, $"eventPending:{eventPending}");
            IRQ.Set();
        }

        private IFlagRegisterField rxEventEnabled;
        private IFlagRegisterField urx_end_int;
        private readonly DoubleWordRegisterCollection registers;

        private enum Registers : long
        {
            utx_config = 0x00,
            urx_config = 0x04,
            uart_bit_prd = 0x08,
            data_config = 0x0c,
            utx_ir_position = 0x10,
            urx_ir_position = 0x14,
            urx_rto_timer = 0x18,
            uart_int_sts = 0x20,
            uart_int_mask = 0x24,
            uart_int_clear = 0x28,
            uart_int_en = 0x2c,
            uart_status = 0x30,
            sts_urx_abr_prd = 0x34,
            uart_fifo_config_0 = 0x80,
            uart_fifo_config_1 = 0x84,
            uart_fifo_wdata = 0x88,
            uart_fifo_rdata = 0x8c,
        }
    }
}