using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Utilities;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Peripherals.IRQControllers.CLIC;

namespace Antmicro.Renode.Peripherals.IRQControllers {
    public class CoreLocalInterruptController : IBytePeripheral, IIRQController, INumberedGPIOOutput, IKnownSize  {
        public CoreLocalInterruptController(Machine machine, int numberOfSources, bool prioritiesEnabled = true, uint extraInterrupts = 0) {
            this.machine = machine;
            var registersMap = new Dictionary<long, ByteRegister>();
            var connections = new Dictionary<int, IGPIO>();
            irqSources = new IrqSource[numberOfSources + 16];

            for(var i = 0; i < extraInterrupts + 1; i++)
            {
                connections[i] = new GPIO();
            }
            Connections = connections;

            this.Log(LogLevel.Warning, $"gpio init done {connections.Count}");

            this.Log(LogLevel.Warning, $"irqSources.Length {irqSources.Length}");

            for(uint i = 0; i <= irqSources.Length - 1; i++)
            {
                var j = i;
                irqSources[i] = new IrqSource(i, this);
                registersMap[(long)Registers.clicIntIP + i] = new ByteRegister(this)
                    .WithFlag(0, 
                    name: $"clicIntIP{i}", 
                    valueProviderCallback: _ => irqSources[i].IsPending
                    );
                this.Log(LogLevel.Warning, $"added register clicIntIP{j} @ {(long)Registers.clicIntIP + i}");
                registersMap[(long)Registers.clicIntIE + i] = new ByteRegister(this)
                    .WithFlag(0,
                    name: $"clicIntIE{i}",
                    valueProviderCallback: _ => irqSources[i].IsEnabled,
                    writeCallback: (_, value) => {
                        this.Log(LogLevel.Warning, $"trying to write {value} to {j}");
                        irqSources[j].IsEnabled = value;
                        this.Log(LogLevel.Warning, $"wrote {value}");
                        //UpdateInterrupts();
                    });
                this.Log(LogLevel.Warning, $"added register clicIntIE{j} @ {(long)Registers.clicIntIE + i}");
            }
            registers = new ByteRegisterCollection(this, registersMap);
            this.Log(LogLevel.Warning, $"register init done{registers.ToString()}");
        }

        private void UpdateInterrupts() {
            // foreach(var irqSource in irqSources){
            //     this.Log(LogLevel.Warning, $"{irqSource}");
            // }         
            pendingIrq = irqSources.Where(x => x.IsPending && x.IsEnabled)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.Id).FirstOrDefault();
            this.Log(LogLevel.Warning, $"pendingIrq: {pendingIrq}");
            if (pendingIrq != null) { 
                Connections[0].Set();
            }
        }
        public void OnGPIO(int number, bool value) {
            if (irqSources.Length > 0)
            {
                irqSources[number].IsPending = value;
            }
            UpdateInterrupts();
        }

        public byte ReadByte(long offset)
        {
            var value = registers.Read(offset);
            return (byte)value;
        }

        public void WriteByte(long offset, byte value)
        {
            registers.Write(offset, value);
        }

        public void Reset()
        {
            //throw new NotImplementedException();
        }

        private ByteRegisterCollection registers;
        public long Size {get; } = 0xC01;
        public IReadOnlyDictionary<int, IGPIO> Connections { get; }
        private Machine machine;
        readonly IrqSource[] irqSources;
        public IrqSource pendingIrq;
        private enum Registers : long
        {
            //1B(byte) per Interrupt ID
            clicIntIP = 0x000,
            //
            clicIntIE = 0x400,
            //
            clicIntCfg = 0x800,
            cliccfg = 0xC00
        }
    }
}

namespace Antmicro.Renode.Peripherals.IRQControllers.CLIC {
    public class IrqSource
    {
        public IrqSource(uint id, CoreLocalInterruptController irqController)
        {
            this.parent = irqController;

            Id = id;
            Reset();
        }
        public override string ToString()
        {
            return $"IrqSource id: {Id}, priority: {Priority}, state: {State}, pending: {IsPending}";
        }

        public void Reset()
        {
            Priority = DefaultPriority;
            State = false;
            IsPending = false;
        }

        public uint Id { get; private set; }

        public uint Priority
        {
             get { return priority; }
             set
             {
                 if(value == priority)
                 {
                     return;
                 }

                 parent.Log(LogLevel.Noisy, "Setting priority {0} for source #{1}", value, Id);
                 priority = value;
             }
        }

        public bool State
        {
            get { return state; }
            set
            {
                if(value == state)
                {
                    return;
                }

                state = value;
                parent.Log(LogLevel.Noisy, "Setting state to {0} for source #{1}", value, Id);
            }
        }
        public bool IsPending
        {
            get { return isPending; }
            set
            {
                if(value == isPending)
                {
                    return;
                }

                isPending = value;
                parent.Log(LogLevel.Noisy, "Setting pending status to {0} for source #{1}", value, Id);
            }
        }
        public bool IsEnabled
        {
            get {return isEnabled; }
            set
            {
                if(value == isEnabled)
                {
                    return;
                }

                isEnabled = value;
                parent.Log(LogLevel.Noisy, "Setting pending status to {0} for source #{1}", value, Id);
            }
        }

        private uint priority;
        private bool state;
        private bool isPending;
        private bool isEnabled;

        private readonly CoreLocalInterruptController parent;

        // 1 is the default, lowest value. 0 means "no interrupt".
        private const uint DefaultPriority = 1;
    }


}
