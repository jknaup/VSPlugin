﻿//* Copyright 2010-2011 Research In Motion Limited.
//*
//* Licensed under the Apache License, Version 2.0 (the "License");
//* you may not use this file except in compliance with the License.
//* You may obtain a copy of the License at
//*
//* http://www.apache.org/licenses/LICENSE-2.0
//*
//* Unless required by applicable law or agreed to in writing, software
//* distributed under the License is distributed on an "AS IS" BASIS,
//* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//* See the License for the specific language governing permissions and
//* limitations under the License.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ComponentModel.Design;
using BlackBerry.DebugEngine;
using BlackBerry.NativeCore;
using BlackBerry.NativeCore.Components;
using BlackBerry.NativeCore.Debugger.Model;
using BlackBerry.NativeCore.Diagnostics;
using BlackBerry.NativeCore.Model;
using BlackBerry.NativeCore.Services;
using BlackBerry.NativeCore.Tools;
using BlackBerry.Package.Components;
using BlackBerry.Package.Diagnostics;
using BlackBerry.Package.Dialogs;
using BlackBerry.Package.Editors;
using BlackBerry.Package.Helpers;
using BlackBerry.Package.Model.Integration;
using BlackBerry.Package.Options;
using BlackBerry.Package.Registration;
using BlackBerry.Package.ToolWindows;
using BlackBerry.Package.ViewModels;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using System.IO;
using EnvDTE;
using System.Windows.Forms;
using EnvDTE80;

