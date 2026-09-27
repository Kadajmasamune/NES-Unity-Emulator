
public abstract class Instruction
{
    public CPU CPU ;
    public Bus bus => CPU.bus;

    public int cycles;
    public int length;
    public AddressingMode addressingMode;

    public abstract void Method();
    public abstract void Init(CPU _cpu);

    protected void Load(ref byte dst, ushort address) // LDA , LDX , LDY
    {
        dst = bus.Read(address);
    }
}
