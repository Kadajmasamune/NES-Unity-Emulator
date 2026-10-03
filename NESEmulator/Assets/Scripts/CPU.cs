using System;
using UnityEngine;

/// <summary>
/// MOS 6502 CPU core (NES variant - no decimal mode in practice, but the flag exists).
/// Table-driven design: a 256-entry opcode table maps each byte to a
/// (name, operation, addressing-mode, base-cycles) tuple. This is the standard
/// architecture for a 6502 emulator - every instruction and addressing mode is just
/// a method on CPU, bound into the table via delegates. No per-instruction classes,
/// no back-references needed.
/// </summary>
public class CPU : MonoBehaviour
{
    public Bus bus;

    // ----- Registers -----
    public byte Accumulator;
    public byte X;
    public byte Y;
    public byte StackPointer;
    public ushort ProgramCounter;
    public byte StatusRegister;

    // ----- Status flag bitmasks -----
    public const byte bm_CarryFlag             = 1 << 0; // C
    public const byte bm_ZeroFlag              = 1 << 1; // Z
    public const byte bm_InterruptDisableFlag  = 1 << 2; // I
    public const byte bm_DecimalModeFlag       = 1 << 3; // D (unused by NES PPU/APU logic, but still a real flag)
    public const byte bm_BreakFlag             = 1 << 4; // B
    public const byte bm_UnusedFlag            = 1 << 5; // U (always 1 on real hardware)
    public const byte bm_OverflowFlag          = 1 << 6; // V
    public const byte bm_NegativeFlag          = 1 << 7; // N

    public bool GetFlag(byte Bitmask) => (StatusRegister & Bitmask) != 0;
    public void SetFlag(byte Bitmask) => StatusRegister |= Bitmask;
    public void ClearFlag(byte Bitmask) => StatusRegister &= (byte)~Bitmask;
    public void SetFlag(byte Bitmask, bool condition)
    {
        if (condition) SetFlag(Bitmask);
        else ClearFlag(Bitmask);
    }

    // ----- Internal execution state -----
    private byte fetched;      // value fetched for the current instruction
    private ushort addr_abs;   // resolved absolute address for the current instruction
    private ushort addr_rel;   // resolved relative offset (branches)
    private byte opcode;       // currently executing opcode
    private byte cycles;       // cycles remaining for the currently executing instruction

    // Real-time clocking (optional - lets Update() run the CPU at NES speed instead
    // of needing something external to call Clock()/Step() manually).
    public bool autoRun = false;
    public float clockHz = 1789773f; // NTSC NES CPU clock speed
    private float cycleAccumulator = 0f;

    private struct INSTRUCTION
    {
        public string Name;
        public Func<byte> Operate;
        public Func<byte> AddrMode;
        public byte Cycles;

        public INSTRUCTION(string name, Func<byte> operate, Func<byte> addrMode, byte cycles)
        {
            Name = name;
            Operate = operate;
            AddrMode = addrMode;
            Cycles = cycles;
        }
    }

    private INSTRUCTION[] lookup;

    void Awake()
    {
    
        BuildLookupTable();
    }

    void Start()
    {
        if (bus != null)
        {
          
            Reset();
        }
    }

    public void Update()
    {
        if (!autoRun || bus == null) return;

        cycleAccumulator += Time.deltaTime * clockHz;

        // Clock hz is cycles / second. 
        // so second * cycles / second => cycles. 
        // Approxx... 29,829.5 Cycles every 1/60th of a second. 
        while (cycleAccumulator >= 1f) // Checks if we can owe at least one more cycle.
        {
            Clock();
            cycleAccumulator -= 1f;
        }
    }

    public void ConnectBus(Bus bus)
    {
        this.bus = bus;
    }

    private byte Read(ushort address) => bus.Read(address);
    private void Write(ushort address, byte data) => bus.Write(address, data);

    private void Push(byte data)
    {
        Write((ushort)(0x0100 + StackPointer), data);
        StackPointer--;
    }

    private byte Pop()
    {
        StackPointer++;
        return Read((ushort)(0x0100 + StackPointer));
    }

    // ---------------------------------------------------------------
    // Signals
    // ---------------------------------------------------------------

