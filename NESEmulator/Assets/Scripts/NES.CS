using System;
using System.Collections.Generic;
using UnityEngine;
using u_int16 = System.UInt16;
public class NES : MonoBehaviour
{
    private CPU cpu;
    private Bus bus = new(); 


    
    void Start()
    {
        cpu = GetComponentInChildren<CPU>() ;
        cpu.ConnectBus(bus);
        
    }

    void Update()
    {
        // cpu.init();   
    }
}