
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEditor.Scripting;
using UnityEditor.ShaderGraph;
using UnityEngine;

public class CPU : MonoBehaviour
{
    //Implement Fetch Decode Execute 
    public Bus bus;

    public byte Accumulator;
    public byte X;
    public byte Y;
    public byte StackPointer;
    public ushort ProgramCounter;

    public byte StatusRegister;


    public byte bm_ZeroFlag = 0b00000010;
    public byte bm_NegativeFlag = 0b10000000;


    public void SetFlag(byte Bitmask) => StatusRegister |= Bitmask;
    public void ClearFlag(byte Bitmask) => StatusRegister &= (byte)~Bitmask;

    // public Dictionary<byte, Instruction> Opcodes = new();

    // public List<Instruction> instructions;
    // private LDA LDA = new();

    void Start()
    {
        Debug.Log(bus == null);
        // instructions.Add(LDA);

        // foreach(Instruction instruction in instructions)
        // {
        //     instruction.Init(this);
        // }

        // Opcodes[LDA.Opcode] = LDA;




        ProgramCounter = 0x0000; //0x8000
        Accumulator = 0x0000;
        X = 0x0000;
        Y = 0x0000;
        StackPointer = 0xFD;
        StatusRegister = 0b00000100;

        // bus.Write(ProgramCounter , 0xA9);
        // bus.Write((ushort)(ProgramCounter + 1) , 3);

        // Step();

        // Debug.Log($"Value within Accumulator : {Accumulator}");
        // Debug.Log($"Value at Program Counter : {ProgramCounter}");

    }


    public void ConnectBus(Bus bus)
    {
        this.bus = bus;
    }


    public void Step()
    {

        // Remaining Addressing Modes for LDA, Flags. 
        // Stack
        //Remaining Functions 
        byte Opcode = bus.Read(ProgramCounter);

        //56 Different Instructions, the rest are addressing mode variatns . 
        switch (Opcode)
        {
            // LDA : 
            case 0xA9:
                Load(ref Accumulator, (ushort)(ProgramCounter + 1)); // Immediate
                manageLDAFlags();
                ProgramCounter += 2;

                //Wait 2 Clock Cycles
                return;

            case 0xA5:
                Load(ref Accumulator, bus.Read((ushort)(ProgramCounter + 1))); // Zero page (The next byte after the operand is the address to the value)
                manageLDAFlags();
                ProgramCounter += 2;
                return;


            case 0xB5:

                // byte value = bus.Read((ushort)((ushort)(ProgramCounter + 1) + X));
                // if (value > 255)
                // {

                // }


                ushort baseAddress = bus.Read((ushort)(ProgramCounter + 1)); // The operand of this instruction gives a memory address within the zero page 
                ushort indexedAddress = (ushort)((ushort)(baseAddress + X) % 256); //Indexed Address i found via adding the X Register and also performing a modulo operation to keep the address between 0-255.

                Load(ref Accumulator, bus.Read(indexedAddress)); // Zero Page X 
                manageLDAFlags();
                ProgramCounter += 2;
                return;


            case 0xAD:
                //Absolute, Absolute x ,y  needs 16 bit address construction. Check this on Wiki . 

                byte lowByte = bus.Read((ushort)(ProgramCounter + 1));
                byte highByte = bus.Read((ushort)(ProgramCounter + 2));
                ushort target = (ushort)((highByte << 8) | lowByte);

                Load(ref Accumulator, target); // Absolute
                manageLDAFlags();
                ProgramCounter += 3;
                return;


            case 0xBD:
                byte _LowByte = bus.Read((ushort)(ProgramCounter + 1));
                byte _HighByte = bus.Read((ushort)(ProgramCounter + 2));
                ushort FinalAddress = (ushort)(((_HighByte << 8) | _LowByte) + X);

                Load(ref Accumulator, FinalAddress); // Absolute X 
                manageLDAFlags();
                ProgramCounter += 3;
                return;

            case 0xB9:
                ushort address = (ushort)((bus.Read((ushort)(ProgramCounter + 2)) << 8) | (bus.Read((ushort)(ProgramCounter + 1))));
                ushort indexed = (ushort)(address + Y);
                Load(ref Accumulator, indexed); // Absolute Y 
                manageLDAFlags();
                ProgramCounter += 3;
                return;

                // case 0xA1 : 
                //     ushort address = bus.Read((ushort)((ushort)(ProgramCounter + 1) + X));
                //     Load(ref Accumulator , address);
        }
    }

    protected void Load(ref byte dst, ushort address) // LDA , LDX , LDY
    {
        dst = bus.Read(address);
    }

    private void manageLDAFlags()
    {
        if (Accumulator == 0)
        {
            SetFlag(bm_ZeroFlag);
            ClearFlag(bm_NegativeFlag);

        }

        else if (Accumulator > 127) // 127 Adds up bits 0-6 value place holders (1, 2, 4, 8 , 16....64) 
        {
            SetFlag(bm_NegativeFlag);
            ClearFlag(bm_ZeroFlag);
        }

        else
        {
            ClearFlag(bm_ZeroFlag);
            ClearFlag(bm_NegativeFlag);
        }
    }
}

public abstract class instruction
{
    // [Opcode][Operands][cycles][Length]

