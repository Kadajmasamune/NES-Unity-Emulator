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
    public Bus bus;

    public byte Accumulator;
    public byte X;
    public byte Y;
    public byte StackPointer;
    public ushort ProgramCounter;

    public byte StatusRegister;



    public Dictionary<byte, Instruction> Opcodes = new();

    public List<Instruction> instructions;


    private LDA LDA = new();

    
    void Start()
    {        
        instructions.Add(LDA);

        foreach(Instruction instruction in instructions)
        {
            instruction.Init(this);
        }

        Opcodes[0xA9] = LDA;
    }


    public void ConnectBus(Bus bus)
    {
        this.bus = bus;
    }



}

public class LDA : Instruction
{
    public override void Init(CPU _cpu)
    {
        CPU = _cpu;
        cycles = 2;
        length = 2;
        addressingMode = AddressingMode.Immediate;
    }

    public override void Method()
    {
        Load(ref CPU.Accumulator , 0x800);
    }
}