    public void Reset()
    {
        addr_abs = 0xFFFC;
        byte lo = Read(addr_abs);
        byte hi = Read((ushort)(addr_abs + 1));
        ProgramCounter = (ushort)((hi << 8) | lo);

        Accumulator = 0;
        X = 0;
        Y = 0;
        StackPointer = 0xFD;
        StatusRegister = bm_UnusedFlag;

        addr_rel = 0;
        addr_abs = 0;
        fetched = 0;

        cycles = 8;
    }

    public void IRQ()
    {
        if (GetFlag(bm_InterruptDisableFlag)) return;

        Push((byte)((ProgramCounter >> 8) & 0x00FF));
        Push((byte)(ProgramCounter & 0x00FF));

        ClearFlag(bm_BreakFlag);
        SetFlag(bm_UnusedFlag);
        SetFlag(bm_InterruptDisableFlag);
        Push(StatusRegister);

        addr_abs = 0xFFFE;
        byte lo = Read(addr_abs);
        byte hi = Read((ushort)(addr_abs + 1));
        ProgramCounter = (ushort)((hi << 8) | lo);

        cycles = 7;
    }

    public void NMI()
    {
        Push((byte)((ProgramCounter >> 8) & 0x00FF));
        Push((byte)(ProgramCounter & 0x00FF));

        ClearFlag(bm_BreakFlag);
        SetFlag(bm_UnusedFlag);
        SetFlag(bm_InterruptDisableFlag);
        Push(StatusRegister);

        addr_abs = 0xFFFA;
        byte lo = Read(addr_abs);
        byte hi = Read((ushort)(addr_abs + 1));
        ProgramCounter = (ushort)((hi << 8) | lo);

        cycles = 8;
    }

    /// <summary>Advance exactly one clock cycle. When the current instruction's cycles
    /// run out, fetch/decode/execute the next one.</summary>
    public void Clock()
    {
        if (cycles == 0)
        {
            opcode = Read(ProgramCounter);
            SetFlag(bm_UnusedFlag);
            ProgramCounter++;

            INSTRUCTION instruction = lookup[opcode];
            cycles = instruction.Cycles;

            byte extraFromAddrMode = instruction.AddrMode();
            byte extraFromOperate = instruction.Operate();

            cycles += (byte)(extraFromAddrMode & extraFromOperate);

            SetFlag(bm_UnusedFlag);
        }

        cycles--;
    }

    /// <summary>Runs whole instructions at a time (ignores cycle-accurate timing).
    /// Useful for simple tests / step-debugging.</summary>
    public void Step()
    {
        Clock();
        while (cycles > 0)
        {
            Clock();
        }
    }

    // ---------------------------------------------------------------
    // Addressing modes
    // Each sets addr_abs (or addr_rel for Relative) and returns 1 if the
    // addressing mode *might* need an extra cycle (page-cross), else 0.
    // ---------------------------------------------------------------

    private byte IMP() { fetched = Accumulator; return 0; }
    private byte IMM() { addr_abs = ProgramCounter++; return 0; }

    private byte ZP0()
    {
        addr_abs = Read(ProgramCounter++);
        addr_abs &= 0x00FF;
        return 0;
    }

    private byte ZPX()
    {
        addr_abs = (ushort)(Read(ProgramCounter++) + X);
        addr_abs &= 0x00FF;
        return 0;
    }

    private byte ZPY()
    {
        addr_abs = (ushort)(Read(ProgramCounter++) + Y);
        addr_abs &= 0x00FF;
        return 0;
    }

    private byte ABS()
    {
        ushort lo = Read(ProgramCounter++);
        ushort hi = Read(ProgramCounter++);
        addr_abs = (ushort)((hi << 8) | lo);
        return 0;
    }

    private byte ABX()
    {
        ushort lo = Read(ProgramCounter++);
        ushort hi = Read(ProgramCounter++);
        addr_abs = (ushort)((hi << 8) | lo);
        addr_abs += X;
        return ((addr_abs & 0xFF00) != (hi << 8)) ? (byte)1 : (byte)0;
    }

    private byte ABY()
    {
        ushort lo = Read(ProgramCounter++);
        ushort hi = Read(ProgramCounter++);
        addr_abs = (ushort)((hi << 8) | lo);
        addr_abs += Y;
        return ((addr_abs & 0xFF00) != (hi << 8)) ? (byte)1 : (byte)0;
    }

