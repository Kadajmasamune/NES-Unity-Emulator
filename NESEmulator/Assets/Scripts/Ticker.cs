using System;
using UnityEngine;

public class Ticker : MonoBehaviour
{
    public double tickRate = 1f / 1789773f ;
    public double accumulator ; 
    public long CurrentTick ;

    public static event Action OnTick ; 
    
    public void Update()
    {
        accumulator += Time.deltaTime;
        while(canTick())
        {
            accumulator -= tickRate;
            CurrentTick ++ ;

            OnTick?.Invoke();
        }
    }
    public bool canTick ()  { return accumulator >= tickRate ;}
    
}