﻿using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SSDTDevPack.Clippy;
using SSDTDevPack.Common.ProjectVersion;
using SSDTDevPack.Common.ScriptDom;
using SSDTDevPack.Common.Settings;
using SSDTDevPack.Common.UserMessages;
using SSDTDevPack.Common.VSPackage;
using SSDTDevPack.Extraction;
using SSDTDevPack.Formatting;
using SSDTDevPack.Rewriter;
using SSDTDevPack.Logging;
using SSDTDevPack.NameConstraints;
using SSDTDevPack.QueryCosts;
using SSDTDevPack.QueryCosts.Highlighter;
using SSDTDevPack.QuickDeploy;
using SSDTDevPack.tSQLtStubber;

namespace TheAgileSQLClub.SSDTDevPack_VSPackage
{
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof (MergeToolWindow))]
    [ProvideToolWindow(typeof(CodeCoverageToolWindow))]
    [Guid(GuidList.guidSSDTDevPack_VSPackagePkgString)]
    [ProvideAutoLoad("ADFC4E64-0397-11D1-9F4E-00A0C911004F")]
    public sealed class SSDTDevPack_VSPackagePackage : Package, IVsServiceProvider
    {
        public SSDTDevPack_VSPackagePackage()
        {
            VsServiceProvider.Register(this);
        }

        public object GetVsService(Type type)
        {
            return GetService(type);
        }

        private void ShowCodeCoverageToolWindow(object sender, EventArgs e)
        {
            var window = FindToolWindow(typeof(CodeCoverageToolWindow), 0, true);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException(Resources.CanNotCreateWindow);
            }
            var windowFrame = (IVsWindowFrame)window.Frame;
            ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }

        private void ShowMergeToolWindow(object sender, EventArgs e)
        {
            var window = FindToolWindow(typeof (MergeToolWindow), 0, true);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException(Resources.CanNotCreateWindow);
            }
            var windowFrame = (IVsWindowFrame) window.Frame;
            ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }

        protected override void Initialize()
        {
            base.Initialize();

            var mcs = GetService(typeof (IMenuCommandService)) as OleMenuCommandService;
            if (null != mcs)
            {
            
                var toolwndCommandID = new CommandID(GuidList.guidSSDTDevPack_VSPackageCmdSet,
                    (int) PkgCmdIDList.SSDTDevPackMergeUi);
                var menuToolWin = new MenuCommand(ShowMergeToolWindow, toolwndCommandID);
                mcs.AddCommand(menuToolWin);

                toolwndCommandID = new CommandID(GuidList.guidSSDTDevPack_VSPackageCmdSet, (int)PkgCmdIDList.SSDTDevPackCodeCoverage);
                menuToolWin = new MenuCommand(ShowCodeCoverageToolWindow, toolwndCommandID);
                mcs.AddCommand(menuToolWin);


                var menuCommandID = new CommandID(GuidList.guidSSDTDevPack_VSPackageCmdSet,
                    (int) PkgCmdIDList.SSDTDevPackNameConstraints);
                var menuItem = new MenuCommand(NameConstraintsCalled, menuCommandID);
                mcs.AddCommand(menuItem);


                menuCommandID = new CommandID(GuidList.guidSSDTDevPack_VSPackageCmdSet,
                    (int) PkgCmdIDList.SSDTDevPackCreatetSQLtSchema);
                menuItem = new MenuCommand(CreatetSQLtSchema, menuCommandID);
                mcs.AddCommand(menuItem);

                menuCommandID = new CommandID(GuidList.guidSSDTDevPack_VSPackageCmdSet,(int) PkgCmdIDList.SSDTDevPackCreatetSQLtTestStub);
                menuItem = new MenuCommand(CreatetSQLtTest, menuCommandID);
                mcs.AddCommand(menuItem);


                AddMenuItem(mcs, (int) PkgCmdIDList.SSDTDevPackToggleQueryCosts, ToggleQueryCosts);
                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackClearQueryCosts, ClearQueryCosts);

                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackQuickDeploy, QuickDeploy);
                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackQuickDeployToClipboard, QuickDeployToClipboard);
                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackQuickDeployAppendToClipboard, QuickDeployAppendToClipboard);
                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackQuickDeployClearConnection, QuickDeployClearConnection);

                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackLowerCase, LowerCase);
                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackUpperCase, UpperCase);

                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackExtractToTvf, ExtractToTvf);
                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackDeprecatedWarning, DeprecatedWarning);
                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackFindDuplicateIndexes, FindDuplicateIndexes);
                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTNonSargableRewrites, RewriteNonSargableIsNull);
                AddCheckableMenuItem(mcs, (int)PkgCmdIDList.SSDTTSqlClippy, EnableClippy);
                AddMenuItem(mcs, (int)PkgCmdIDList.SSDTDevPackCorrectCase, CorrectCaseTableNames);
                AddCheckableMenuItemCodeCoverage(mcs, (int)PkgCmdIDList.SSDTDevPackToggleCodeCoverageDisplay, EnableCodeCoverage);

            }
        }

        private void DeprecatedWarning(object sender, EventArgs e)
        {
            MessageBox.Show("Yes it is true, mainly because I don't beleive anyone uses these - if you want it to stay in then email ed@agilesql.co.uk and tell me what it is you want to keep on using :)");
        }

        private void QuickDeployClearConnection(object sender, EventArgs e)
        {
            try
            {
                QuickDeployer.ClearConnectionString();
                OutputPane.WriteMessageAndActivatePane("Quick Deploy Connection String has been cleared.");
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("QuickDeployClearConnection error: {0}", ex.Message);
                Log.WriteInfo("QuickDeployClearConnection error: {0}", ex.Message);
            }

        }

        private void QuickDeployAppendToClipboard(object sender, EventArgs e)
        {
            try
            {
                var script = QuickDeployer.GetDeployScript(GetCurrentDocumentText());
                var original = Clipboard.GetText();

                Clipboard.SetText(original + "\r\n" + script, TextDataFormat.UnicodeText);
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("QuickDeployToClipboard error: {0}", ex.Message);
                Log.WriteInfo("QuickDeployToClipboard error: {0}", ex.Message);
            }

        }

        private void QuickDeployToClipboard(object sender, EventArgs e)
        {
            try
            {
                var script = QuickDeployer.GetDeployScript(GetCurrentDocumentText());
                Clipboard.SetText(script, TextDataFormat.UnicodeText);
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("QuickDeployToClipboard error: {0}", ex.Message);
                Log.WriteInfo("QuickDeployToClipboard error: {0}", ex.Message);
            }

        }


        private void CorrectCaseTableNames(object sender, EventArgs e)
        {
            try
            {
                var task = new System.Threading.Tasks.Task(() =>
                {
                    OutputPane.WriteMessageAndActivatePane("Correcting the case of table names...");
                    var finder = new CorrectCaseTableFinder();
                    finder.CorrectCaseAllTableNames();
                    OutputPane.WriteMessageAndActivatePane("Correcting the case of table names...done");
                });
                
                task.Start();
                
                if (task.Exception != null)
                    throw task.Exception;
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("Error correcting table name case: {0}", ex.Message);
            }
        }

        private void EnableCodeCoverage(object sender, EventArgs e)
        {
            CodeCoverageTaggerSettings.Enabled = !CodeCoverageTaggerSettings.Enabled;
        }

        private void EnableClippy(object sender, EventArgs e)
        {
            CallWrapper();
            ClippySettings.Enabled = !ClippySettings.Enabled;
        }

        private void RewriteNonSargableIsNull(object sender, EventArgs e)
        {
            try
            {
                CallWrapper();
                var oldDoc = GetCurrentDocumentText();
                var newDoc = oldDoc;

                var rewriter = new NonSargableRewrites(oldDoc);
                var queries = ScriptDom.GetQuerySpecifications(oldDoc);
                foreach (var rep in rewriter.GetReplacements(queries))
                {
                    newDoc = newDoc.Replace(rep.Original, rep.Replacement);
                    OutputPane.WriteMessage("Non-Sargable IsNull re-written from \r\n\"{0}\" \r\nto\r\n\"{1}\"\r\n", rep.Original, rep.Replacement);
                }

                if(oldDoc != newDoc)
                    SetCurrentDocumentText(newDoc);

            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("Error re-writing non sargable isnulls {0}", ex.Message);
            }

        }

        private void FindDuplicateIndexes(object sender, EventArgs e)
        {
            try
            {
                CallWrapper();
                var task = new System.Threading.Tasks.Task(() =>
                {
                    OutputPane.WriteMessageAndActivatePane("Finding Duplicate Indexes...");
                    var finder = new DuplicateIndexFinder();
                    finder.ShowDuplicateIndexes();
                    OutputPane.WriteMessageAndActivatePane("Finding Duplicate Indexes...done");
                });

                task.Start();

                if (task.Exception != null)
                    throw task.Exception;
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("Error finding duplicate indexes: {0}", ex.Message);
            }
        }


        private void ExtractToTvf(object sender, EventArgs e)
        {
            try
            {
                CallWrapper();
                var dte = (DTE) GetService(typeof (DTE));

                if (dte.ActiveDocument == null)
                {
                    return;
                }

                var doc = dte.ActiveDocument;

                var text = GetCurrentText();
                if (String.IsNullOrEmpty(text))
                    return;

                var newText = new CodeExtractor(text).ExtractIntoFunction();

                if (text != newText && !String.IsNullOrEmpty(newText))
                {
                    doc.Activate();
                    SetCurrentText(newText);
                    OutputPane.WriteMessage("Code extracted into an inline table valued function");
                }
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("Error extracting code into a TVF: {0}", ex.Message);
            }
        }

        //NOT ALL KEYWORDS ARE done LIKE "RETURN"  or datatypes
        private void UpperCase(object sender, EventArgs e)
        {
            try
            {
                CallWrapper();
                var text = GetCurrentDocumentText();
                if (String.IsNullOrEmpty(text))
                    return;

                var newText = KeywordCaser.KeywordsToUpper(text);

                if (text != newText)
                {
                    SetCurrentDocumentText(newText);
                    OutputPane.WriteMessage("Changed keywords to UPPER CASE");
                }
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("Exception changing keywords to UPPER CASE, error: {0}", ex.Message);
                
            }
        }

        private void CallWrapper()
        {
            var dte = (DTE)GetService(typeof(DTE));
            var project = dte.ActiveDocument.ProjectItem.ContainingProject;

            //project.GetType().GetProperty("Globals").GetValue(project).GetType().GetProperty("Parent").GetValue(project.GetType().GetProperty("Globals").GetValue(project)).GetType().GetProperty("DatabaseSchemaProvider").GetValue(project.GetType().GetProperty("Globals").GetValue(project).GetType().GetProperty("Parent").GetValue(project.GetType().GetProperty("Globals").GetValue(project))).GetType().GetProperty("Platform").GetValue(project.GetType().GetProperty("Globals").GetValue(project).GetType().GetProperty("Parent").GetValue(project.GetType().GetProperty("Globals").GetValue(project)).GetType().GetProperty("DatabaseSchemaProvider").GetValue(project.GetType().GetProperty("Globals").GetValue(project).GetType().GetProperty("Parent").GetValue(project.GetType().GetProperty("Globals").GetValue(project))))
            var projectType = project.GetType();
            var globalsProperty = projectType.GetProperty("Globals");
            if (globalsProperty == null)
                return;

            var globals = globalsProperty.GetValue(project);
            var globalsType = globals.GetType();

            var parentProperty = globalsType.GetProperty("Parent");
            if (parentProperty == null)
                return;

            var parent = parentProperty.GetValue(globals);

            var schemaProviderProperty = parent.GetType().GetProperty("DatabaseSchemaProvider");
            if (schemaProviderProperty == null)
                return;

            var databaseSchemaProvider = schemaProviderProperty.GetValue(parent);

            var platformProperty = databaseSchemaProvider.GetType().GetProperty("Platform");
            if (platformProperty == null)
                return;

            var version = platformProperty.GetValue(databaseSchemaProvider);

            
            VersionDetector.SetVersion(version as string);
        }

        private void LowerCase(object sender, EventArgs e)
        {

            try
            {

                CallWrapper();
                
                var text = GetCurrentDocumentText();
                if (String.IsNullOrEmpty(text))
                    return;

                var newText = KeywordCaser.KeywordsToLower(text);

                if (text != newText)
                {
                    SetCurrentDocumentText(newText);
                    OutputPane.WriteMessage("Changed keywords to lower case");
                }
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("Exception changing keywords to UPPER CASE, error: {0}", ex.Message);
            }
        }

        private void QuickDeploy(object sender, EventArgs e)
        {
            try
            {
                CallWrapper();
                QuickDeployer.DeployFile(GetCurrentDocumentText());
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("QuickDeploy error: {0}", ex.Message);
                Log.WriteInfo("QuickDeploy error: {0}", ex.Message);
            }
            
        }

        private void SetCurrentDocumentText(string newText)
        {
            var dte = (DTE)GetService(typeof(DTE));

            if (dte.ActiveDocument == null)
            {
                return;
            }

            var doc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
            if (null == doc)
            {
                return;
            }

            var ep = doc.StartPoint.CreateEditPoint();
            ep.EndOfDocument();
            
            var length = ep.AbsoluteCharOffset;

            ep.StartOfDocument();
            ep.Delete(length);
         
            ep.Insert(newText);
        }


        private void SetCurrentText(string newText)
        {
            var dte = (DTE)GetService(typeof(DTE));

            if (dte.ActiveDocument == null)
            {
                return;
            }

            var doc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
            if (null == doc)
            {
                return;
            }

            doc.Selection.Text = newText;
        }

        private string GetCurrentDocumentText()
        {

            var dte = (DTE) GetService(typeof (DTE));

            if (dte.ActiveDocument == null)
            {
                return null;
            }

            var doc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
            if (null == doc)
            {
                return null;
            }

            var ep = doc.StartPoint.CreateEditPoint();
            ep.EndOfDocument();

            var length = ep.AbsoluteCharOffset;
            ep.StartOfDocument();
            return ep.GetText(length);
        }

        private string GetCurrentText()
        {
            var dte = (DTE)GetService(typeof(DTE));

            if (dte.ActiveDocument == null)
            {
                return null;
            }

            var doc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
            if (null == doc)
            {
                return null;
            }

            return doc.Selection.Text;
        }

        private void ClearQueryCosts(object sender, EventArgs e)
        {
            DocumentScriptCosters.GetInstance().ClearCache();
        }



        private void AddMenuItem(OleMenuCommandService mcs, int cmdId,EventHandler eventHandler )
        {
            CommandID menuCommandID;
            MenuCommand menuItem;
            menuCommandID = new CommandID(GuidList.guidSSDTDevPack_VSPackageCmdSet, cmdId);
            menuItem = new MenuCommand(eventHandler, menuCommandID);
            mcs.AddCommand(menuItem);

            var a = new OleMenuCommand(eventHandler, menuCommandID);
            a.Checked = false;
        }
        private void AddCheckableMenuItem(OleMenuCommandService mcs, int cmdId, EventHandler eventHandler)
        {
            var menuCommandID = new CommandID(GuidList.guidSSDTDevPack_VSPackageCmdSet, cmdId);
            
            ClippySettings.MenuItem = new OleMenuCommand(eventHandler, menuCommandID);

            ClippySettings.MenuItem.Checked = false;
            ClippySettings.MenuItem.BeforeQueryStatus += a_BeforeQueryStatus;
            mcs.AddCommand(ClippySettings.MenuItem);
        }

        void a_BeforeQueryStatus(object sender, EventArgs e)
        {
            ClippySettings.MenuItem.Checked = ClippySettings.Enabled;
        }


        private void AddCheckableMenuItemCodeCoverage(OleMenuCommandService mcs, int cmdId, EventHandler eventHandler)
        {
            var menuCommandID = new CommandID(GuidList.guidSSDTDevPack_VSPackageCmdSet, cmdId);

            CodeCoverageTaggerSettings.MenuItem = new OleMenuCommand(eventHandler, menuCommandID);

            CodeCoverageTaggerSettings.MenuItem.Checked = false;
            CodeCoverageTaggerSettings.MenuItem.BeforeQueryStatus += a_BeforeQueryStatusCodeCoverage;
            mcs.AddCommand(CodeCoverageTaggerSettings.MenuItem);
        }

        void a_BeforeQueryStatusCodeCoverage(object sender, EventArgs e)
        {
            CodeCoverageTaggerSettings.MenuItem.Checked = CodeCoverageTaggerSettings.Enabled;
        }



        private void ToggleQueryCosts(object sender, EventArgs e)
        {
            try
            {
                var dte = (DTE) GetService(typeof (DTE));

                if (dte.ActiveDocument == null)
                {
                    return;
                }

                var doc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (null == doc)
                {
                    return;
                }

                var ep = doc.StartPoint.CreateEditPoint();

                ep.EndOfDocument();

                var length = ep.AbsoluteCharOffset;
                ep.StartOfDocument();

                var originalText = ep.GetText(length);

                DocumentScriptCosters.SetDte(dte);

                var coster = DocumentScriptCosters.GetInstance().GetCoster();
                if (coster == null)
                    return;

                if (coster.ShowCosts)
                {
                    coster.ShowCosts = false;
                }
                else
                {
                    coster.ShowCosts = true;
                    coster.AddCosts(originalText, dte.ActiveDocument);
                }
            }
            catch (Exception ee)
            {
                OutputPane.WriteMessage("ToggleQueryCosts error: {0}", ee.Message);
                Log.WriteInfo("ToggleQueryCosts error: {0}", ee.Message);
            }
        }

        private void CreatetSQLtTest(object sender, EventArgs e)
        {
            try
            {
                CallWrapper();

                var dte = (DTE) GetService(typeof (DTE));

                if (dte.ActiveDocument == null)
                {
                    return;
                }

                var doc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (null == doc)
                {
                    return;
                }

                var ep = doc.StartPoint.CreateEditPoint();

                ep.EndOfDocument();

                var length = ep.AbsoluteCharOffset;
                ep.StartOfDocument();

                var originalText = ep.GetText(length);

                var builder = new TestBuilder(originalText, dte.ActiveDocument.ProjectItem.ContainingProject);
                builder.Go();
                //  builder.CreateTests();
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("Exception creating tSQLt tests, error: {0}", ex.Message);
            }
        }

        private void CreatetSQLtSchema(object sender, EventArgs e)
        {
            try
            {
                CallWrapper();

                var dte = (DTE) GetService(typeof (DTE));

                if (dte.ActiveDocument == null)
                {
                    return;
                }

                var doc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (null == doc)
                {
                    return;
                }

                var ep = doc.StartPoint.CreateEditPoint();

                ep.EndOfDocument();

                var length = ep.AbsoluteCharOffset;
                ep.StartOfDocument();

                var originalText = ep.GetText(length);

                var builder = new SchemaBuilder(originalText);
                builder.CreateSchemas();
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("Exception creating tSQLt schema, error: {0}", ex.Message);
            }
        }

        
        private void NameConstraintsCalled(object sender, EventArgs e)
        {
            try
            {
                CallWrapper();

                var dte = (DTE) GetService(typeof (DTE));
                if (null == dte || dte.ActiveDocument == null)
                {
                    return;
                }

                var doc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (null == doc)
                {
                    return;
                }

                var ep = doc.StartPoint.CreateEditPoint();

                ep.EndOfDocument();

                var length = ep.AbsoluteCharOffset;
                ep.StartOfDocument();

                var originalText = ep.GetText(length);

                var namer = new ConstraintNamer(originalText);
                var modifiedText = namer.Go();

                if (originalText != modifiedText)
                {
                    ep.Delete(length);
                    ep.Insert(modifiedText);
                }
            }
            catch (Exception ex)
            {
                OutputPane.WriteMessage("Exception naming constraints, error: {0}", ex.Message);
            }
        }
    }
}