    private byte IND()
    {
        ushort ptrLo = Read(ProgramCounter++);
        ushort ptrHi = Read(ProgramCounter++);
        ushort ptr = (ushort)((ptrHi << 8) | ptrLo);

        if (ptrLo == 0x00FF) // page-boundary hardware bug
        {
            addr_abs = (ushort)((Read((ushort)(ptr & 0xFF00)) << 8) | Read(ptr));
        }
        else
        {
            addr_abs = (ushort)((Read((ushort)(ptr + 1)) << 8) | Read(ptr));
        }
        return 0;
    }

    private byte IZX()
    {
        ushort t = Read(ProgramCounter++);
        ushort lo = Read((ushort)((t + X) & 0x00FF));
        ushort hi = Read((ushort)((t + X + 1) & 0x00FF));
        addr_abs = (ushort)((hi << 8) | lo);
        return 0;
    }

    private byte IZY()
    {
        ushort t = Read(ProgramCounter++);
        ushort lo = Read((ushort)(t & 0x00FF));
        ushort hi = Read((ushort)((t + 1) & 0x00FF));
        addr_abs = (ushort)((hi << 8) | lo);
        addr_abs += Y;
        return ((addr_abs & 0xFF00) != (hi << 8)) ? (byte)1 : (byte)0;
    }

    private byte REL()
    {
        addr_rel = Read(ProgramCounter++);
        if ((addr_rel & 0x80) != 0) addr_rel |= 0xFF00;
        return 0;
    }

    private byte Fetch()
    {
        bool isImplied = lookup[opcode].AddrMode == IMP;
        if (!isImplied)
        {
            fetched = Read(addr_abs);
        }
        return fetched;
    }

    private void SetZN(byte value)
    {
        SetFlag(bm_ZeroFlag, value == 0);
        SetFlag(bm_NegativeFlag, (value & 0x80) != 0);
    }

    // ---------------------------------------------------------------
    // Instructions (official 6502 opcodes)
    // Each returns 1 if it *can* take an extra cycle (combined with the
    // addressing mode's result via AND, matching real 6502 behaviour),
    // else 0.
    // ---------------------------------------------------------------

    private byte ADC()
    {
        Fetch();
        ushort sum = (ushort)(Accumulator + fetched + (GetFlag(bm_CarryFlag) ? 1 : 0));
        SetFlag(bm_CarryFlag, sum > 255);
        SetFlag(bm_ZeroFlag, (sum & 0x00FF) == 0);
        SetFlag(bm_OverflowFlag, (~(Accumulator ^ fetched) & (Accumulator ^ sum) & 0x80) != 0);
        SetFlag(bm_NegativeFlag, (sum & 0x80) != 0);
        Accumulator = (byte)(sum & 0x00FF);
        return 1;
    }

    private byte SBC()
    {
        Fetch();
        ushort value = (ushort)(fetched ^ 0x00FF);
        ushort sum = (ushort)(Accumulator + value + (GetFlag(bm_CarryFlag) ? 1 : 0));
        SetFlag(bm_CarryFlag, (sum & 0xFF00) != 0);
        SetFlag(bm_ZeroFlag, (sum & 0x00FF) == 0);
        SetFlag(bm_OverflowFlag, ((sum ^ Accumulator) & (sum ^ value) & 0x80) != 0);
        SetFlag(bm_NegativeFlag, (sum & 0x80) != 0);
        Accumulator = (byte)(sum & 0x00FF);
        return 1;
    }

    private byte AND()
    {
        Fetch();
        Accumulator &= fetched;
        SetZN(Accumulator);
        return 1;
    }

    private byte ASL()
    {
        Fetch();
        ushort temp = (ushort)(fetched << 1);
        SetFlag(bm_CarryFlag, (temp & 0xFF00) != 0);
        SetZN((byte)(temp & 0x00FF));
        if (lookup[opcode].AddrMode == IMP) Accumulator = (byte)(temp & 0x00FF);
        else Write(addr_abs, (byte)(temp & 0x00FF));
        return 0;
    }

