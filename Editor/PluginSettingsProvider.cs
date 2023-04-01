using UnityEditor;
using UnityEngine.UIElements;

namespace Abuksigun.PackageShortcuts
{
    public static class PluginSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateMyCustomSettingsProvider() => new("Preferences/External Tools/Package Shortcuts", SettingsScope.User) {
            activateHandler = (_, rootElement) => rootElement.Add(new IMGUIContainer(() => {
            }))
        };
    }
}
