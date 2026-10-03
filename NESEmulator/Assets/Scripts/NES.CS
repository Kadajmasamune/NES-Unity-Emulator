using System;
using System.Collections.Generic;
using UnityEngine;
using u_int16 = System.UInt16;
public class NES : MonoBehaviour
{
    private CPU cpu;
    private Bus bus ; 


    void Awake()
    {
        cpu = GetComponentInChildren<CPU>() ;
        bus = GetComponentInChildren<Bus>();
        cpu.ConnectBus(bus);
        
    }

    void Start()
    {
        
    }

    void Update()
    {
        // cpu.init();   
    }
}