    private byte BCC() { return Branch(!GetFlag(bm_CarryFlag)); }
    private byte BCS() { return Branch(GetFlag(bm_CarryFlag)); }
    private byte BEQ() { return Branch(GetFlag(bm_ZeroFlag)); }
    private byte BMI() { return Branch(GetFlag(bm_NegativeFlag)); }
    private byte BNE() { return Branch(!GetFlag(bm_ZeroFlag)); }
    private byte BPL() { return Branch(!GetFlag(bm_NegativeFlag)); }
    private byte BVC() { return Branch(!GetFlag(bm_OverflowFlag)); }
    private byte BVS() { return Branch(GetFlag(bm_OverflowFlag)); }

    private byte Branch(bool condition)
    {
        if (condition)
        {
            cycles++;
            ushort target = (ushort)(ProgramCounter + addr_rel);
            if ((target & 0xFF00) != (ProgramCounter & 0xFF00)) cycles++;
            ProgramCounter = target;
        }
        return 0;
    }

    private byte BIT()
    {
        Fetch();
        ushort temp = (ushort)(Accumulator & fetched);
        SetFlag(bm_ZeroFlag, (temp & 0x00FF) == 0);
        SetFlag(bm_NegativeFlag, (fetched & 0x80) != 0);
        SetFlag(bm_OverflowFlag, (fetched & 0x40) != 0);
        return 0;
    }

    private byte BRK()
    {
        ProgramCounter++;

        SetFlag(bm_InterruptDisableFlag);
        Push((byte)((ProgramCounter >> 8) & 0x00FF));
        Push((byte)(ProgramCounter & 0x00FF));

        SetFlag(bm_BreakFlag);
        Push(StatusRegister);
        ClearFlag(bm_BreakFlag);

        addr_abs = 0xFFFE;
        byte lo = Read(addr_abs);
        byte hi = Read((ushort)(addr_abs + 1));
        ProgramCounter = (ushort)((hi << 8) | lo);
        return 0;
    }

    private byte CLC() { ClearFlag(bm_CarryFlag); return 0; }
    private byte CLD() { ClearFlag(bm_DecimalModeFlag); return 0; }
    private byte CLI() { ClearFlag(bm_InterruptDisableFlag); return 0; }
    private byte CLV() { ClearFlag(bm_OverflowFlag); return 0; }
    private byte SEC() { SetFlag(bm_CarryFlag); return 0; }
    private byte SED() { SetFlag(bm_DecimalModeFlag); return 0; }
    private byte SEI() { SetFlag(bm_InterruptDisableFlag); return 0; }

    private byte CMP()
    {
        Fetch();
        ushort temp = (ushort)(Accumulator - fetched);
        SetFlag(bm_CarryFlag, Accumulator >= fetched);
        SetZN((byte)(temp & 0x00FF));
        return 1;
    }

    private byte CPX()
    {
        Fetch();
        ushort temp = (ushort)(X - fetched);
        SetFlag(bm_CarryFlag, X >= fetched);
        SetZN((byte)(temp & 0x00FF));
        return 0;
    }

    private byte CPY()
    {
        Fetch();
        ushort temp = (ushort)(Y - fetched);
        SetFlag(bm_CarryFlag, Y >= fetched);
        SetZN((byte)(temp & 0x00FF));
        return 0;
    }

    private byte DEC()
    {
        Fetch();
        byte temp = (byte)(fetched - 1);
        Write(addr_abs, temp);
        SetZN(temp);
        return 0;
    }

    private byte DEX() { X--; SetZN(X); return 0; }
    private byte DEY() { Y--; SetZN(Y); return 0; }

    private byte EOR()
    {
        Fetch();
        Accumulator ^= fetched;
        SetZN(Accumulator);
        return 1;
    }

    private byte INC()
    {
        Fetch();
        byte temp = (byte)(fetched + 1);
        Write(addr_abs, temp);
        SetZN(temp);
        return 0;
    }

    private byte INX() { X++; SetZN(X); return 0; }
    private byte INY() { Y++; SetZN(Y); return 0; }

    private byte JMP() { ProgramCounter = addr_abs; return 0; }

    private byte JSR()
    {
        ProgramCounter--;
        Push((byte)((ProgramCounter >> 8) & 0x00FF));
        Push((byte)(ProgramCounter & 0x00FF));
        ProgramCounter = addr_abs;
        return 0;
    }

