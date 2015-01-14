using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Web.Configuration;
using System.Collections.ObjectModel;
using System.Threading;
using System.Reflection;

using Mono.TextEditor;
using Mono.Cecil;

using MonoDevelop.Components.Commands;
using MonoDevelop.Ide;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Core;
using MonoDevelop.Projects;
using MonoDevelop.Ide.Tasks;

using Gendarme.Framework;

namespace MonoDevelop.Gendarme
{
    class GendarmeHandler : CommandHandler
    {
        static IAsyncOperation currentGendarmeAnalysisOperation = MonoDevelop.Core.ProgressMonitoring.NullAsyncOperation.Success;

        protected override void Run ()
        {
            if (currentGendarmeAnalysisOperation != null && !currentGendarmeAnalysisOperation.IsCompleted)
                return;
                             
            IProgressMonitor monitor = IdeApp.Workbench.ProgressMonitors.GetBuildProgressMonitor ();
            currentGendarmeAnalysisOperation = monitor.AsyncOperation;
            DispatchService.BackgroundDispatch (new StatefulMessageHandler (BuildAnalyzeAsync), new object[] {
                monitor,
                IdeApp.ProjectOperations.CurrentSelectedProject.DefaultConfiguration.Selector
            });
        }

        void BuildAnalyzeAsync (object ob)
        {
            object[] data = (object[])ob;
            IProgressMonitor monitor = (IProgressMonitor)data [0];
            ConfigurationSelector configuration = (ConfigurationSelector)data [1];
                         
            try {
                BuildResult result = IdeApp.ProjectOperations.CurrentSelectedProject.Build (monitor, 
                                         configuration);

                if (!result.Failed) {
                    List<FilePath> files = IdeApp.ProjectOperations.CurrentSelectedProject
                    .GetOutputFiles (IdeApp.ProjectOperations.CurrentSelectedProject.DefaultConfiguration.Selector);
                    Analyze (files);
                    
                }
            } catch (Exception ex) {
                monitor.ReportError (GettextCatalog.GetString ("MonoGendarme Analyze failed."), ex);
            } finally {
                monitor.Log.WriteLine ();
                monitor.Log.WriteLine (GettextCatalog.GetString ("MonoGendarme Analyze Done."));
                monitor.Dispose ();
            }
        }

        protected override void Update (CommandInfo info)
        {
            Document doc = IdeApp.Workbench.ActiveDocument;  
            info.Enabled = doc != null && doc.GetContent<ITextEditorDataProvider> () != null;  
        }

        private void Analyze (List<FilePath> files)
        {
            try {

                GendarmeRunner aRunner = new GendarmeRunner ();
                aRunner.LoadRules ();
                aRunner.Reset ();
                aRunner.Assemblies.Clear ();

                foreach (FilePath file in files) {
                    try {
                        aRunner.Assemblies.Add (AssemblyDefinition.ReadAssembly (file.FullPath, new ReaderParameters { AssemblyResolver = AssemblyResolver.Resolver }));
                    } catch (BadImageFormatException) {

                    } catch (FileNotFoundException fnfe) {

                    } finally {

                    }
                }

                aRunner.Execute ();

                // Clear all the errors if there is any.
                TaskService.Errors.ClearByOwner(this);

                List<Task> gendarmeAnalysisResultList = new List<Task> ();

                Collection<Defect> defects = aRunner.Defects;
                foreach (Defect defect in defects) {
                    
                    string filePath = string.Empty;
                    int lineNumber = 0;

                    if (!string.IsNullOrEmpty (defect.Source)) {
                        filePath = defect.Source.Substring (0, defect.Source.IndexOf ("("));

                        // Get line number
                        int starts = defect.Source.IndexOf ("(") + 2;
                        string lineNumberStr = defect.Source.Substring (starts,
                                               defect.Source.IndexOf (")") - starts);
                        lineNumber = Convert.ToInt32 (lineNumberStr);
                    }

                    // warning description
                    string warningDesc = defect.Rule.Name + ": " + defect.Rule.Problem
                        + " The solution: " + defect.Rule.Solution;

                    Task gendarmeWarning = new Task (new FilePath (filePath),
                        warningDesc,
                        0,
                        lineNumber - 1,
                        TaskSeverity.Warning,
                        TaskPriority.Normal,
                        IdeApp.ProjectOperations.CurrentSelectedProject,
                        this);

                    gendarmeAnalysisResultList.Add (gendarmeWarning);

                }
                TaskService.Errors.AddRange (gendarmeAnalysisResultList);
                TaskService.Errors.ResetLocationList ();
                IdeApp.Workbench.ActiveLocationList = TaskService.Errors;

            } catch (Exception e) {

            }
        }
    }
}