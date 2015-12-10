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

        protected override void Run()
        {
            if (currentGendarmeAnalysisOperation != null && !currentGendarmeAnalysisOperation.IsCompleted)
                return;

            ConfigurationSelector configuration = null;

            if (!(IdeApp.ProjectOperations.CurrentSelectedItem is Project) && !(IdeApp.ProjectOperations.CurrentSelectedItem is Solution))
            {
                return;
            }
            
            // IS PROJECT ?
            if (IdeApp.ProjectOperations.CurrentSelectedItem is Project)
            {
                configuration = (IdeApp.ProjectOperations.CurrentSelectedItem as Project).DefaultConfiguration.Selector;
            }

            // IS SOLUTION ?
            if (IdeApp.ProjectOperations.CurrentSelectedItem is Solution)
            {
                configuration = (IdeApp.ProjectOperations.CurrentSelectedItem as Solution).DefaultConfiguration.Selector;
            }

            IProgressMonitor monitor = IdeApp.Workbench.ProgressMonitors.GetBuildProgressMonitor();
            currentGendarmeAnalysisOperation = monitor.AsyncOperation;
            DispatchService.BackgroundDispatch(new StatefulMessageHandler(BuildAnalyzeAsync), new object[]
                {
                    monitor,
                    configuration
                });
        }

        void BuildAnalyzeAsync(object ob)
        {
            object[] data = (object[])ob;
            IProgressMonitor monitor = (IProgressMonitor)data[0];
            ConfigurationSelector configuration = (ConfigurationSelector)data[1];
                         
            try
            {
                BuildResult result = null;

                if (IdeApp.ProjectOperations.CurrentSelectedItem is Project)
                {
                    result = (IdeApp.ProjectOperations.CurrentSelectedItem as Project).Build(monitor, 
                        configuration);
                }
                else if (IdeApp.ProjectOperations.CurrentSelectedItem is Solution)
                {
                    result = (IdeApp.ProjectOperations.CurrentSelectedItem as Solution).Build(monitor, 
                        configuration);
                }

                if (result != null && !result.Failed)
                {
                    List<FilePath> files = null;

                    if (IdeApp.ProjectOperations.CurrentSelectedItem is Project)
                    {
                        files = (IdeApp.ProjectOperations.CurrentSelectedItem as Project).GetOutputFiles(configuration);
                    }
                    else if (IdeApp.ProjectOperations.CurrentSelectedItem is Solution)
                    {
                        files = new List<FilePath>();
                        ReadOnlyCollection<Project> projects = (IdeApp.ProjectOperations.CurrentSelectedItem as Solution).GetAllProjects();
                        foreach (Project proj in projects)
                        {
                            files.AddRange(proj.GetOutputFiles(configuration));
                        }
                    }

                    Analyze(monitor, files);
                }
            }
            catch (Exception ex)
            {
                monitor.ReportError(GettextCatalog.GetString("MonoGendarme Analyze failed."), ex);
            }
            finally
            {
                monitor.Log.WriteLine();
                monitor.Log.WriteLine(GettextCatalog.GetString("MonoGendarme Analyze Done."));
                monitor.Dispose();
            }
        }

        protected override void Update(CommandInfo info)
        {
            if ((IdeApp.ProjectOperations.CurrentSelectedItem is Project)
                || (IdeApp.ProjectOperations.CurrentSelectedItem is Solution))
            {
                info.Enabled = true;
            }
        }

        private void Analyze(IProgressMonitor monitor, List<FilePath> files)
        {
            try
            {
                GendarmeRunner aRunner = new GendarmeRunner();
                aRunner.LoadRules();
                aRunner.Reset();
                aRunner.Assemblies.Clear();

                foreach (FilePath file in files)
                {
                    try
                    {
                        if (file.Extension.Equals(".dll") || file.Extension.Equals(".exe"))
                        {
                            monitor.Log.WriteLine(GettextCatalog.GetString("Register assembly " + file.FullPath + " for analysis."));
                            aRunner.Assemblies.Add(AssemblyDefinition.ReadAssembly(file.FullPath, new ReaderParameters { AssemblyResolver = AssemblyResolver.Resolver }));
                        }
                        else
                        {
                            monitor.Log.WriteLine(GettextCatalog.GetString("Skipping the file " + file.FullPath + "."));
                        }
                    }
                    catch (BadImageFormatException bife)
                    {
                        monitor.ReportError("Can't add assembly " + file.FullPath, bife); 
                    }
                    catch (FileNotFoundException fnfe)
                    {
                        monitor.ReportError("Can't add assembly " + file.FullPath, fnfe);
                    }
                }

                aRunner.Execute();

                ReportDefects(aRunner);
            }
            catch (Exception e)
            {
                monitor.ReportError("Failed to analyse. \r\n" 
                    + "Message: " + e.Message + "\r\n" 
                    + "Stacktrace: " + e.StackTrace, e);
            }
        }

        void ReportDefects(GendarmeRunner aRunner)
        {
            // Clear all the errors if there is any.
            TaskService.Errors.ClearByOwner(this);
            List<Task> gendarmeAnalysisResultList = new List<Task>();
            Collection<Defect> defects = aRunner.Defects;
            foreach (Defect defect in defects)
            {
                string filePath = string.Empty;
                int lineNumber = 0;
                if (!string.IsNullOrEmpty(defect.Source))
                {
                    filePath = defect.Source.Substring(0, defect.Source.IndexOf("("));
                    // Get line number
                    int starts = defect.Source.IndexOf("(") + 2;
                    string lineNumberStr = defect.Source.Substring(starts, defect.Source.IndexOf(")") - starts);
                    Int32.TryParse(lineNumberStr.Trim(), out lineNumber);
                }

                // warning description
                string warningDesc = defect.Rule.Name + ": " + defect.Rule.Problem
                                     + " The solution: " + defect.Rule.Solution
                                     + "\r\nMore information: " + defect.Rule.Uri.ToString();
                Task gendarmeWarning = new Task(new FilePath(filePath), warningDesc, 0, lineNumber, TaskSeverity.Warning, TaskPriority.Normal, IdeApp.ProjectOperations.CurrentSelectedProject, this);
                gendarmeAnalysisResultList.Add(gendarmeWarning);
            }
            TaskService.Errors.AddRange(gendarmeAnalysisResultList);
            TaskService.Errors.ResetLocationList();
            IdeApp.Workbench.ActiveLocationList = TaskService.Errors;
        }
    }
}