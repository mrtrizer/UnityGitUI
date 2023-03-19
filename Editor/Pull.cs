using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Abuksigun.PackageShortcuts
{
    public static class Pull
    {
        [MenuItem("Assets/Pull", true)]
        public static bool Check() => PackageShortcuts.GetGitModules().Any();

        [MenuItem("Assets/Pull")]
        public static async void Invoke()
        {
            await Task.WhenAll(PackageShortcuts.GetGitModules().Select(module => module.RunGit("pull")));
        }
    }
}