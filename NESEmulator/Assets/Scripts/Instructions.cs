// public abstract class Instruction
// {
//     public CPU CPU ;
//     public Bus bus => CPU.bus;


//     public byte Opcode ; 
//     public byte OperandA = 0x0000;
//     public byte OperandB = 0x0000;  
//     public ushort cycles;
//     public ushort length;

//     public AddressingMode addressingMode ; 
//     public abstract void Method();



//     public abstract void Init(CPU _cpu);

//     protected void Load(ref byte dst, ushort address) // LDA , LDX , LDY
//     {
//         dst = bus.Read(address);
//     }

// }
