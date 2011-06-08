﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AxeSoftware.Quest.EditorControls
{
    [ControlType("script")]
    public partial class ScriptEditorControl : UserControl, IElementEditorControl
    {
        private ControlDataHelper<IEditableScripts> m_helper;
        private EditorController m_controller;
        private List<IElementEditorControl> m_subControls = new List<IElementEditorControl>();
        private IEditableScripts m_scripts;
        private IEditorData m_data;
        private bool m_readOnly;
        private bool m_saving;

        internal event Action Initialise;

        public ScriptEditorControl()
        {
            InitializeComponent();
            SetEditButtonsEnabled(false);
            m_helper = new ControlDataHelper<IEditableScripts>(this);
            m_helper.Initialise += m_helper_Initialise;

            ctlToolbar.Delete += ctlToolbar_Delete;
            ctlToolbar.MoveUp += ctlToolbar_MoveUp;
            ctlToolbar.MoveDown += ctlToolbar_MoveDown;
            ctlToolbar.Cut += ctlToolbar_Cut;
            ctlToolbar.Copy += ctlToolbar_Copy;
            ctlToolbar.Paste += ctlToolbar_Paste;
            ctlToolbar.PopOut += ctlToolbar_PopOut;
        }

        void m_helper_Initialise()
        {
            if (m_controller != null) throw new InvalidOperationException("Control has already been initialised");
            m_controller = m_helper.Controller;
            ctlScriptAdder.CloseButtonVisible = false;
            ctlScriptAdder.Initialise(m_controller);
            ctlScriptAdder.AddScript += ctlScriptAdder_AddScript;

            if (Initialise != null) Initialise();
        }

        private string ElementName
        {
            get { return m_data == null ? null : m_data.Name; }
        }

        void ctlScriptAdder_AddScript(string script)
        {
            if (m_scripts == null)
            {
                m_scripts = m_controller.CreateNewEditableScripts(ElementName, m_helper.ControlDefinition.Attribute, script, true);
            }
            else
            {
                m_scripts.AddNew(script, ElementName);
            }

            RefreshScriptsList();
        }

        public IControlDataHelper Helper
        {
            get { return m_helper; }
        }

        public void Populate(IEditorData data)
        {
            m_data = data;
            if (data == null) return;
            m_helper.StartPopulating();

            Populate(m_helper.Populate(data));

            m_readOnly = data.ReadOnly;
            lstScripts.IsEnabled = m_helper.CanEdit(data) && !data.ReadOnly;
            adderExpander.IsEnabled = m_helper.CanEdit(data) && !data.ReadOnly;
            if (m_readOnly)
            {
                SetEditButtonsEnabled(false);
            }

            m_helper.FinishedPopulating();
        }

        public void Populate(IEditableScripts script)
        {
            if (m_scripts != null)
            {
                m_scripts.Updated -= m_scripts_Updated;
            }
            m_scripts = script;
            if (m_scripts != null)
            {
                m_scripts.Updated += m_scripts_Updated;
            }

            RefreshScriptsList();
        }

        public void Save()
        {
            m_saving = true;
            foreach (IElementEditorControl subControl in m_subControls)
            {
                subControl.Save();
            }
            m_saving = false;
        }

        private void RefreshScriptsList()
        {
            if (m_saving) return;
            m_scriptParameterControlMap.Clear();
            lstScripts.Items.Clear();

            foreach (IElementEditorControl subCtl in m_subControls)
            {
                ((Control)subCtl).GotFocus -= SubControl_GotFocus;
                subCtl.Helper.Dirty -= SubControl_Dirty;
                subCtl.Helper.RequestParentElementEditorSave -= SubControl_RequestParentElementEditorSave;

                // Populating with null data is a signal to subcontrols to detach any event handlers from the previous data,
                // so they don't respond to updates for data which they are not currently editing.
                subCtl.Populate(null);
            }

            m_subControls.Clear();

            if (m_scripts == null || m_scripts.Scripts == null)
            {
                lstScripts.Visibility = Visibility.Collapsed;
                return;
            }

            foreach (IEditableScript script in m_scripts.Scripts)
            {
                AddScript(script);
            }

            lstScripts.Visibility = (lstScripts.Items.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
            adderExpander.IsExpanded = (lstScripts.Items.Count == 0);
        }

        void m_scripts_Updated(object sender, EditableScriptsUpdatedEventArgs e)
        {
            if (e.UpdatedScriptEventArgs != null)
            {
                // For nested script updates (e.g. an "if"'s "then" script), there's no need to repopulate the parent script control,
                // as the child script control will have already updated itself to reflect the change.
                if (e.UpdatedScriptEventArgs.IsNestedScriptUpdate)
                {
                    return;
                }

                if (e.UpdatedScriptEventArgs.IsParameterUpdate)
                {
                    // might be an update for a nested script, so check it's "ours" first, and if so, update the control
                    if (m_scriptParameterControlMap.ContainsKey(e.UpdatedScript))
                    {
                        // An entire script refresh would be overkill - we just need to update the specified parameter
                        IElementEditorControl control = GetScriptParameterControl(e.UpdatedScript, e.UpdatedScriptEventArgs.Index.ToString());
                        if (e.UpdatedScript.Type == ScriptType.Normal)
                        {
                            control.Populate(m_controller.GetScriptEditorData(e.UpdatedScript));
                        }
                        else
                        {
                            ((IfEditor)control).Populate((EditableIfScript)e.UpdatedScript);
                        }
                    }
                    return;
                }

                if (e.UpdatedScriptEventArgs.IsNamedParameterUpdate)
                {
                    // might be an update for a nested script, so check it's "ours" first, and if so, update the control
                    if (m_scriptParameterControlMap.ContainsKey(e.UpdatedScript))
                    {
                        // currently, named parameter updates are only used for "else if" expressions. The entire "if" is
                        // registered for attribute "0".
                        IElementEditorControl control = GetScriptParameterControl(e.UpdatedScript, "0");
                        ((IfEditor)control).Populate((EditableIfScript)e.UpdatedScript);
                    }
                    return;
                }

                if (e.UpdatedScript.Type == ScriptType.If)
                {
                    // "If" scripts have their own events which the editor controls handle themselves to update
                    // appropriately, so there is nothing further to do in response to the Updated event.
                    return;
                }
            }

            RefreshScriptsList();
        }

        private void AddScript(IEditableScript script)
        {
            ListBoxItem newItem = new ListBoxItem();
            newItem.HorizontalAlignment = HorizontalAlignment.Stretch;

            // a DockPanel is used as the ItemsPanel, so that when nested Expanders are collapsed,
            // they don't leave a blank space
            DockPanel.SetDock(newItem, Dock.Top);
            lstScripts.Items.Add(newItem);

            if (script.Type != ScriptType.If)
            {
                Grid parentGrid = new Grid();
                parentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                parentGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                newItem.Content = parentGrid;

                AddScriptControls(newItem, parentGrid, script);
            }
            else
            {
                IfEditor newIfEditor = new IfEditor();
                newIfEditor.Padding = new Thickness(3);
                newIfEditor.HorizontalAlignment = HorizontalAlignment.Stretch;
                AddEditorControl(newIfEditor, newItem, null);
                newItem.Content = newIfEditor;
                newIfEditor.Populate((EditableIfScript)script);

                // Ensure the expression responds to updates e.g. from undo/redo. The nested scripts will take
                // care of themselves.
                // TO DO: This isn't particularly efficient, as when m_scripts_Updated is triggered it repopulates
                // the entire "if" script when an if/elseif expression changes. It should only need to update the
                // changed expression.
                AddToScriptParameterControlMap(script, "0", newIfEditor);
            }
        }

        private Grid NewScriptControlGrid(Grid parent)
        {
            Grid grid = new Grid();
            grid.HorizontalAlignment = HorizontalAlignment.Stretch;
            parent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(grid, parent.RowDefinitions.Count - 1);
            parent.Children.Add(grid);
            return grid;
        }

        private void AddScriptControls(ListBoxItem listItem, Grid parentGrid, IEditableScript script)
        {
            IEditorDefinition definition = m_controller.GetEditorDefinition(script.EditorName);
            IEditorData data = m_controller.GetScriptEditorData(script);
            Grid grid = NewScriptControlGrid(parentGrid);

            foreach (IEditorControl ctl in definition.Controls)
            {
                bool isFullWidthControl = false;
                string controlType = ctl.ControlType;
                if (ctl.ControlType == "script")
                {
                    controlType = "scriptexpander";
                    isFullWidthControl = true;
                }
                if (ctl.ControlType == "scriptdictionary" || ctl.ControlType == "list")
                {
                    isFullWidthControl = true;
                }
                Control newControl = ControlFactory.CreateEditorControl(m_controller, controlType);
                newControl.VerticalAlignment = VerticalAlignment.Top;
                if (newControl is LabelControl)
                {
                    newControl.Padding = new Thickness(3, 6, 3, 3);
                }
                else
                {
                    newControl.Padding = new Thickness(3);
                }

                if (isFullWidthControl)
                {
                    newControl.HorizontalAlignment = HorizontalAlignment.Stretch;
                }

                if (ctl.GetBool("breakbefore"))
                {
                    // Create a "line break" by putting this and subsequent controls in a new horizontal grid,
                    // underneath the previous one. We're using a grid instead of a StackPanel as script expanders
                    // won't take the full width of the list otherwise.
                    grid = NewScriptControlGrid(parentGrid);

                    // Indent the new line
                    newControl.Padding = new Thickness(newControl.Padding.Left + 20, newControl.Padding.Top, newControl.Padding.Right, newControl.Padding.Bottom);
                }

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, isFullWidthControl ? GridUnitType.Star : GridUnitType.Auto) });
                Grid.SetColumn(newControl, grid.ColumnDefinitions.Count - 1);
                grid.Children.Add(newControl);

                IElementEditorControl editorCtl = newControl as IElementEditorControl;
                if (editorCtl != null)
                {
                    AddEditorControl(editorCtl, listItem, ctl);
                    editorCtl.Populate(data);
                    AddToScriptParameterControlMap(script, ctl.Attribute, editorCtl);
                }
            }
        }

        private void AddEditorControl(IElementEditorControl control, ListBoxItem listItem, IEditorControl ctl)
        {
            // TO DO: set readonly if required

            m_subControls.Add(control);
            ((Control)control).Tag = listItem;
            ((Control)control).GotFocus += SubControl_GotFocus;
            control.Helper.Dirty += SubControl_Dirty;
            control.Helper.RequestParentElementEditorSave += SubControl_RequestParentElementEditorSave;
            control.Helper.DoInitialise(m_controller, ctl);
        }

        private Dictionary<IEditableScript, Dictionary<string, IElementEditorControl>> m_scriptParameterControlMap = new Dictionary<IEditableScript, Dictionary<string, IElementEditorControl>>();

        private void AddToScriptParameterControlMap(IEditableScript script, string parameter, IElementEditorControl control)
        {
            if (parameter == null) return;

            if (!m_scriptParameterControlMap.ContainsKey(script))
            {
                m_scriptParameterControlMap.Add(script, new Dictionary<string, IElementEditorControl>());
            }
            m_scriptParameterControlMap[script].Add(parameter, control);
        }

        private IElementEditorControl GetScriptParameterControl(IEditableScript script, string parameter)
        {
            return m_scriptParameterControlMap[script][parameter];
        }

        void SubControl_GotFocus(object sender, RoutedEventArgs e)
        {
            // select parent script item when a subcontrol is clicked
            lstScripts.SelectedItem = ((FrameworkElement)sender).Tag;
        }

        void SubControl_Dirty(object sender, DataModifiedEventArgs e)
        {
            m_helper.RaiseDirtyEvent(m_scripts);
        }

        void SubControl_RequestParentElementEditorSave()
        {
        }

        private int[] GetSelectedIndicesArray()
        {
            List<int> selectedIndices = new List<int>();
            for (int i = 0; i < lstScripts.Items.Count; i++)
            {
                if (lstScripts.SelectedItems.Contains(lstScripts.Items[i]))
                {
                    selectedIndices.Add(i);
                }
            }

            return selectedIndices.ToArray();
        }

        private void SetSelectedIndex(int index)
        {
            lstScripts.SelectedItems.Clear();
            lstScripts.SelectedItem = lstScripts.Items[index];
        }

        void ctlToolbar_Delete()
        {
            Save();
            m_scripts.Remove(GetSelectedIndicesArray());
        }

        void ctlToolbar_MoveUp()
        {
            int index = lstScripts.SelectedIndex;
            Save();
            if (index <= 0) return;
            m_controller.StartTransaction("Move script up");
            m_scripts.Swap(index - 1, index);
            m_controller.EndTransaction();
            SetSelectedIndex(index - 1);
        }

        void ctlToolbar_MoveDown()
        {
            int index = lstScripts.SelectedIndex;
            Save();
            if (index >= m_scripts.Count - 1) return;
            m_controller.StartTransaction("Move script down");
            m_scripts.Swap(index, index + 1);
            m_controller.EndTransaction();
            SetSelectedIndex(index + 1);
        }

        void ctlToolbar_Cut()
        {
            Save();
            m_scripts.Cut(GetSelectedIndicesArray());
        }

        void ctlToolbar_Copy()
        {
            Save();
            m_scripts.Copy(GetSelectedIndicesArray());
            ctlToolbar.CanPaste = m_controller.CanPasteScript();
        }

        void ctlToolbar_Paste()
        {
            int index = lstScripts.SelectedIndex;
            if (index < 0) return;
            Save();
            if (m_scripts == null)
            {
                m_controller.StartTransaction("Paste script");
                m_scripts = m_controller.CreateNewEditableScripts(ElementName, m_helper.ControlDefinition.Attribute, null, false);
                m_scripts.Paste(index, false);
                m_controller.EndTransaction();
                RefreshScriptsList();
            }
            else
            {
                m_scripts.Paste(index, true);
            }
            Save();
            SetSelectedIndex(index);
        }

        void ctlToolbar_PopOut()
        {
            ScriptEditorPopOut popOut = new ScriptEditorPopOut();
            popOut.ScriptEditor.Helper.Dirty += PopOut_Dirty;
            popOut.ScriptEditor.Helper.RequestParentElementEditorSave += PopOut_RequestParentElementEditorSave;
            popOut.ScriptEditor.Helper.DoInitialise(m_helper.Controller, m_helper.ControlDefinition);
            popOut.ScriptEditor.Populate(m_scripts);
            popOut.ShowDialog();
            m_scripts = popOut.ScriptEditor.m_scripts;
        }

        void PopOut_RequestParentElementEditorSave()
        {
            m_helper.RaiseRequestParentElementEditorSaveEvent();
        }

        void PopOut_Dirty(object sender, DataModifiedEventArgs e)
        {
            m_helper.RaiseDirtyEvent(m_scripts);
            RefreshScriptsList();
        }

        private void SetEditButtonsEnabled(bool enabled)
        {
            // Copy is enabled even in read-only mode
            ctlToolbar.CanCopy = enabled;

            // Paste is enabled only if not in read-only mode, and if we have something to paste
            ctlToolbar.CanPaste = enabled && (!m_readOnly) && m_controller != null && m_controller.CanPasteScript();

            if (m_readOnly) enabled = false;
            ctlToolbar.CanDelete = enabled;
            ctlToolbar.CanMoveUp = enabled && lstScripts.SelectedIndex > 0;
            ctlToolbar.CanMoveDown = enabled && lstScripts.SelectedIndex < m_scripts.Count - 1;
            ctlToolbar.CanCut = enabled;
        }

        private void lstScripts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetEditButtonsEnabled(lstScripts.SelectedItems.Count > 0);
        }

        public void HidePopOutButton()
        {
            ctlToolbar.HidePopOutButton();
        }

        internal IEditorControl Definition
        {
            get { return m_helper.ControlDefinition; }
        }

        internal IEditableScripts Scripts
        {
            get { return m_scripts; }
        }
    }
}