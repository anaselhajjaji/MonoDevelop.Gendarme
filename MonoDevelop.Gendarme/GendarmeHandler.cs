using Gendarme.Framework;

namespace MonoDevelop.Gendarme
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Threading;
    using System.Web.Configuration;

    using Mono.Cecil;
    using Mono.TextEditor;

    using MonoDevelop.Components.Commands;
    using MonoDevelop.Core;
    using MonoDevelop.Ide;
    using MonoDevelop.Ide.Gui;
    using MonoDevelop.Ide.Tasks;
    using MonoDevelop.Projects;

    /// <summary>
    /// Gendarme handler.
    /// </summary>
    public class GendarmeHandler : CommandHandler
    {
        /// <summary>
        /// The current gendarme analysis operation.
        /// </summary>
        private static IAsyncOperation currentGendarmeAnalysisOperation = MonoDevelop.Core.ProgressMonitoring.NullAsyncOperation.Success;

        /// <summary>
        /// The locker.
        /// </summary>
        private static object locker = new object();

        /// <summary>
        /// Run the analysis.
        /// </summary>
        protected override void Run()
        {
            if (currentGendarmeAnalysisOperation != null && !currentGendarmeAnalysisOperation.IsCompleted)
            {
                return;
            }

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
            lock (locker)
            {
                currentGendarmeAnalysisOperation = monitor.AsyncOperation;
            }

            DispatchService.BackgroundDispatch(
                new StatefulMessageHandler(this.BuildAnalyzeAsync), 
                new object[] { monitor, configuration });
        }

        /// <summary>
        /// Reports the defects.
        /// </summary>
        /// <param name="aRunner">A runner.</param>
        protected void ReportDefects(GendarmeRunner theRunner)
        {
            // Clear all the errors if there is any.
            TaskService.Errors.ClearByOwner(this);
            List<Task> gendarmeAnalysisResultList = new List<Task>();
            Collection<Defect> defects = theRunner.Defects;
            foreach (Defect defect in defects)
            {
                string filePath = string.Empty;
                int lineNumber = 0;
                if (!string.IsNullOrEmpty(defect.Source))
                {
                    filePath = defect.Source.Substring(0, defect.Source.IndexOf('('));

                    // Get line number
                    int starts = defect.Source.IndexOf('(') + 2;
                    string lineNumberStr = defect.Source.Substring(starts, defect.Source.IndexOf(')') - starts);
                    int.TryParse(lineNumberStr.Trim(), out lineNumber);
                }

                // warning description
                string warningDesc = defect.Rule.Name + ": " + defect.Rule.Problem
                                     + " The solution: " + defect.Rule.Solution + Environment.NewLine
                                     + "More information: " + defect.Rule.Uri.ToString();
                Task gendarmeWarning = new Task(new FilePath(filePath), warningDesc, 0, lineNumber, TaskSeverity.Warning, TaskPriority.Normal, IdeApp.ProjectOperations.CurrentSelectedProject, this);
                gendarmeAnalysisResultList.Add(gendarmeWarning);
            }

            TaskService.Errors.AddRange(gendarmeAnalysisResultList);
            TaskService.Errors.ResetLocationList();
            IdeApp.Workbench.ActiveLocationList = TaskService.Errors;
        }

        /// <summary>
        /// Update the specified info.
        /// </summary>
        /// <param name="info">The command info.</param>
        protected override void Update(CommandInfo info)
        {
            if ((IdeApp.ProjectOperations.CurrentSelectedItem is Project)
                || (IdeApp.ProjectOperations.CurrentSelectedItem is Solution))
            {
                info.Enabled = true;
            }
        }

        /// <summary>
        /// Build and analyze asynchronously.
        /// </summary>
        /// <param name="inputData">The input data.</param>
        private void BuildAnalyzeAsync(object inputData)
        {
            object[] data = (object[])inputData;
            IProgressMonitor monitor = (IProgressMonitor)data[0];
            ConfigurationSelector configuration = (ConfigurationSelector)data[1];
                         
            try
            {
                BuildResult result = null;

                if (IdeApp.ProjectOperations.CurrentSelectedItem is Project)
                {
                    result = (IdeApp.ProjectOperations.CurrentSelectedItem as Project).Build(
                        monitor, 
                        configuration);
                }
                else if (IdeApp.ProjectOperations.CurrentSelectedItem is Solution)
                {
                    result = (IdeApp.ProjectOperations.CurrentSelectedItem as Solution).Build(
                        monitor, 
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

                    this.Analyze(monitor, files);
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

        /// <summary>
        /// Analyze the specified monitor and files.
        /// </summary>
        /// <param name="monitor">Monitor.</param>
        /// <param name="files">Files.</param>
        private void Analyze(IProgressMonitor monitor, List<FilePath> files)
        {
            try
            {
                GendarmeRunner theRunner = new GendarmeRunner();
                theRunner.LoadRules();
                theRunner.Reset();
                theRunner.Assemblies.Clear();

                foreach (FilePath file in files)
                {
                    try
                    {
                        if (file.Extension.Equals(".dll") || file.Extension.Equals(".exe"))
                        {
                            monitor.Log.WriteLine(GettextCatalog.GetString("Register assembly " + file.FullPath + " for analysis."));
                            theRunner.Assemblies.Add(AssemblyDefinition.ReadAssembly(file.FullPath, new ReaderParameters { AssemblyResolver = AssemblyResolver.Resolver }));
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

                theRunner.Execute();

                this.ReportDefects(theRunner);
            }
            catch (Exception e)
            {
                monitor.ReportError(
                    "Failed to analyse. " + Environment.NewLine
                    + "Message: " + e.Message + Environment.NewLine
                    + "Stacktrace: " + e.StackTrace,
                    e);
            }
        }
    }
}