namespace BlackBerry.Package
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // Register the editor factory
    [XmlEditorDesignerViewRegistration("XML", "xml", LogicalViewID.Designer, 0x60, DesignerLogicalViewEditor = typeof(BarDescriptorEditorFactory),
        Namespace = "http://www.qnx.com/schemas/application/1.0", MatchExtensionAndNamespace = true)]
    // And which type of files we want to handle
    [ProvideEditorExtension(typeof(BarDescriptorEditorFactory), BarDescriptorEditorFactory.DefaultExtension, 0x40, NameResourceID = 106)]
    // We register that our editor supports LOGVIEWID_Designer logical view
    [ProvideEditorLogicalView(typeof(BarDescriptorEditorFactory), LogicalViewID.Designer)]
    // Microsoft Visual C++ Project
    [EditorFactoryNotifyForProject("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}", BarDescriptorEditorFactory.DefaultExtension, GuidList.guidVSNDK_PackageEditorFactoryString)]

    // Registration of custom debugger
    [DebugEngineRegistration(AD7PortSupplier.PublicName, AD7Engine.DebugEngineGuid,
        Attach = true, AddressBreakpoints = false, CallstackBreakpoints = true, AlwaysLoadLocal = true, AutoSelectPriority = 4,
        DebugEngineClassGUID = AD7Engine.ClassGuid, DebugEngineClassName = AD7Engine.ClassName,
        ProgramProviderClassGUID = AD7ProgramProvider.ClassGuid, ProgramProviderClassName = AD7ProgramProvider.ClassName,
        PortSupplierClassGUID = AD7PortSupplier.ClassGuid, PortSupplierClassName = AD7PortSupplier.ClassName,
#if DEBUG
        AssemblyName = ConfigDefaults.DebugEngineDebugablePath
#else
        AssemblyName = "BlackBerry.DebugEngine.dll"
#endif
)]

    // This attribute is used to register the informations needed to show the this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", VersionString, IconResourceID = 400)]
    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]

    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    [Guid(GuidList.guidVSNDK_PackageString)]

    // This attribute defines set of settings pages provided by this package.
    // They are automatically instantiated and displayed inside [Visual Studio -> Tools -> Options...] dialog.
    [ProvideOptionPage(typeof(GeneralOptionPage), OptionsCategoryName, "General", 1001, 1002, true)]
    [ProvideOptionPage(typeof(LogsOptionPage), OptionsCategoryName, "Logs", 1001, 1003, true)]
    [ProvideOptionPage(typeof(ApiLevelOptionPage), OptionsCategoryName, "API Levels", 1001, 1004, true)]
    [ProvideOptionPage(typeof(TargetsOptionPage), OptionsCategoryName, "Targets", 1001, 1005, true)]
    [ProvideOptionPage(typeof(SigningOptionPage), OptionsCategoryName, "Signing", 1001, 1006, true)]

    // This attribute registers public services exposed by this package.
    // The package itself will be automatically loaded if needed.
    [ProvideService(typeof(IDeviceDiscoveryService), ServiceName = "BlackBerry Device Discovery")]
    [ProvideService(typeof(IAttachDiscoveryService), ServiceName = "BlackBerry Process-Attach Executable Discovery")]

    // This attribute registers a tool window exposed by this package.
    [ProvideToolWindow(typeof(TargetNavigatorPane), Style = VsDockStyle.Tabbed, MultiInstances = false)]
    public sealed class BlackBerryPackage : Microsoft.VisualStudio.Shell.Package, IDisposable, IDeviceDiscoveryService, IAttachDiscoveryService
    {
        public const string VersionString = "2.1.2014.922";
        public const string OptionsCategoryName = "BlackBerry";

        private BlackBerryPaneTraceListener _mainTraceWindow;
        private BlackBerryPaneTraceListener _gdbTraceWindow;
        private BlackBerryPaneTraceListener _qconnTraceWindow;
        private DTE2 _dte;
        private BuildPlatformsManager _buildPlatformsManager;

        #region Package Members

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public BlackBerryPackage()
        {
            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", ToString()));
        }

        ~BlackBerryPackage()
        {
            Dispose(false);
        }

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Trace.WriteLine (string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", ToString()));
            base.Initialize();

            _dte = (DTE2) GetService(typeof(SDTE));

            // create dedicated trace-logs output window pane (available in combo-box at regular Visual Studio Output Window):
            _mainTraceWindow = new BlackBerryPaneTraceListener("BlackBerry", TraceLog.Category, true, GetService(typeof(SVsOutputWindow)) as IVsOutputWindow, GuidList.GUID_TraceMainOutputWindowPane);
#if DEBUG
            _gdbTraceWindow = new BlackBerryPaneTraceListener("BlackBerry - GDB", TraceLog.CategoryGDB, true, GetService(typeof(SVsOutputWindow)) as IVsOutputWindow, GuidList.GUID_TraceGdbOutputWindowPane);
#endif
            _qconnTraceWindow = new BlackBerryPaneTraceListener("BlackBerry - QConn", QTraceLog.Category, true, GetService(typeof(SVsOutputWindow)) as IVsOutputWindow, GuidList.GUID_TraceQConnOutputWindowPane);
            _mainTraceWindow.Activate();

            // and set it to monitor all logs (they have to be marked with 'BlackBerry' category! aka TraceLog.Category):
            TraceLog.Add(_mainTraceWindow);
            TraceLog.Add(_gdbTraceWindow);
            TraceLog.Add(_qconnTraceWindow);
            TraceLog.WriteLine("BlackBerry plugin started");

            // add this package to the globally-proffed services:
            IServiceContainer serviceContainer = this;
            serviceContainer.AddService(typeof(IDeviceDiscoveryService), this, true);
            serviceContainer.AddService(typeof(IAttachDiscoveryService), this, true);

            TraceLog.WriteLine(" * registered services");

            TraceLog.WriteLine(" * loaded NDK descriptions");

            // setup called before running any 'tool':
            ToolRunner.Startup += (s, e) =>
                {
                    var ndk = PackageViewModel.Instance.ActiveNDK;

                    if (ndk != null)
                    {
                        e["QNX_TARGET"] = ndk.TargetPath;
                        e["QNX_HOST"] = ndk.HostPath;
                        e["PATH"] = string.Concat(Path.Combine(ConfigDefaults.JavaHome, "bin"), ";", e["PATH"],
                                                  Path.Combine(ndk.HostPath, "usr", "bin"), ";");
                    }
                    else
                    {
                        e["PATH"] = string.Concat(Path.Combine(ConfigDefaults.JavaHome, "bin"), ";", e["PATH"]);
                    }
                };

            // inform Targets, that list of target-devices has been changed and maybe it should force some disconnections:
            PackageViewModel.Instance.TargetsChanged += (s, e) => Targets.DisconnectIfOutside(e.TargetDevices);

            // Create Editor Factory. Note that the base Package class will call Dispose on it.
            RegisterEditorFactory(new BarDescriptorEditorFactory());
            TraceLog.WriteLine(" * registered editors");

            _buildPlatformsManager = new BuildPlatformsManager(_dte);
            _buildPlatformsManager.Initialize();
            TraceLog.WriteLine(" * registered build-platforms manager");

            // Add our command handlers for menu (commands must exist in the .vsct file)
            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (mcs != null)
            {
                // Create command for the 'Options...' menu
                CommandID optionsCommandID = new CommandID(GuidList.guidVSNDK_PackageCmdSet, PackageCommands.cmdidBlackBerryOptions);
                MenuCommand optionsMenu = new MenuCommand((s, e) => ShowOptionPage(typeof(GeneralOptionPage)), optionsCommandID);
                mcs.AddCommand(optionsMenu);

                // Create dynamic command for the 'devices-list' menu
                CommandID devicesCommandID = new CommandID(GuidList.guidVSNDK_PackageCmdSet, PackageCommands.cmdidBlackBerryTargetsDevicesPlaceholder);
                DynamicMenuCommand devicesMenu = new DynamicMenuCommand(() => PackageViewModel.Instance.TargetDevices,
                                                                        (cmd, collection, index) =>
                                                                            {
                                                                                var item = index >= 0 && index < collection.Count ? ((DeviceDefinition[])collection)[index] : null;
                                                                                PackageViewModel.Instance.ActiveDevice = item;
                                                                            },
                                                                        (cmd, collection, index) =>
                                                                            {
                                                                                var item = index >= 0 && index < collection.Count ? ((DeviceDefinition[])collection)[index] : null;
                                                                                cmd.Checked = item != null && (item == PackageViewModel.Instance.ActiveDevice || item == PackageViewModel.Instance.ActiveSimulator);
                                                                                cmd.Visible = item != null;
                                                                                cmd.Text = item != null ? item.ToString() : "-";
                                                                            },
                                                                        devicesCommandID);
                mcs.AddCommand(devicesMenu);

                // Create dynamic command for the 'api-level-list' menu
                CommandID apiLevelCommandID = new CommandID(GuidList.guidVSNDK_PackageCmdSet, PackageCommands.cmdidBlackBerryTargetsApiLevelsPlaceholder);
                DynamicMenuCommand apiLevelMenu = new DynamicMenuCommand(() => PackageViewModel.Instance.InstalledNDKs,
                                                                         (cmd, collection, index) =>
                                                                             {
                                                                                 var item = index >= 0 && index < collection.Count ? ((NdkInfo[]) collection)[index] : null;
                                                                                 PackageViewModel.Instance.ActiveNDK = item;
                                                                             },
                                                                         (cmd, collection, index) =>
                                                                             {
                                                                                 var item = index >= 0 && index < collection.Count ? ((NdkInfo[]) collection)[index] : null;
                                                                                 cmd.Checked = item != null && item == PackageViewModel.Instance.ActiveNDK;
                                                                                 cmd.Visible = item != null;
                                                                                 cmd.Text = item != null ? item.ToString() : "-";
                                                                             },
                                                                         apiLevelCommandID);
                mcs.AddCommand(apiLevelMenu);

                // Create dynamic command for the 'runtime-libraries' menu
                CommandID runtimeCommandID = new CommandID(GuidList.guidVSNDK_PackageCmdSet, PackageCommands.cmdidBlackBerryTargetsRuntimeLibrariesPlaceholder);
                DynamicMenuCommand runtimeMenu = new DynamicMenuCommand(() => PackageViewModel.Instance.InstalledRuntimes,
                                                                         (cmd, collection, index) =>
                                                                             {
                                                                                 var item = index >= 0 && index < collection.Count ? ((RuntimeInfo[]) collection)[index] : null;
                                                                                 PackageViewModel.Instance.ActiveRuntime = item;
                                                                             },
                                                                         (cmd, collection, index) =>
                                                                             {
                                                                                 var item = index >= 0 && index < collection.Count ? ((RuntimeInfo[])collection)[index] : null;
                                                                                 cmd.Checked = item != null && item == PackageViewModel.Instance.ActiveRuntime;
                                                                                 cmd.Visible = item != null;
                                                                                 cmd.Text = item != null ? item.Name : "-";
                                                                             },
                                                                         runtimeCommandID);
                mcs.AddCommand(runtimeMenu);

                // Create command for 'Help' menus
                var helpCmdIDs = new[] {
                                            PackageCommands.cmdidBlackBerryHelpWelcomePage, PackageCommands.cmdidBlackBerryHelpSupportForum,
                                            PackageCommands.cmdidBlackBerryHelpDocNative, PackageCommands.cmdidBlackBerryHelpDocCascades, PackageCommands.cmdidBlackBerryHelpDocPlayBook,
                                            PackageCommands.cmdidBlackBerryHelpSamplesNative, PackageCommands.cmdidBlackBerryHelpSamplesCascades, PackageCommands.cmdidBlackBerryHelpSamplesPlayBook, PackageCommands.cmdidBlackBerryHelpSamplesOpenSource,
                                            PackageCommands.cmdidBlackBerryHelpAbout
                                       };
                foreach (var cmdID in helpCmdIDs)
                {
                    CommandID helpCommandID = new CommandID(GuidList.guidVSNDK_PackageCmdSet, cmdID);
                    MenuCommand helpMenu = new MenuCommand(OpenHelpWebPage, helpCommandID);
                    mcs.AddCommand(helpMenu);
                }

                // Create command for 'Configure...' [targets] menu
                CommandID configureCommandID = new CommandID(GuidList.guidVSNDK_PackageCmdSet, PackageCommands.cmdidBlackBerryTargetsConfigure);
                MenuCommand configureMenu = new MenuCommand((s, e) => ShowOptionPage(typeof(TargetsOptionPage)), configureCommandID);
                mcs.AddCommand(configureMenu);

                // Create the command for 'Add BlackBerry Platforms' menu item
                CommandID platformsCommandID = new CommandID(GuidList.guidVSNDK_PackageCmdSet, PackageCommands.cmdidBlackBerryCommonAddPlatformTargets);
                OleMenuCommand platformsItem = new OleMenuCommand(AddBlackBerryTargetPlatforms, platformsCommandID);
                mcs.AddCommand(platformsItem);

                // Create the command for 'Import BlackBerry Native Project' menu item
                CommandID projectCommandID = new CommandID(GuidList.guidVSNDK_PackageCmdSet, PackageCommands.cmdidBlackBerryCommonProjectImport);
                OleMenuCommand projectItem = new OleMenuCommand(ImportBlackBerryProject, projectCommandID);
                mcs.AddCommand(projectItem);

                // Create the command for 'Target Navigator' menu item
                CommandID targetNavigatorCommandID = new CommandID(GuidList.guidVSNDK_PackageCmdSet, PackageCommands.cmdidBlackBerryToolWindowsTargetNavigator);
                OleMenuCommand targetNavigatorMenu = new OleMenuCommand(ShowTargetNavigator, targetNavigatorCommandID);
                mcs.AddCommand(targetNavigatorMenu);

                TraceLog.WriteLine(" * initialized menus");
            }

            TraceLog.WriteLine("-------------------- DONE ({0})", VersionString);

            // make sure there is an NDK selected and developer knows about it:
            EnsureActiveNDK();
            if (_buildPlatformsManager.IsBlackBerrySolution() && ActiveNDK == null)
            {
                var form = new MissingNdkInstalledForm();
                form.ShowDialog();
                EnsureActiveNDK();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_mainTraceWindow != null)
                {
                    _mainTraceWindow.Dispose();
                    _mainTraceWindow = null;
                }
                if (_gdbTraceWindow != null)
                {
                    _gdbTraceWindow.Dispose();
                    _gdbTraceWindow = null;
                }
                if (_qconnTraceWindow != null)
                {
                    _qconnTraceWindow.Dispose();
                    _qconnTraceWindow = null;
                }

                if (_buildPlatformsManager != null)
                {
                    _buildPlatformsManager.Close();
                    _buildPlatformsManager = null;
                }
            }

            base.Dispose(disposing);
        }

        protected override int QueryClose(out bool canClose)
        {
            var updateManager = PackageViewModel.Instance.UpdateManager;

            if (updateManager.IsRunning)
            {
                canClose = false;

                if (MessageBoxHelper.Show("Do you want to abort it and exit?",
                    "There are still some jobs running by BlackBerry Native Plugin (i.e. new NDK, simulator or runtime downloads).",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
                {
                    canClose = true;
                    updateManager.CancelAll();
                }
            }
            else
            {
                canClose = true;
            }

            return VSConstants.S_OK;
        }

        public Window2 OpenWebPageTab(string url)
        {
            return (Window2)_dte.ItemOperations.Navigate(url, vsNavigateOptions.vsNavigateOptionsNewWindow);
        }

        private void OpenUrl(string url)
        {
            var options = (GeneralOptionPage)GetDialogPage(typeof(GeneralOptionPage));

            if (options.IsOpeningExternal)
            {
                DialogHelper.StartURL(url);
            }
            else
            {
                OpenWebPageTab(url);
            }
        }

        private void OpenHelpWebPage(object sender, EventArgs e)
        {
            var menuCommand = sender as MenuCommand;
            int cmdID = menuCommand != null ? menuCommand.CommandID.ID : 0;

            switch (cmdID)
            {
                case PackageCommands.cmdidBlackBerryHelpWelcomePage:
                    OpenUrl("http://developer.blackberry.com/cascades/momentics/");
                    break;
                case PackageCommands.cmdidBlackBerryHelpSupportForum:
                    OpenUrl("http://supportforums.blackberry.com/t5/Developer-Support-Forums/ct-p/blackberrydev");
                    break;
                case PackageCommands.cmdidBlackBerryHelpDocNative:
                    OpenUrl("http://developer.blackberry.com/native/documentation/core/framework.html");
                    break;
                case PackageCommands.cmdidBlackBerryHelpDocCascades:
                    OpenUrl("http://developer.blackberry.com/native/documentation/cascades/dev/index.html");
                    break;
                case PackageCommands.cmdidBlackBerryHelpDocPlayBook:
                    OpenUrl("http://developer.blackberry.com/playbook/native/documentation/");
                    break;
                case PackageCommands.cmdidBlackBerryHelpSamplesNative:
                    OpenUrl("http://developer.blackberry.com/native/sampleapps/");
                    break;
                case PackageCommands.cmdidBlackBerryHelpSamplesCascades:
                    OpenUrl("http://developer.blackberry.com/native/sampleapps/");
                    break;
                case PackageCommands.cmdidBlackBerryHelpSamplesPlayBook:
                    OpenUrl("http://developer.blackberry.com/playbook/native/sampleapps/");
                    break;
                case PackageCommands.cmdidBlackBerryHelpSamplesOpenSource:
                    OpenUrl("https://github.com/blackberry");
                    break;
                case PackageCommands.cmdidBlackBerryHelpAbout:
                    {
                        var form = new AboutForm();
                        form.ShowDialog();
                    }
                    break;
                default:
                    TraceLog.WarnLine("Unknown Help item requested");
                    break;
            }
        }

        #endregion

        /// <summary>
        /// Gets the currently selected NDK to build against.
        /// </summary>
        private NdkInfo ActiveNDK
        {
            get { return PackageViewModel.Instance.ActiveNDK; }
        }

        /// <summary>
        /// Makes sure the NDK and runtime libraries are selected
        /// and their paths are stored inside registry for build toolset.
        /// </summary>
        private void EnsureActiveNDK()
        {
            PackageViewModel.Instance.EnsureActiveNDK();
            PackageViewModel.Instance.EnsureActiveRuntime();
        }

        private void AddBlackBerryTargetPlatforms(object sender, EventArgs e)
        {
            var projects = DteHelper.GetProjects(_dte);

            if (projects == null || projects.Length == 0)
            {
                MessageBoxHelper.Show("Unable to add BlackBerry Platforms.", "Please open a solution with Visual C++ projects", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (MessageBoxHelper.Show("Do you want to add BlackBerry target platforms for all C/C++ projects?", null, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            foreach (var project in projects)
            {
                _buildPlatformsManager.AddPlatforms(project);
            }

            MessageBoxHelper.Show("You might now:\r\n * restart Visual Studio, as it has the 'deploy' option disabled\r\n * update the Author Information within the bar-descriptor.xml", "BlackBerry and BlackBerrySimulator targets have been added to solution configurations.", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ImportBlackBerryProject(object sender, EventArgs e)
        {
            var form = DialogHelper.OpenNativeCoreProject("Open Core Native Application Project", null);
            if (form.ShowDialog() == DialogResult.OK)
            {
                string filename = form.FileName;
                FileInfo fi = new FileInfo(filename);
                string folderName = fi.DirectoryName;

                Array projects = (Array)_dte.ActiveSolutionProjects;
                Project project = (Project)projects.GetValue(0);
                string name = project.FullName;

                // Create the dialog instance without Help support.
                var importSummary = new Import.Import(project, folderName, name);
                importSummary.ShowModel2();
            }
        }

        private void ShowTargetNavigator(object sender, EventArgs e)
        {
            ShowToolWindow<TargetNavigatorPane>();
        }

        /// <summary>
        /// Shows the singleton tool window or creates new one.
        /// </summary>
        private T ShowToolWindow<T>() where T : ToolWindowPane
        {
            var toolWindow = (T) FindToolWindow(typeof(T), 0, true);

            if (toolWindow == null || toolWindow.Frame == null)
                throw new InvalidOperationException("Unable to create tool window of specified type");

            // force the frame to be shown as MDI content window:
            var frame = (IVsWindowFrame) toolWindow.Frame;
            frame.SetProperty((int) __VSFPROPID.VSFPROPID_FrameMode, VSFRAMEMODE.VSFM_MdiChild);
            ErrorHandler.ThrowOnFailure(frame.Show());

            return toolWindow;
        }

        #region IDisposable Implementation

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        #endregion

        #region IDeviceDiscoveryService Implementation

        DeviceDefinition IDeviceDiscoveryService.FindDevice()
        {
            var dialog = new DeviceForm("Searching for BlackBerry device");
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                return dialog.ToDevice();
            }

            return null;
        }

        #endregion

        #region IAttachDiscoveryService Implementation

        string IAttachDiscoveryService.FindExecutable(ProcessInfo process)
        {
            foreach (Project project in _dte.Solution.Projects)
            {
                if (_buildPlatformsManager.IsBlackBerryProject(project))
                {
                    var outputPath = ProjectHelper.GetTargetFullName(project);
                    if (!string.IsNullOrEmpty(outputPath))
                    {
                        var name = Path.GetFileName(outputPath);
                        if (string.Compare(name, process.Name, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            TraceLog.WarnLine("Suggesting executable path: \"{0}\"", outputPath);
                            return outputPath;
                        }
                    }
                }
            }

            // PH: TODO: ask developer via any UI to point to the executable (binary)
            return null;
        }

        #endregion
    }
}