    public byte[] Opcodes; // i.e. LDA has 0xA9 , A5 , B5 , AD , BD, B9 , A1 , B1.
    public ushort Operands;
    public AddressingMode addressingMode;
    public byte cycles;
    public byte length;

    private CPU _cpu;

    public void GetCPUReference(CPU cpu) => _cpu = cpu;

    public void Execute()
    {
        Logic();
        ManageFlags();
        appendPC();
    }

    public ushort deduceOperands()
    {
        ushort result = 0;

        switch (addressingMode)
        {
            case AddressingMode.Immediate:
                // Return the address of the immediate value
                result = (ushort)(_cpu.ProgramCounter + 1);
                break;

            case AddressingMode.ZeroPage:
                // Operand itself is the zero-page address
                result = _cpu.bus.Read((ushort)(_cpu.ProgramCounter + 1));
                break;

            case AddressingMode.ZeroPageX:
                // Zero-page indexing wraps around $FF → $00
                result = (ushort)(
                    (_cpu.bus.Read((ushort)(_cpu.ProgramCounter + 1)) + _cpu.X) % 256
                );
                break;

            case AddressingMode.ZeroPageY:
                // Zero-page indexing wraps around $FF → $00
                result = (ushort)(
                    (_cpu.bus.Read((ushort)(_cpu.ProgramCounter + 1)) + _cpu.Y) % 256
                );
                break;

            case AddressingMode.Abs:
                {
                    byte lowByte = _cpu.bus.Read((ushort)(_cpu.ProgramCounter + 1));
                    byte highByte = _cpu.bus.Read((ushort)(_cpu.ProgramCounter + 2));

                    result = (ushort)((highByte << 8) | lowByte);
                    break;
                }

            case AddressingMode.AbsX:
                {
                    byte lowByte = _cpu.bus.Read((ushort)(_cpu.ProgramCounter + 1));
                    byte highByte = _cpu.bus.Read((ushort)(_cpu.ProgramCounter + 2));

                    ushort baseAddress = (ushort)((highByte << 8) | lowByte);

                    result = (ushort)(baseAddress + _cpu.X);
                    break;
                }

            case AddressingMode.AbsY:
                {
                    byte lowByte = _cpu.bus.Read((ushort)(_cpu.ProgramCounter + 1));
                    byte highByte = _cpu.bus.Read((ushort)(_cpu.ProgramCounter + 2));

                    ushort baseAddress = (ushort)((highByte << 8) | lowByte);

                    result = (ushort)(baseAddress + _cpu.Y);
                    break;
                }

            case AddressingMode.Accumulator:
                // The operand is the accumulator itself
                result = _cpu.Accumulator;
                break;

            case AddressingMode.Implied:
                // No operand
                result = 0;
                break;

            case AddressingMode.Relative:
                {
                    // Branch offset is signed 8-bit
                    sbyte offset = (sbyte)_cpu.bus.Read(
                        (ushort)(_cpu.ProgramCounter + 1)
                    );

                    result = (ushort)(_cpu.ProgramCounter + 2 + offset);
                    break;
                }

            case AddressingMode.Indirect:
                {
                    // First construct the pointer address
                    byte lowByte = _cpu.bus.Read((ushort)(_cpu.ProgramCounter + 1));
                    byte highByte = _cpu.bus.Read((ushort)(_cpu.ProgramCounter + 2));

                    ushort pointer = (ushort)((highByte << 8) | lowByte);

                    // Then read the actual target address from that pointer
                    byte targetLow = _cpu.bus.Read(pointer);
                    byte targetHigh = _cpu.bus.Read((ushort)(pointer + 1));

                    result = (ushort)((targetHigh << 8) | targetLow);
                    break;
                }

            case AddressingMode.IndirectX:
                {
                    // (Indirect,X)
                    byte zeroPageAddress = _cpu.bus.Read(
                        (ushort)(_cpu.ProgramCounter + 1)
                    );

                    byte pointer = (byte)(zeroPageAddress + _cpu.X);

                    byte lowByte = _cpu.bus.Read(pointer);
                    byte highByte = _cpu.bus.Read((byte)(pointer + 1));

                    result = (ushort)((highByte << 8) | lowByte);
                    break;
                }

            case AddressingMode.IndirectY:
                {
                    // (Indirect),Y
                    byte zeroPagePointer = _cpu.bus.Read(
                        (ushort)(_cpu.ProgramCounter + 1)
                    );

                    byte lowByte = _cpu.bus.Read(zeroPagePointer);
                    byte highByte = _cpu.bus.Read((byte)(zeroPagePointer + 1));

                    ushort baseAddress = (ushort)((highByte << 8) | lowByte);

                    result = (ushort)(baseAddress + _cpu.Y);
                    break;
                }
        }

        return result;
    }



    public abstract void Logic();
    public abstract void ManageFlags();
    public abstract void appendPC();
}

public class i_LDA : instruction
{
    
    // Port the switch case statement above here
    public override void Logic()
    {
        throw new System.NotImplementedException();
    }

    public override void ManageFlags()
    {
        throw new System.NotImplementedException();
    }

    public override void appendPC()
    {
        throw new System.NotImplementedException();
    }
}