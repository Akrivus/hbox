using System;
using UnityEngine;

public class WaitUntilTimer : CustomYieldInstruction
{
    private readonly Func<bool> _predicate;
    private readonly float _duration;

    private float _startTime;

    public override bool keepWaiting => Time.time - _startTime < _duration && !_predicate();

    public WaitUntilTimer(Func<bool> predicate, float duration = 120)
    {
        _predicate = predicate;
        _duration = duration;
        _startTime = Time.time;
    }
}