    private byte LDA() { Fetch(); Accumulator = fetched; SetZN(Accumulator); return 1; }
    private byte LDX() { Fetch(); X = fetched; SetZN(X); return 1; }
    private byte LDY() { Fetch(); Y = fetched; SetZN(Y); return 1; }

    private byte LSR()
    {
        Fetch();
        SetFlag(bm_CarryFlag, (fetched & 0x0001) != 0);
        byte temp = (byte)(fetched >> 1);
        SetZN(temp);
        if (lookup[opcode].AddrMode == IMP) Accumulator = temp;
        else Write(addr_abs, temp);
        return 0;
    }

    private byte NOP() { return 0; }

    private byte ORA()
    {
        Fetch();
        Accumulator |= fetched;
        SetZN(Accumulator);
        return 1;
    }

    private byte PHA() { Push(Accumulator); return 0; }

    private byte PHP()
    {
        Push((byte)(StatusRegister | bm_BreakFlag | bm_UnusedFlag));
        ClearFlag(bm_BreakFlag);
        ClearFlag(bm_UnusedFlag);
        return 0;
    }

    private byte PLA()
    {
        Accumulator = Pop();
        SetZN(Accumulator);
        return 0;
    }

    private byte PLP()
    {
        StatusRegister = Pop();
        SetFlag(bm_UnusedFlag);
        return 0;
    }

    private byte ROL()
    {
        Fetch();
        ushort temp = (ushort)((fetched << 1) | (GetFlag(bm_CarryFlag) ? 1 : 0));
        SetFlag(bm_CarryFlag, (temp & 0xFF00) != 0);
        SetZN((byte)(temp & 0x00FF));
        if (lookup[opcode].AddrMode == IMP) Accumulator = (byte)(temp & 0x00FF);
        else Write(addr_abs, (byte)(temp & 0x00FF));
        return 0;
    }

    private byte ROR()
    {
        Fetch();
        ushort temp = (ushort)((fetched >> 1) | ((GetFlag(bm_CarryFlag) ? 1 : 0) << 7));
        SetFlag(bm_CarryFlag, (fetched & 0x0001) != 0);
        SetZN((byte)(temp & 0x00FF));
        if (lookup[opcode].AddrMode == IMP) Accumulator = (byte)(temp & 0x00FF);
        else Write(addr_abs, (byte)(temp & 0x00FF));
        return 0;
    }

    private byte RTI()
    {
        StatusRegister = Pop();
        ClearFlag(bm_BreakFlag);
        ClearFlag(bm_UnusedFlag);

        ushort lo = Pop();
        ushort hi = Pop();
        ProgramCounter = (ushort)((hi << 8) | lo);
        return 0;
    }

    private byte RTS()
    {
        ushort lo = Pop();
        ushort hi = Pop();
        ProgramCounter = (ushort)((hi << 8) | lo);
        ProgramCounter++;
        return 0;
    }

    private byte STA() { Write(addr_abs, Accumulator); return 0; }
    private byte STX() { Write(addr_abs, X); return 0; }
    private byte STY() { Write(addr_abs, Y); return 0; }

    private byte TAX() { X = Accumulator; SetZN(X); return 0; }
    private byte TAY() { Y = Accumulator; SetZN(Y); return 0; }
    private byte TSX() { X = StackPointer; SetZN(X); return 0; }
    private byte TXA() { Accumulator = X; SetZN(Accumulator); return 0; }
    private byte TXS() { StackPointer = X; return 0; }
    private byte TYA() { Accumulator = Y; SetZN(Accumulator); return 0; }

    /// <summary>Catch-all for illegal/undocumented opcodes. Treated as a NOP so
    /// execution doesn't crash; not cycle- or behaviour-accurate for the handful
    /// of NES titles that rely on illegal-opcode side effects.</summary>
    private byte XXX() { return 0; }

