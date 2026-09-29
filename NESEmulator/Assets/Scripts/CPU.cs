
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

                
                //Research and Implement Wrap arounds (Study what the zero page is too)
                Load(ref Accumulator , bus.Read((ushort)((ushort)(ProgramCounter + 1) + X))); // Zero Page X 
                manageLDAFlags();
                ProgramCounter += 2;
                return ; 


            case 0xAD : 
                //Absolute, Absolute x ,y  needs 16 bit address construction. Check this on Wiki . 
                Load(ref Accumulator , bus.Read((ushort)(ProgramCounter + 1 ))); // Absolute
                manageLDAFlags();
                ProgramCounter += 3; 
                return;

            case 0xBD : 
                Load(ref Accumulator , bus.Read((ushort)((ushort)(ProgramCounter + 1) + X))); // Absolute X 
                manageLDAFlags();
                ProgramCounter += 3 ;
                return ; 

            case 0xB9 : 
                Load(ref Accumulator , bus.Read((ushort)((ushort)(ProgramCounter + 1) + Y))); // Absolute Y 
                manageLDAFlags(); 
                ProgramCounter += 3 ;
                return ; 

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
