using Autodesk.Revit.UI;
using System;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace GSADUs.Revit.Addin
{
    public class Startup : IExternalApplication
    {
        static BitmapImage Pack(string packUri)
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(packUri, UriKind.Absolute);
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            return img;
        }

        public Result OnStartup(UIControlledApplication app)
        {
            // Initialize DI container early
            _ = ServiceBootstrap.Provider;

            var panel = app.GetRibbonPanels(Tab.AddIns).FirstOrDefault(p => p.Name == "GSADUs")
                     ?? app.CreateRibbonPanel(Tab.AddIns, "GSADUs");

            var pbdExport = new PushButtonData(
              "BatchExportBtn", "Batch Export",
              Assembly.GetExecutingAssembly().Location,
              "GSADUs.Revit.Addin.BatchExportCommand");

            var btnExport = (PushButton)panel.AddItem(pbdExport);

            var small = Pack("pack://application:,,,/GSADUs.Revit.Addin;component/icons/batch_export_16.png");
            var large = Pack("pack://application:,,,/GSADUs.Revit.Addin;component/icons/batch_export_32.png");
            btnExport.Image = small;
            btnExport.LargeImage = large;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
    }
}