    // ---------------------------------------------------------------
    // Opcode table: 256 entries, indexed directly by opcode byte.
    // ---------------------------------------------------------------
    private void BuildLookupTable()
    {
        lookup = new INSTRUCTION[256]
        {
            new INSTRUCTION("BRK",BRK,IMP,7), new INSTRUCTION("ORA",ORA,IZX,6), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("???",NOP,IMP,3), new INSTRUCTION("ORA",ORA,ZP0,3), new INSTRUCTION("ASL",ASL,ZP0,5), new INSTRUCTION("???",XXX,IMP,5),
            new INSTRUCTION("PHP",PHP,IMP,3), new INSTRUCTION("ORA",ORA,IMM,2), new INSTRUCTION("ASL",ASL,IMP,2), new INSTRUCTION("???",XXX,IMP,2),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("ORA",ORA,ABS,4), new INSTRUCTION("ASL",ASL,ABS,6), new INSTRUCTION("???",XXX,IMP,6),

            new INSTRUCTION("BPL",BPL,REL,2), new INSTRUCTION("ORA",ORA,IZY,5), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("ORA",ORA,ZPX,4), new INSTRUCTION("ASL",ASL,ZPX,6), new INSTRUCTION("???",XXX,IMP,6),
            new INSTRUCTION("CLC",CLC,IMP,2), new INSTRUCTION("ORA",ORA,ABY,4), new INSTRUCTION("???",NOP,IMP,2), new INSTRUCTION("???",XXX,IMP,7),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("ORA",ORA,ABX,4), new INSTRUCTION("ASL",ASL,ABX,7), new INSTRUCTION("???",XXX,IMP,7),

            new INSTRUCTION("JSR",JSR,ABS,6), new INSTRUCTION("AND",AND,IZX,6), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("BIT",BIT,ZP0,3), new INSTRUCTION("AND",AND,ZP0,3), new INSTRUCTION("ROL",ROL,ZP0,5), new INSTRUCTION("???",XXX,IMP,5),
            new INSTRUCTION("PLP",PLP,IMP,4), new INSTRUCTION("AND",AND,IMM,2), new INSTRUCTION("ROL",ROL,IMP,2), new INSTRUCTION("???",XXX,IMP,2),
            new INSTRUCTION("BIT",BIT,ABS,4), new INSTRUCTION("AND",AND,ABS,4), new INSTRUCTION("ROL",ROL,ABS,6), new INSTRUCTION("???",XXX,IMP,6),

            new INSTRUCTION("BMI",BMI,REL,2), new INSTRUCTION("AND",AND,IZY,5), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("AND",AND,ZPX,4), new INSTRUCTION("ROL",ROL,ZPX,6), new INSTRUCTION("???",XXX,IMP,6),
            new INSTRUCTION("SEC",SEC,IMP,2), new INSTRUCTION("AND",AND,ABY,4), new INSTRUCTION("???",NOP,IMP,2), new INSTRUCTION("???",XXX,IMP,7),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("AND",AND,ABX,4), new INSTRUCTION("ROL",ROL,ABX,7), new INSTRUCTION("???",XXX,IMP,7),

            new INSTRUCTION("RTI",RTI,IMP,6), new INSTRUCTION("EOR",EOR,IZX,6), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("???",NOP,IMP,3), new INSTRUCTION("EOR",EOR,ZP0,3), new INSTRUCTION("LSR",LSR,ZP0,5), new INSTRUCTION("???",XXX,IMP,5),
            new INSTRUCTION("PHA",PHA,IMP,3), new INSTRUCTION("EOR",EOR,IMM,2), new INSTRUCTION("LSR",LSR,IMP,2), new INSTRUCTION("???",XXX,IMP,2),
            new INSTRUCTION("JMP",JMP,ABS,3), new INSTRUCTION("EOR",EOR,ABS,4), new INSTRUCTION("LSR",LSR,ABS,6), new INSTRUCTION("???",XXX,IMP,6),

            new INSTRUCTION("BVC",BVC,REL,2), new INSTRUCTION("EOR",EOR,IZY,5), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("EOR",EOR,ZPX,4), new INSTRUCTION("LSR",LSR,ZPX,6), new INSTRUCTION("???",XXX,IMP,6),
            new INSTRUCTION("CLI",CLI,IMP,2), new INSTRUCTION("EOR",EOR,ABY,4), new INSTRUCTION("???",NOP,IMP,2), new INSTRUCTION("???",XXX,IMP,7),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("EOR",EOR,ABX,4), new INSTRUCTION("LSR",LSR,ABX,7), new INSTRUCTION("???",XXX,IMP,7),

            new INSTRUCTION("RTS",RTS,IMP,6), new INSTRUCTION("ADC",ADC,IZX,6), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("???",NOP,IMP,3), new INSTRUCTION("ADC",ADC,ZP0,3), new INSTRUCTION("ROR",ROR,ZP0,5), new INSTRUCTION("???",XXX,IMP,5),
            new INSTRUCTION("PLA",PLA,IMP,4), new INSTRUCTION("ADC",ADC,IMM,2), new INSTRUCTION("ROR",ROR,IMP,2), new INSTRUCTION("???",XXX,IMP,2),
            new INSTRUCTION("JMP",JMP,IND,5), new INSTRUCTION("ADC",ADC,ABS,4), new INSTRUCTION("ROR",ROR,ABS,6), new INSTRUCTION("???",XXX,IMP,6),

            new INSTRUCTION("BVS",BVS,REL,2), new INSTRUCTION("ADC",ADC,IZY,5), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("ADC",ADC,ZPX,4), new INSTRUCTION("ROR",ROR,ZPX,6), new INSTRUCTION("???",XXX,IMP,6),
            new INSTRUCTION("SEI",SEI,IMP,2), new INSTRUCTION("ADC",ADC,ABY,4), new INSTRUCTION("???",NOP,IMP,2), new INSTRUCTION("???",XXX,IMP,7),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("ADC",ADC,ABX,4), new INSTRUCTION("ROR",ROR,ABX,7), new INSTRUCTION("???",XXX,IMP,7),

            new INSTRUCTION("???",NOP,IMP,2), new INSTRUCTION("STA",STA,IZX,6), new INSTRUCTION("???",NOP,IMP,2), new INSTRUCTION("???",XXX,IMP,6),
            new INSTRUCTION("STY",STY,ZP0,3), new INSTRUCTION("STA",STA,ZP0,3), new INSTRUCTION("STX",STX,ZP0,3), new INSTRUCTION("???",XXX,IMP,3),
            new INSTRUCTION("DEY",DEY,IMP,2), new INSTRUCTION("???",NOP,IMP,2), new INSTRUCTION("TXA",TXA,IMP,2), new INSTRUCTION("???",XXX,IMP,2),
            new INSTRUCTION("STY",STY,ABS,4), new INSTRUCTION("STA",STA,ABS,4), new INSTRUCTION("STX",STX,ABS,4), new INSTRUCTION("???",XXX,IMP,4),

            new INSTRUCTION("BCC",BCC,REL,2), new INSTRUCTION("STA",STA,IZY,6), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,6),
            new INSTRUCTION("STY",STY,ZPX,4), new INSTRUCTION("STA",STA,ZPX,4), new INSTRUCTION("STX",STX,ZPY,4), new INSTRUCTION("???",XXX,IMP,4),
            new INSTRUCTION("TYA",TYA,IMP,2), new INSTRUCTION("STA",STA,ABY,5), new INSTRUCTION("TXS",TXS,IMP,2), new INSTRUCTION("???",XXX,IMP,5),
            new INSTRUCTION("???",NOP,IMP,5), new INSTRUCTION("STA",STA,ABX,5), new INSTRUCTION("???",XXX,IMP,5), new INSTRUCTION("???",XXX,IMP,5),

            new INSTRUCTION("LDY",LDY,IMM,2), new INSTRUCTION("LDA",LDA,IZX,6), new INSTRUCTION("LDX",LDX,IMM,2), new INSTRUCTION("???",XXX,IMP,6),
            new INSTRUCTION("LDY",LDY,ZP0,3), new INSTRUCTION("LDA",LDA,ZP0,3), new INSTRUCTION("LDX",LDX,ZP0,3), new INSTRUCTION("???",XXX,IMP,3),
            new INSTRUCTION("TAY",TAY,IMP,2), new INSTRUCTION("LDA",LDA,IMM,2), new INSTRUCTION("TAX",TAX,IMP,2), new INSTRUCTION("???",XXX,IMP,2),
            new INSTRUCTION("LDY",LDY,ABS,4), new INSTRUCTION("LDA",LDA,ABS,4), new INSTRUCTION("LDX",LDX,ABS,4), new INSTRUCTION("???",XXX,IMP,4),

            new INSTRUCTION("BCS",BCS,REL,2), new INSTRUCTION("LDA",LDA,IZY,5), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,5),
            new INSTRUCTION("LDY",LDY,ZPX,4), new INSTRUCTION("LDA",LDA,ZPX,4), new INSTRUCTION("LDX",LDX,ZPY,4), new INSTRUCTION("???",XXX,IMP,4),
            new INSTRUCTION("CLV",CLV,IMP,2), new INSTRUCTION("LDA",LDA,ABY,4), new INSTRUCTION("TSX",TSX,IMP,2), new INSTRUCTION("???",XXX,IMP,4),
            new INSTRUCTION("LDY",LDY,ABX,4), new INSTRUCTION("LDA",LDA,ABX,4), new INSTRUCTION("LDX",LDX,ABY,4), new INSTRUCTION("???",XXX,IMP,4),

