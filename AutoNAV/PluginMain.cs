using System;
using Autodesk.Navisworks.Api.Plugins;
using System.Windows;

namespace AutoNAV
{
    /// <summary>
    /// Main plugin entry point for AutoNAV
    /// 
    /// This plugin handles:
    /// - Function 1: Search Set Creation Based on Discipline (Item -> Name -> Contains)
    /// - Function 2: Categorized Search Sets Based on Discipline (Element -> Category -> Equal)
    /// - Function 3: Hierarchical Organization and Refinement
    /// - Function 4: Automated Clash Test Generation from Discipline Search Sets
    /// 
    /// All four functions are integrated into a single UI with tabs/sections
    /// </summary>
    [Plugin("AutoNAV",
        "ACLP_VDC",
        ToolTip = "AutoNAV: Automated Design Coordination",
        DisplayName = "AutoNAV")]
    [AddInPlugin(AddInLocation.AddIn)]
    public class PluginMain : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                MainWindow mainWindow = new MainWindow();
                mainWindow.ShowDialog();
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Error launching AutoNAV:\n\n" + ex.Message + "\n\n" + ex.StackTrace,
                    "AutoNAV Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
