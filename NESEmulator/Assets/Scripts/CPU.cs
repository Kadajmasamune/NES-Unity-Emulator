
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
        
        switch(Opcode)
        {
            // LDA : 
            case 0xA9:
                Load(ref Accumulator , (ushort)(ProgramCounter + 1)); // Immediate
                ProgramCounter += 2 ; 
                //Wait 2 Clock Cycles
                return;

            case 0xA5 : 
                Load(ref Accumulator, bus.Read(bus.Read((ushort)(ProgramCounter + 1))));
                ProgramCounter += 2 ; 
                return ; 

        }
    }

    protected void Load(ref byte dst, ushort address) // LDA , LDX , LDY
    {
        dst = bus.Read(address);
    }
}
