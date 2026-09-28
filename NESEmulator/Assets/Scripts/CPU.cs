using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions.Must;

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


    public void OnEnable()
    {
        Ticker.OnTick += Step;
    }

    void Start()
    {        
        // instructions.Add(LDA);

        // foreach(Instruction instruction in instructions)
        // {
        //     instruction.Init(this);
        // }

        // Opcodes[LDA.Opcode] = LDA;




        ProgramCounter = 0x8000;
        Accumulator = 0x0000;
        X = 0x0000;
        Y = 0x0000;
        StackPointer = 0xFD;
        StatusRegister = 0b00000100;
    }


    public void ConnectBus(Bus bus)
    {
        this.bus = bus;
    }


    public void Step()
    {
                
        byte Opcode = bus.Read(ProgramCounter);
        
        switch(Opcode)
        {
            case 0xA9:
                Load(ref Accumulator , bus.Read((ushort)(ProgramCounter + 1))); // Immediate
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
