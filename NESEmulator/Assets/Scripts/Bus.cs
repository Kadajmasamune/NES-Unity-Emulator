using UnityEngine ;
using UnityEngine.Assertions.Must;

public class Bus 
{
    private Ram ram  = new(); 
    private byte[] Memory => ram.Memory;


    public byte Read(ushort address)
    {
        return Memory[address];       
    }
    public void Write(ushort address, byte value)   
    {
        Memory[address] = value ; return ; 
    }

    
}