            new INSTRUCTION("CPY",CPY,IMM,2), new INSTRUCTION("CMP",CMP,IZX,6), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("CPY",CPY,ZP0,3), new INSTRUCTION("CMP",CMP,ZP0,3), new INSTRUCTION("DEC",DEC,ZP0,5), new INSTRUCTION("???",XXX,IMP,5),
            new INSTRUCTION("INY",INY,IMP,2), new INSTRUCTION("CMP",CMP,IMM,2), new INSTRUCTION("DEX",DEX,IMP,2), new INSTRUCTION("???",XXX,IMP,2),
            new INSTRUCTION("CPY",CPY,ABS,4), new INSTRUCTION("CMP",CMP,ABS,4), new INSTRUCTION("DEC",DEC,ABS,6), new INSTRUCTION("???",XXX,IMP,6),

            new INSTRUCTION("BNE",BNE,REL,2), new INSTRUCTION("CMP",CMP,IZY,5), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("CMP",CMP,ZPX,4), new INSTRUCTION("DEC",DEC,ZPX,6), new INSTRUCTION("???",XXX,IMP,6),
            new INSTRUCTION("CLD",CLD,IMP,2), new INSTRUCTION("CMP",CMP,ABY,4), new INSTRUCTION("???",NOP,IMP,2), new INSTRUCTION("???",XXX,IMP,7),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("CMP",CMP,ABX,4), new INSTRUCTION("DEC",DEC,ABX,7), new INSTRUCTION("???",XXX,IMP,7),

