using System;
using GCam.Core.Diagnostics;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace GCam.SolidWorks.PropertyPages
{
    /// <summary>
    /// Entry point 7. Base class for every G-CAM PropertyManager page: implements all
    /// thirty-seven handler methods with the try/catch once, so no page can forget one.
    /// </summary>
    /// <remarks>
    /// Each interface method is implemented *explicitly* and does nothing but call a
    /// protected virtual of the same name. That is what lets a page override
    /// <c>OnButtonPress</c> with no error handling of its own while SOLIDWORKS still
    /// only ever reaches the wrapped version - the explicit implementation is not
    /// visible to derived classes, so an override cannot shadow the boundary.
    ///
    /// Two things are deliberately not defaulted to "failure":
    ///
    /// * The page-navigation handlers return <c>true</c> even after an exception. They
    ///   are asking permission to move; refusing would strand the user on a page that
    ///   has already gone wrong.
    /// * <see cref="OnSubmitSelection"/> does the opposite and rejects, because a
    ///   validation check that threw has not established that the selection is usable,
    ///   and accepting unvalidated geometry is how a toolpath ends up cutting air.
    ///
    /// COM visibility: this assembly carries no assembly-level
    /// <c>[ComVisible(false)]</c>, so its public types are COM-visible by default and
    /// SOLIDWORKS can QueryInterface a handler for IPropertyManagerPage2Handler9
    /// without any attributes here. Derived pages must therefore never have a public
    /// parameterless constructor, or regasm will register them as creatable classes.
    /// </remarks>
    public abstract class PmpHandlerBase : IPropertyManagerPage2Handler9
    {
        private readonly ErrorHandler _errors;

        protected PmpHandlerBase(ErrorHandler errors)
        {
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        }

        /// <summary>The handler that every boundary below reports through.</summary>
        protected ErrorHandler Errors => _errors;

        /// <param name="quiet">
        /// Log only. Used for the handlers SOLIDWORKS fires continuously - pre-select
        /// hover, keystrokes, slider drags, menu repaints - where a dialog would be an
        /// unkillable modal storm.
        /// </param>
        private void Handle(Exception ex, string member, bool quiet = false)
        {
            _errors.Handle(ex, GetType().Name + "." + member, quiet);
        }

        // ---- Page lifecycle ------------------------------------------------

        void IPropertyManagerPage2Handler9.AfterActivation()
        {
            try { AfterActivation(); }
            catch (Exception ex) { Handle(ex, nameof(AfterActivation)); }
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            try { OnClose((swPropertyManagerPageCloseReasons_e)Reason); }
            catch (Exception ex) { Handle(ex, nameof(OnClose)); }
        }

        void IPropertyManagerPage2Handler9.AfterClose()
        {
            try { AfterClose(); }
            catch (Exception ex) { Handle(ex, nameof(AfterClose)); }
        }

        protected virtual void AfterActivation() { }

        /// <summary>
        /// The page is about to close. SOLIDWORKS allows almost no real work here -
        /// the page and its command are already going away. Do it in
        /// <see cref="AfterClose"/> instead.
        /// </summary>
        protected virtual void OnClose(swPropertyManagerPageCloseReasons_e reason) { }

        protected virtual void AfterClose() { }

        // ---- Page-level buttons --------------------------------------------

        bool IPropertyManagerPage2Handler9.OnHelp()
        {
            try { return OnHelp(); }
            catch (Exception ex) { Handle(ex, nameof(OnHelp)); return false; }
        }

        bool IPropertyManagerPage2Handler9.OnPreviousPage()
        {
            try { return OnPreviousPage(); }
            catch (Exception ex) { Handle(ex, nameof(OnPreviousPage)); return true; }
        }

        bool IPropertyManagerPage2Handler9.OnNextPage()
        {
            try { return OnNextPage(); }
            catch (Exception ex) { Handle(ex, nameof(OnNextPage)); return true; }
        }

        bool IPropertyManagerPage2Handler9.OnPreview()
        {
            try { return OnPreview(); }
            catch (Exception ex) { Handle(ex, nameof(OnPreview)); return true; }
        }

        void IPropertyManagerPage2Handler9.OnWhatsNew()
        {
            try { OnWhatsNew(); }
            catch (Exception ex) { Handle(ex, nameof(OnWhatsNew)); }
        }

        void IPropertyManagerPage2Handler9.OnUndo()
        {
            try { OnUndo(); }
            catch (Exception ex) { Handle(ex, nameof(OnUndo)); }
        }

        void IPropertyManagerPage2Handler9.OnRedo()
        {
            try { OnRedo(); }
            catch (Exception ex) { Handle(ex, nameof(OnRedo)); }
        }

        /// <summary>False leaves SOLIDWORKS to open its own help.</summary>
        protected virtual bool OnHelp() => false;

        protected virtual bool OnPreviousPage() => true;

        protected virtual bool OnNextPage() => true;

        protected virtual bool OnPreview() => true;

        protected virtual void OnWhatsNew() { }

        protected virtual void OnUndo() { }

        protected virtual void OnRedo() { }

        // ---- Controls ------------------------------------------------------

        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id)
        {
            try { return OnTabClicked(Id); }
            catch (Exception ex) { Handle(ex, nameof(OnTabClicked)); return true; }
        }

        void IPropertyManagerPage2Handler9.OnGroupExpand(int Id, bool Expanded)
        {
            try { OnGroupExpand(Id, Expanded); }
            catch (Exception ex) { Handle(ex, nameof(OnGroupExpand)); }
        }

        void IPropertyManagerPage2Handler9.OnGroupCheck(int Id, bool Checked)
        {
            try { OnGroupCheck(Id, Checked); }
            catch (Exception ex) { Handle(ex, nameof(OnGroupCheck)); }
        }

        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked)
        {
            try { OnCheckboxCheck(Id, Checked); }
            catch (Exception ex) { Handle(ex, nameof(OnCheckboxCheck)); }
        }

        void IPropertyManagerPage2Handler9.OnOptionCheck(int Id)
        {
            try { OnOptionCheck(Id); }
            catch (Exception ex) { Handle(ex, nameof(OnOptionCheck)); }
        }

        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            try { OnButtonPress(Id); }
            catch (Exception ex) { Handle(ex, nameof(OnButtonPress)); }
        }

        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text)
        {
            try { OnTextboxChanged(Id, Text); }
            catch (Exception ex) { Handle(ex, nameof(OnTextboxChanged)); }
        }

        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value)
        {
            try { OnNumberboxChanged(Id, Value); }
            catch (Exception ex) { Handle(ex, nameof(OnNumberboxChanged)); }
        }

        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value)
        {
            try { OnNumberBoxTrackingCompleted(Id, Value); }
            catch (Exception ex) { Handle(ex, nameof(OnNumberBoxTrackingCompleted)); }
        }

        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text)
        {
            try { OnComboboxEditChanged(Id, Text); }
            catch (Exception ex) { Handle(ex, nameof(OnComboboxEditChanged)); }
        }

        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            try { OnComboboxSelectionChanged(Id, Item); }
            catch (Exception ex) { Handle(ex, nameof(OnComboboxSelectionChanged)); }
        }

        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item)
        {
            try { OnListboxSelectionChanged(Id, Item); }
            catch (Exception ex) { Handle(ex, nameof(OnListboxSelectionChanged)); }
        }

        void IPropertyManagerPage2Handler9.OnListboxRMBUp(int Id, int PosX, int PosY)
        {
            try { OnListboxRMBUp(Id, PosX, PosY); }
            catch (Exception ex) { Handle(ex, nameof(OnListboxRMBUp)); }
        }

        void IPropertyManagerPage2Handler9.OnSliderPositionChanged(int Id, double Value)
        {
            try { OnSliderPositionChanged(Id, Value); }
            catch (Exception ex) { Handle(ex, nameof(OnSliderPositionChanged), quiet: true); }
        }

        void IPropertyManagerPage2Handler9.OnSliderTrackingCompleted(int Id, double Value)
        {
            try { OnSliderTrackingCompleted(Id, Value); }
            catch (Exception ex) { Handle(ex, nameof(OnSliderTrackingCompleted)); }
        }

        protected virtual bool OnTabClicked(int id) => true;

        protected virtual void OnGroupExpand(int id, bool expanded) { }

        protected virtual void OnGroupCheck(int id, bool isChecked) { }

        protected virtual void OnCheckboxCheck(int id, bool isChecked) { }

        protected virtual void OnOptionCheck(int id) { }

        protected virtual void OnButtonPress(int id) { }

        protected virtual void OnTextboxChanged(int id, string text) { }

        protected virtual void OnNumberboxChanged(int id, double value) { }

        protected virtual void OnNumberBoxTrackingCompleted(int id, double value) { }

        protected virtual void OnComboboxEditChanged(int id, string text) { }

        protected virtual void OnComboboxSelectionChanged(int id, int item) { }

        protected virtual void OnListboxSelectionChanged(int id, int item) { }

        protected virtual void OnListboxRMBUp(int id, int posX, int posY) { }

        protected virtual void OnSliderPositionChanged(int id, double value) { }

        protected virtual void OnSliderTrackingCompleted(int id, double value) { }

        // ---- Selection -----------------------------------------------------

        // ItemText is `ref`, not `out`: the interop declares it [In, Out] even though
        // the help documents it as an output. The virtual below takes `out`, which is
        // what an override actually wants, and the local bridges the two.
        bool IPropertyManagerPage2Handler9.OnSubmitSelection(
            int Id, object Selection, int SelType, ref string ItemText)
        {
            // This is the signature that killed the Boundary.Run() wrapper idea: C#
            // cannot capture a ref or out parameter in a lambda. See
            // docs/error-handling.md.
            try
            {
                string itemText;
                bool accepted = OnSubmitSelection(Id, Selection, SelType, out itemText);
                ItemText = itemText;
                return accepted;
            }
            catch (Exception ex)
            {
                // Fires on every pre-select hover, so quiet. Reject rather than
                // accept: a check that threw has proved nothing about the selection.
                Handle(ex, nameof(OnSubmitSelection), quiet: true);
                ItemText = null;
                return false;
            }
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id)
        {
            try { OnSelectionboxFocusChanged(Id); }
            catch (Exception ex) { Handle(ex, nameof(OnSelectionboxFocusChanged)); }
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count)
        {
            try { OnSelectionboxListChanged(Id, Count); }
            catch (Exception ex) { Handle(ex, nameof(OnSelectionboxListChanged)); }
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id)
        {
            try { OnSelectionboxCalloutCreated(Id); }
            catch (Exception ex) { Handle(ex, nameof(OnSelectionboxCalloutCreated)); }
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id)
        {
            try { OnSelectionboxCalloutDestroyed(Id); }
            catch (Exception ex) { Handle(ex, nameof(OnSelectionboxCalloutDestroyed)); }
        }

        /// <summary>
        /// Vets one candidate for a selection box. <paramref name="selectionType"/> is
        /// a <c>swSelectType_e</c>. Return false to refuse the selection.
        /// </summary>
        protected virtual bool OnSubmitSelection(
            int id, object selection, int selectionType, out string itemText)
        {
            itemText = null;
            return true;
        }

        protected virtual void OnSelectionboxFocusChanged(int id) { }

        protected virtual void OnSelectionboxListChanged(int id, int count) { }

        protected virtual void OnSelectionboxCalloutCreated(int id) { }

        protected virtual void OnSelectionboxCalloutDestroyed(int id) { }

        // ---- Focus, keystrokes, hosted controls, popup menus ---------------

        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id)
        {
            try { OnGainedFocus(Id); }
            catch (Exception ex) { Handle(ex, nameof(OnGainedFocus), quiet: true); }
        }

        void IPropertyManagerPage2Handler9.OnLostFocus(int Id)
        {
            try { OnLostFocus(Id); }
            catch (Exception ex) { Handle(ex, nameof(OnLostFocus), quiet: true); }
        }

        bool IPropertyManagerPage2Handler9.OnKeystroke(
            int Wparam, int Message, int Lparam, int Id)
        {
            try { return OnKeystroke(Wparam, Message, Lparam, Id); }
            catch (Exception ex)
            {
                // Once per key press. Falling through to SOLIDWORKS is the safe answer.
                Handle(ex, nameof(OnKeystroke), quiet: true);
                return false;
            }
        }

        int IPropertyManagerPage2Handler9.OnActiveXControlCreated(int Id, bool Status)
        {
            try { return (int)OnActiveXControlCreated(Id, Status); }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnActiveXControlCreated));
                return (int)swHandleActiveXCreationFailure_e.swHandleActiveXCreationFailure_Continue;
            }
        }

        int IPropertyManagerPage2Handler9.OnWindowFromHandleControlCreated(int Id, bool Status)
        {
            try { return (int)OnWindowFromHandleControlCreated(Id, Status); }
            catch (Exception ex)
            {
                Handle(ex, nameof(OnWindowFromHandleControlCreated));
                return (int)swHandleActiveXCreationFailure_e.swHandleActiveXCreationFailure_Continue;
            }
        }

        void IPropertyManagerPage2Handler9.OnPopupMenuItem(int Id)
        {
            try { OnPopupMenuItem(Id); }
            catch (Exception ex) { Handle(ex, nameof(OnPopupMenuItem)); }
        }

        void IPropertyManagerPage2Handler9.OnPopupMenuItemUpdate(int Id, ref int retval)
        {
            // Another out-shaped signature - see OnSubmitSelection.
            try
            {
                OnPopupMenuItemUpdate(Id, ref retval);
            }
            catch (Exception ex)
            {
                // Called whenever Windows repaints the menu, so quiet. 0 is
                // "unchecked and greyed out": a menu item we cannot describe is one
                // the user should not be able to pick.
                Handle(ex, nameof(OnPopupMenuItemUpdate), quiet: true);
                retval = 0;
            }
        }

        protected virtual void OnGainedFocus(int id) { }

        protected virtual void OnLostFocus(int id) { }

        /// <summary>
        /// True means the add-in consumed the keystroke and SOLIDWORKS should stop
        /// processing it. Only called when the page was created with
        /// <c>swPropertyManagerOptions_HandleKeystrokes</c>.
        /// </summary>
        protected virtual bool OnKeystroke(int wParam, int message, int lParam, int id) => false;

        protected virtual swHandleActiveXCreationFailure_e OnActiveXControlCreated(int id, bool created)
            => swHandleActiveXCreationFailure_e.swHandleActiveXCreationFailure_Continue;

        protected virtual swHandleActiveXCreationFailure_e OnWindowFromHandleControlCreated(int id, bool created)
            => swHandleActiveXCreationFailure_e.swHandleActiveXCreationFailure_Continue;

        protected virtual void OnPopupMenuItem(int id) { }

        /// <summary>
        /// Reports a popup menu item's state: 0 unchecked+disabled, 1 unchecked+enabled,
        /// 2 checked+disabled, 3 checked+enabled.
        /// </summary>
        protected virtual void OnPopupMenuItemUpdate(int id, ref int state) { }
    }
}
