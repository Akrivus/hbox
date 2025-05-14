using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class TaskExtensions
{
    public static IEnumerator AsCoroutine(this Task task, Action callback = null)
    {
        yield return new WaitUntil(() => task.IsCompleted);
        if (task.IsFaulted)
            Debug.LogError(task.Exception);
        callback?.Invoke();
    }

    public static IEnumerator AsCoroutine<T>(this Task<T> task, Action<T> callback = null)
    {
        yield return new WaitUntil(() => task.IsCompleted);
        if (task.IsFaulted)
        {
            Debug.LogError(task.Exception);
            throw task.Exception;
        }
        callback?.Invoke(task.Result);
    }
}