            new INSTRUCTION("CPX",CPX,IMM,2), new INSTRUCTION("SBC",SBC,IZX,6), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("CPX",CPX,ZP0,3), new INSTRUCTION("SBC",SBC,ZP0,3), new INSTRUCTION("INC",INC,ZP0,5), new INSTRUCTION("???",XXX,IMP,5),
            new INSTRUCTION("INX",INX,IMP,2), new INSTRUCTION("SBC",SBC,IMM,2), new INSTRUCTION("NOP",NOP,IMP,2), new INSTRUCTION("???",XXX,IMP,2),
            new INSTRUCTION("CPX",CPX,ABS,4), new INSTRUCTION("SBC",SBC,ABS,4), new INSTRUCTION("INC",INC,ABS,6), new INSTRUCTION("???",XXX,IMP,6),

            new INSTRUCTION("BEQ",BEQ,REL,2), new INSTRUCTION("SBC",SBC,IZY,5), new INSTRUCTION("???",XXX,IMP,2), new INSTRUCTION("???",XXX,IMP,8),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("SBC",SBC,ZPX,4), new INSTRUCTION("INC",INC,ZPX,6), new INSTRUCTION("???",XXX,IMP,6),
            new INSTRUCTION("SED",SED,IMP,2), new INSTRUCTION("SBC",SBC,ABY,4), new INSTRUCTION("???",NOP,IMP,2), new INSTRUCTION("???",XXX,IMP,7),
            new INSTRUCTION("???",NOP,IMP,4), new INSTRUCTION("SBC",SBC,ABX,4), new INSTRUCTION("INC",INC,ABX,7), new INSTRUCTION("???",XXX,IMP,7),
        };
    }
}
