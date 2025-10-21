using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GSADUs.Revit.Addin.Workflows.Csv
{
    internal sealed class ExportCsvActionRegistration
    {
        // Static registration helper invoked during DI setup
        public static void Register(IServiceCollection services)
        {
            services.AddSingleton<IExportAction, ExportCsvAction>();
        }
    }
}
