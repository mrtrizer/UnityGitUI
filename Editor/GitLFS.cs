using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Abuksigun.MRGitUI
{
    public static class GitLFS
    {
        [MenuItem("Assets/Git/LFS", true)]
        public static bool Check() => Utils.GetSelectedGitModules().Any();

        [MenuItem("Assets/Git/LFS", priority = 100)]
        public static void Invoke()
        {
            if (EditorWindow.GetWindow<GitLFSWindow>() is { } window && window)
            {
                window.titleContent = new GUIContent("Git LFS");
                window.Show();
            }
        }

        public class GitLFSWindow : DefaultWindow
        {
            const float TopPanelHeight = 60;

            [SerializeField] string selectedModuleGuid;
            LazyTreeView<string> patternsTreeView;
            [SerializeField] TreeViewState patternsTreeViewState;

            protected override void OnGUI()
            {
                var modules = Utils.GetSelectedGitModules().ToList();
                bool lfsInstalled = modules.All(x => x.IsLfsAvailable.GetResultOrDefault());
                if (!lfsInstalled)
                {
                    GUILayout.Label("Git LFS is not available in the system");
                    return;
                }
                var unitializedModules = modules.Where(x => !x.IsLfsInstalled.GetResultOrDefault()).ToArray();
                var module = GUIUtils.ModuleGuidToolbar(modules, selectedModuleGuid);
                if (module == null)
                    return;
                selectedModuleGuid = module.Guid;

                using (new GUILayout.HorizontalScope())
                {
                    if (!module.IsLfsInstalled.GetResultOrDefault())
                    {
                        if (GUILayout.Button($"Install LFS"))
                            foreach (var m in unitializedModules)
                                m.InstallLfs();
                    }
                    else
                    {
                        if ((module.LfsTrackedPaths.GetResultOrDefault()?.Any() ?? false) || (module.LfsFiles.GetResultOrDefault()?.Any() ?? false))
                        {
                            if (GUILayout.Button("Prune LFS"))
                                _ = GUIUtils.RunSafe(new[] { module }, (module) => module.PruneLfsObjects());
                            if (GUILayout.Button("Fetch LFS"))
                                _ = GUIUtils.RunSafe(new[] { module }, (module) => module.FetchLfsObjects());
                        }
                        if (GUILayout.Button("Add new pattern"))
                            _ = ShowAddTrackPatternWindow(module);
                    }
                }
                if (module.LfsTrackedPaths.GetResultOrDefault() is { } paths)
                {
                    GUILayout.Label($"Tracked paths ({paths.Length}):");

                    patternsTreeView ??= new(paths => paths.Select(x => new TreeViewItem(x.GetHashCode(), 0, x)).ToList(), patternsTreeViewState ??= new(), true);

                    var selectedPaths = paths.Where(x => patternsTreeViewState.selectedIDs.Contains(x.GetHashCode())).ToArray();

                    patternsTreeView.Draw(
                        new Vector2(position.width, position.height - TopPanelHeight),
                        paths,
                        contextMenuCallback: id => {
                            var menu = new GenericMenu();
                            menu.AddItem(new GUIContent("Untrack"), false, () => _ = module.UntrackPathsWithLfs(selectedPaths));
                            menu.AddItem(new GUIContent("Migrate"), false, () => {
                                string msg = $"Rewrite every commit in history (with flag -- everything) (will require running git push --force)\n{selectedPaths.Join()}";
                                if (EditorUtility.DisplayDialog($"DANGER! REWRITE HISTORY IN ALL BRANCHES!", msg, "Rewrite histroy", "Cancel"))
                                    _ = GUIUtils.RunSafe(new[] { module }, (module) => module.GitLfsMigrate(GitLfsMigrateMode.Import, selectedPaths, false));
                            });
                            menu.ShowAsContext();
                        },
                        doubleClickCallback: id => {
                            
                        });
                }
            }

            static async Task ShowAddTrackPatternWindow(Module module)
            {
                string newPattern = "";
                await GUIUtils.ShowModalWindow("Set Value", new Vector2Int(300, 180), window => {
                    newPattern = GUILayout.TextField(newPattern);
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Close"))
                        {
                            window.Close();
                        }
                        if (GUILayout.Button("Apply"))
                        {
                            _ = _ = GUIUtils.RunSafe(new[] { module }, (module) => module.TrackPathsWithLfs(new[] { newPattern }));
                            window.Close();
                        }
                    }
                });
            }
        }
    }
}