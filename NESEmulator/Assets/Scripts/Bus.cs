using UnityEngine ;
using UnityEngine.Assertions.Must;

public class Bus : MonoBehaviour
{
    private readonly byte[] ram = new byte[64 * 1024]; // 2 KB Internal Memory

    public byte Read(ushort address)
    {
        if(address >= 0x0000 && address <= 0x1FFF)
            return ram[address % 0x0800];
    

        else if (address >= 0x2000 && address <= 0x3FFF)
            return ram[(address - 0x2000) % 8 + 0x2000];

        
        return ram[address];
    } 
        

    public void Write(ushort address, byte data)
    {
        ram[address] = data;
    }

    private void Awake()
    {
        // ram[0xFFFC] = 43; 
    }
}
