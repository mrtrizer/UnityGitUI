using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class Extensions
    {
        public static T GetResultOrDefault<T>(this Task<T> task, T defaultValue = default(T))
        {
            return task.IsCompleted ? task.Result : defaultValue;
        }
    }
}
