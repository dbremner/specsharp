#define CCI_TRACING
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows; 
using Microsoft.VisualStudio.OLE.Interop;
using System.Xml;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Collections;
using System.Text;

namespace Microsoft.VisualStudio.Package{

  public enum HierarchyNodeType {
    Root,
    RefFolder,
    Reference,
    Folder,
    File, // text file
  }

  public enum HierarchyAddType {
    addNewItem,
    addExistingItem
  }

  public enum Commands { 
    Compilable       = 201
  }

  //=========================================================================
  /// <summary>
  /// An object that deals with user interaction via a GUI in the form a hierarchy: a parent node with zero or more child nodes, each of which
  /// can itself be a hierarchy.
  /// </summary>
  [IDispatchImpl(IDispatchImplType.SystemDefinedImpl)]
  public class HierarchyNode : IVsUIHierarchy,
    IVsPersistHierarchyItem2, 
    Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget, 
    IVsHierarchyDropDataSource2, 
    IVsHierarchyDropDataTarget,
    IVsHierarchyDeleteHandler, 
    IVsComponentUser, 
    IComparable  { //, IVsBuildStatusCallback 

    // fm: the next GUIDs are declared to identify hierarchytypes
    public static Guid guidItemTypePhysicalFile   = new Guid("6bb5f8ee-4483-11d3-8bcf-00c04f8ec28c");
    public static Guid guidItemTypePhysicalFolder = new Guid("6bb5f8ef-4483-11d3-8bcf-00c04f8ec28c");
    public static Guid guidItemTypeVirtualFolder  = new Guid("6bb5f8f0-4483-11d3-8bcf-00c04f8ec28c");
    public static Guid guidItemTypeSubProject     = new Guid("EA6618E8-6e24-4528-94be-6889fe1648c5");
                                               
    private CookieMap hierarchyEventSinks = new CookieMap();

    protected ProjectManager projectMgr; //TODO: keeping root node pointer around, and assuming it is a project manager seems unfortunate
    protected XmlElement xmlNode;
    protected HierarchyNodeType nodeType;
    protected HierarchyNode parentNode;
    protected HierarchyNode nextSibling;
    protected HierarchyNode firstChild;
    protected HierarchyNode lastChild;
    protected bool isExpanded;
    protected uint hierarchyId;
    protected ArrayList itemsDragged; // list HierarchyNodes
    protected uint docCookie;
    protected bool hasDesigner;

    enum HierarchyWindowCommands {
      RightClick =  1,
      DoubleClick = 2,
      EnterKey  =   3,
    }
    enum DropEffect { None, Copy=1, Move=2, Link=4 }; // oleidl.h
    bool dragSource;
    DropDataType _ddt;

    const int MAX_PATH          = 260; // windef.h
    
    public HierarchyNode() {
      this.nodeType = HierarchyNodeType.Root;      
    }
    
    public HierarchyNode(ProjectManager root, HierarchyNodeType type, XmlElement e){
      this.projectMgr = root;
      this.nodeType = type;
      this.xmlNode = e;
      this.hierarchyId = this.projectMgr.ItemIdMap.Add(this);      
    }

    /// <summary>
    /// note that here the directorypath needs to end with a backslash...
    /// </summary>
    /// <param name="root"></param>
    /// <param name="type"></param>
    /// <param name="strDirectoryPath"></param>
    public HierarchyNode(ProjectManager root, HierarchyNodeType type, string strDirectoryPath) {
      Uri uriBase;
      Uri uriNew;
      string relPath;

      // the path is an absolute one, need to make it relative to the project for further use in the xml document
      uriNew = new Uri(strDirectoryPath);
      uriBase = new Uri(root.projFile.BaseURI);  
      relPath = uriBase.MakeRelative(uriNew);
      relPath = relPath.Replace("/", "\\"); 

      this.projectMgr = root;
      this.nodeType = type;
      // we need to create an dangling node... just for this abstract folder type
      this.xmlNode = this.projectMgr.projFile.CreateElement("Folder");
      this.xmlNode.SetAttribute("RelPath", relPath);

      this.hierarchyId = this.projectMgr.ItemIdMap.Add(this); 
    }


    public ProjectManager ProjectMgr  {
      get  {
        return this.projectMgr; 
      }
    }

    public HierarchyNode NextSibling  {
      get  {
        return this.nextSibling; 
      }
    }

    public HierarchyNode FirstChild  {
      get  {
        return this.firstChild;
      }
    }

    public HierarchyNodeType NodeType  {
      get  {
        return this.nodeType;
      }
    }

    public uint ID  {
      get  {
        return hierarchyId;
      }
    }

    public XmlElement XmlNode  {
      get  {
        return xmlNode; 
      }
    }

    public bool HasDesigner {
      get { return this.hasDesigner;}
      set { this.hasDesigner = value; }
    }
  
    /// <summary>
    /// IComparable. Used to sort the files/folders in the treeview
    /// </summary>
    public int CompareTo(object obj) {
      HierarchyNode compadre = (HierarchyNode) obj;
      int iReturn = -1; 

      if (compadre.nodeType == this.nodeType) {
        iReturn = String.Compare(this.Caption, compadre.Caption, true, this.projectMgr.Culture);
      }
      else  {
        switch (this.nodeType) {
          case HierarchyNodeType.Reference:
            iReturn = 1; 
            break;
          case HierarchyNodeType.Folder:
            if (compadre.nodeType == HierarchyNodeType.Reference) {
              iReturn = 1;
            }
            else {
              iReturn = -1;
            }
            break;
          default:
            iReturn = -1;
            break;
        }
      }
      return iReturn;
    }
    public virtual string Caption {
      get {
        switch (this.nodeType) {
          case HierarchyNodeType.Root:
            return this.xmlNode.SelectSingleNode("//@Name").InnerText;
          case HierarchyNodeType.RefFolder:
            return this.xmlNode.Name;
          case HierarchyNodeType.Folder: {
            // if it's a folder, it might have a backslash at the end... 
            // and it might consist of Grandparent\parent\this\
            string caption = this.xmlNode.GetAttribute("RelPath");
            string [] parts;
            parts = caption.Split(Path.DirectorySeparatorChar);
            caption = parts[parts.GetUpperBound(0)-1];
            return caption;
          }
          case HierarchyNodeType.Reference:
            // we want to remove the .DLL if it's there....
            return this.xmlNode.GetAttribute("Name");
        }
        return null;
      }
      set {
        throw new NotImplementedException();
      }
    }

    
    /// <summary>
    /// this is the base method.. it returns a propertypageguid for generic properties
    /// </summary>
    ///
    public virtual Guid[] GetPropertyPageGuids() {
      Guid[] result = new Guid[1];
      result[0] = typeof(GeneralPropertyPage).GUID;
      return result;
    }

    public void OnItemAdded(HierarchyNode parent, HierarchyNode child) {
      HierarchyNode foo;
      foo = this.projectMgr == null ? this : this.projectMgr; 
      HierarchyNode prev = child.PreviousSibling;
      uint prevId = (prev != null) ? prev.hierarchyId : VsConstants.VSITEMID_NIL;
      try  {
        foreach (IVsHierarchyEvents sink in foo.hierarchyEventSinks) {
          sink.OnItemAdded(parent.hierarchyId, prevId, child.hierarchyId);          
        }
      }
      catch  {
        CCITracing.TraceCall("Error catched");
      }
    }    

    public void OnItemDeleted() {
      HierarchyNode foo;
      foo = this.projectMgr == null ? this : this.projectMgr; 
      try  {
        foreach (IVsHierarchyEvents sink in foo.hierarchyEventSinks) {
          sink.OnItemDeleted(this.hierarchyId);          
        }
      }
      catch  {
        CCITracing.TraceCall("OnItemDeleted exception");
      }
    }    

    public void OnItemsAppended(HierarchyNode parent) {
      HierarchyNode foo;
      foo = this.projectMgr == null ? this : this.projectMgr; 
      try  {
        foreach (IVsHierarchyEvents sink in foo.hierarchyEventSinks) {
          sink.OnItemsAppended(parent.hierarchyId);
        }
      }
      catch  {
        CCITracing.TraceCall("Error catched");
      }
    }

    
    public void OnPropertyChanged(HierarchyNode node, int propid, uint flags) {
      HierarchyNode foo;
      foo = this.projectMgr == null ? this : this.projectMgr; 
      foreach (IVsHierarchyEvents sink in foo.hierarchyEventSinks) {
        sink.OnPropertyChanged(node.hierarchyId, propid, flags);
      }
    }
    
    
    public void OnInvalidateItems(HierarchyNode parent) {
      HierarchyNode foo;
      foo = this.projectMgr == null ? this : this.projectMgr; 
      foreach (IVsHierarchyEvents sink in foo.hierarchyEventSinks) {
        sink.OnInvalidateItems(parent.hierarchyId);        
      }
    }


    
    //////////////////////////////////////////////////////////////////////////////////////////
    //  
    //  OnInvalidate: invalidates a single node
    //
    //////////////////////////////////////////////////////////////////////////////////////////
    public void OnInvalidate() {
      foreach (IVsHierarchyEvents sink in this.projectMgr.hierarchyEventSinks) {
        sink.OnPropertyChanged(this.ID, (int) __VSHPROPID.VSHPROPID_Caption, 0);        
      }
    }

    //==================================================================
    // HierarchyNode virtual methods.
    public virtual string Url {

      get {
        string result = null;
        switch (this.nodeType) {
          case HierarchyNodeType.Root:
            result = this.xmlNode.OwnerDocument.BaseURI;
            break;
          case HierarchyNodeType.RefFolder:
            result = this.xmlNode.Name;
            break;
          case HierarchyNodeType.Folder:
          case HierarchyNodeType.File: {
            try { 
              Uri uri = new Uri(new Uri(this.xmlNode.OwnerDocument.BaseURI), this.xmlNode.GetAttribute("RelPath"), true);
              result = uri.LocalPath; //??? 
            } catch {
              result = this.xmlNode.GetAttribute("RelPath");
            }
          }
            break;
          case HierarchyNodeType.Reference: {
            result = this.xmlNode.GetAttribute("HintPath");
            if (result == "" || result == null) {
              result = this.xmlNode.GetAttribute("AssemblyName");
              if (!result.ToLower().EndsWith(".dll")) result += ".dll";
            }
            break;
          }
        }
        return result;        
      }
      set  {
        throw new NotImplementedException();
      }
    }

    public virtual bool IsFile {
      get { return this.NodeType == HierarchyNodeType.File; }
    }

    //////////////////////////////////////////////////////////////////////////////////////////
    //  
    //  AddChild - add a node, sorted in the right location.
    //
    //////////////////////////////////////////////////////////////////////////////////////////
    public virtual void AddChild(HierarchyNode node){
      // make sure it's in the map.
      if (node.hierarchyId < this.projectMgr.ItemIdMap.Length && 
        this.projectMgr.ItemIdMap[node.hierarchyId] == null) { // reuse our hierarchy id if possible.
        this.projectMgr.ItemIdMap.SetAt(node.hierarchyId, this);
      } else {
        node.hierarchyId = this.projectMgr.ItemIdMap.Add(node);
      }
        
      HierarchyNode previous = null;
      for (HierarchyNode n = this.firstChild; n != null; n = n.nextSibling){
        if (n.CompareTo(node)>0) break;
        previous = n;
      }
      // insert "node" after "previous".
      if (previous != null) {
        node.nextSibling = previous.nextSibling;
        previous.nextSibling = node;
        if (previous == this.lastChild) 
          this.lastChild = node;
      } else {      
        if (this.lastChild == null) {
          this.lastChild = node;
        }
        node.nextSibling = this.firstChild;
        this.firstChild = node;
      }      
      node.parentNode = this;      
      this.OnItemAdded(this, node);  
 
    }

       
    #region IVsHierarchy methods
    public virtual void AdviseHierarchyEvents(IVsHierarchyEvents sink, out uint cookie) {
      CCITracing.TraceCall();
      cookie = this.hierarchyEventSinks.Add(sink);      
    }
    public virtual void Close() {
      CCITracing.TraceCall();
      CloseDoc(true);
      // walk tree closing any open docs that we own.
      for (HierarchyNode n = this.firstChild; n!= null; n = n.nextSibling) {
        n.Close();
      }
    }

    public virtual void GetCanonicalName(uint itemId, out string name) {
      CCITracing.TraceCall();
      HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
      name = (n != null) ? n.GetCanonicalName() : null;
    }

    public virtual int GetGuidProperty(uint itemId, int propid, out Guid guid){
      guid = Guid.Empty;
      HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
      if (n != null) {
        guid = n.GetGuidProperty(propid);
      }
      __VSHPROPID vspropId = (__VSHPROPID)propid;
      CCITracing.TraceCall(vspropId.ToString()+"="+guid.ToString());
      if (guid == Guid.Empty) {
        unchecked{return (int) OleDispatchErrors.DISP_E_MEMBERNOTFOUND;}
      }
      return 0;
    }

    
    public virtual int GetProperty(uint itemId, int propId, out object propVal) {
      propVal = null;
      HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
      if (n != null) propVal = n.GetProperty(propId);
      if (propVal == null){
        unchecked{return (int) OleDispatchErrors.DISP_E_MEMBERNOTFOUND;}
      }
      return 0;
    }

    public virtual int GetNestedHierarchy(uint itemId, ref Guid iidHierarchyNested, out IntPtr ppHierarchyNested, out uint pItemId){
      CCITracing.TraceCall();
      // nested hierarchies are not used here.
      ppHierarchyNested = IntPtr.Zero;
      pItemId = 0;
      unchecked{return (int)0x80004001;} //E_NOTIMPL
    }

    public virtual void GetSite(out Microsoft.VisualStudio.OLE.Interop.IServiceProvider site){
      CCITracing.TraceCall();      
      throw new NotImplementedException();
    }    

    /// <summary>
    /// the canonicalName of an item is it's URL, or better phrased,
    ///  the persistence data we put into @RelPath, which is a relative URL
    ///  to the root project
    ///  
    ///  returning the itemID from this means scanning the list
    /// </summary>
    /// <param name="name"></param>
    /// <param name="itemId"></param>
    public virtual void ParseCanonicalName(string name, out uint itemId){
      CCITracing.TraceCall(); 

      // we always start at the current node and go it's children down, so 
      //  if you want to scan the whole tree, better call 
      // the root
      itemId = 0; 

      if (name == this.Url) {
        itemId = this.hierarchyId; 
        return; 
      }
      if (itemId == 0 && this.firstChild != null) {
        this.firstChild.ParseCanonicalName(name, out itemId); 
      }
      if (itemId == 0 && this.nextSibling != null) {
        this.nextSibling.ParseCanonicalName(name, out itemId); 
      }
    }

    public virtual void QueryClose(out int fCanClose){
      CCITracing.TraceCall();      
      fCanClose = 1;
    }

    public virtual int SetGuidProperty(uint itemId, int propid, ref Guid guid){  
      CCITracing.TraceCall();      
      HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
      if (n != null) {
        n.SetGuidProperty(propid, ref guid); return 0;
      }
      throw new InvalidOperationException("item not found");
    }

    public virtual int SetProperty(uint itemId, int propid, object value){      
      CCITracing.TraceCall();      
      HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
      if (n != null) {
        return n.SetProperty(propid, value);
      } else {
        unchecked{return (int) OleDispatchErrors.DISP_E_MEMBERNOTFOUND;}
      }
    }

    public virtual void SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider site){
      CCITracing.TraceCall();
      throw new NotImplementedException(); 
    }
    public virtual void UnadviseHierarchyEvents(uint cookie){
      CCITracing.TraceCall();
      this.hierarchyEventSinks.RemoveAt(cookie);
    }
    public void Unused0(){
      CCITracing.TraceCall();
      throw new NotImplementedException();
    }
    public void Unused1(){
      CCITracing.TraceCall();
      throw new NotImplementedException();
    }

    public void Unused2(){
      CCITracing.TraceCall();
      throw new NotImplementedException();
    }

    public void Unused3() {
      CCITracing.TraceCall();
      throw new NotImplementedException();
    }

    public void Unused4() {
      CCITracing.TraceCall();
      throw new NotImplementedException();
    }
    #endregion   
  
    #region IVsUIHierarchy methods
    
    public virtual int ExecCommand(uint itemId, ref Guid guidCmdGroup, uint nCmdId, uint nCmdExecOpt, IntPtr pvain, IntPtr p) {
      CCITracing.TraceCall(guidCmdGroup.ToString()+","+nCmdId.ToString());
      HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
      if (guidCmdGroup == VsConstants.guidVsUIHierarchyWindowCmds) {
        if (n != null) { 
          switch((HierarchyWindowCommands)nCmdId) {
            case HierarchyWindowCommands.RightClick:
              n.DisplayContextMenu();
              return 0;
            case HierarchyWindowCommands.DoubleClick: goto case HierarchyWindowCommands.EnterKey;
            case HierarchyWindowCommands.EnterKey:
              n.DoDefaultAction();
              return 0;
          }
        }
        unchecked{return (int) OleDocumentError.OLECMDERR_E_NOTSUPPORTED;}
      }
      return ((Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget)n).Exec(ref guidCmdGroup, nCmdId, nCmdExecOpt, pvain, p);
    }        
    
    public virtual int QueryStatusCommand(uint itemId, ref Guid guidCmdGroup, uint cCmds, OLECMD[] cmds, IntPtr pCmdText)  {
      // CCITracing.TraceCall(guidCmdGroup.ToString());
      return ((Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget)this).QueryStatus(ref guidCmdGroup, cCmds, cmds, pCmdText);
    }
    #endregion
        
    #region IVsPersistHierarchyItem2 methods
    public virtual void IsItemDirty(uint itemId, IntPtr punkDocData, out int pfDirty) {
      IVsPersistDocData pd = (IVsPersistDocData)Marshal.GetObjectForIUnknown(punkDocData);
      pd.IsDocDataDirty(out pfDirty);     
    }

    public virtual void SaveItem(VSSAVEFLAGS dwSave, string silentSaveAsName, uint itemid, IntPtr punkDocData, out int pfCancelled) {
      CCITracing.TraceCall();
      string docNew;
      pfCancelled = 0;

      if ((VSSAVEFLAGS.VSSAVE_SilentSave & dwSave) != 0) {
        IPersistFileFormat pff = (IPersistFileFormat)Marshal.GetObjectForIUnknown(punkDocData);
        this.projectMgr.UIShell.SaveDocDataToFile(VSSAVEFLAGS.VSSAVE_SilentSave, pff, silentSaveAsName, out docNew, out pfCancelled);        
      }
      else {
        IVsPersistDocData dd = (IVsPersistDocData)Marshal.GetObjectForIUnknown(punkDocData);
        dd.SaveDocData(dwSave, out docNew, out pfCancelled);
      }
      if (pfCancelled != 0 && docNew != null && docNew != silentSaveAsName) {
        // update config file with new filename?
      }
    }

    public virtual void IgnoreItemFileChanges(uint itemid, int fignore) {
      CCITracing.TraceCall();
      throw new NotImplementedException();
    }
  
    public virtual void IsItemReloadable(uint itemid, out int freloadable) {
      CCITracing.TraceCall();
      freloadable = 1;
    }

    public virtual void ReloadItem(uint itemid, uint res) {
      CCITracing.TraceCall();
      throw new NotImplementedException();
    }
    #endregion

    #region IOleCommandTarget methods

    protected virtual int ExecCommand(ref Guid guidCmdGroup, uint cmd, IntPtr pvaIn, IntPtr pvaOut) {
      if (guidCmdGroup == VsConstants.guidStandardCommandSet97) {
        switch ((VsCommands)cmd) {
          case VsCommands.SolutionCfg:
            unchecked { return (int) OleDocumentError.OLECMDERR_E_NOTSUPPORTED; }
          case VsCommands.SearchCombo:
            unchecked { return (int)OleDocumentError.OLECMDERR_E_NOTSUPPORTED; }
          case VsCommands.ViewCode: 
            OpenItem(false, false, Guid.Empty);
            return 0;
          case VsCommands.ViewForm:
            OpenItem(false, false, VsConstants.LOGVIEWID_Designer);
            return 0;
          case VsCommands.Open:         
            OpenItem(false, false);
            return 0;
          case VsCommands.OpenWith: 
            OpenItem(false, true);
            return 0;
          case VsCommands.NewFolder:
            return AddNewFolder();
          case VsCommands.AddNewItem:
            return AddItemToHierarchy(HierarchyAddType.addNewItem);
          case VsCommands.AddExistingItem:
            return AddItemToHierarchy(HierarchyAddType.addExistingItem);
        }
      } 

      if (guidCmdGroup == VsConstants.guidStandardCommandSet2K)  {
        switch ((VsCommands2K)cmd) {
          case VsCommands2K.EXCLUDEFROMPROJECT:
            Remove(false);
            return 0;
          case VsCommands2K.ADDREFERENCE:
            return AddProjectReference();
          case VsCommands2K.QUICKOBJECTSEARCH:
            return this.projectMgr.ShowObjectBrowser(this.XmlNode);
        }
      }

      if (guidCmdGroup == VsConstants.guidCciSet) {
        switch ((Commands)cmd) {
          case Commands.Compilable:
            // switch from compile to content and vice versa
            if (this.IsFile) {
              string strAction = this.xmlNode.GetAttribute("BuildAction");
              if (strAction == "Compile") {
                this.xmlNode.SetAttribute("BuildAction", "None");
                this.xmlNode.SetAttribute("SubType", "Content"); 
              }
              else {
                this.xmlNode.SetAttribute("BuildAction", "Compile");
                this.xmlNode.SetAttribute("SubType", "Code"); 
              }
            }
            break;
        }
      }

    
      if (guidCmdGroup == Guid.Empty){ 
        unchecked { return (int)OleDocumentError.OLECMDERR_E_NOTSUPPORTED; }
      } else if (guidCmdGroup == VsConstants.guidStandardCommandSet2K || guidCmdGroup == VsConstants.guidStandardCommandSet97) {
        // not supported here - delegate to vsenv.
        unchecked { return (int)OleDocumentError.OLECMDERR_E_NOTSUPPORTED; }
      } else if (guidCmdGroup == this.projectMgr.GetProjectGuid()) {        
        return 0;
      } else {
        unchecked { return (int)OleDocumentError.OLECMDERR_E_NOTSUPPORTED; }
      }
    }

    /// <summary>
    /// CommandTarget.Exec is called for most major operations if they are NOT UI based. Otherwise IVSUInode::exec is called first
    /// </summary>
    public int Exec(ref Guid guidCmdGroup, uint nCmdId, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
      if (guidCmdGroup == VsConstants.guidStandardCommandSet97 && nCmdId == (uint)VsCommands.SolutionCfg || nCmdId == (uint)VsCommands.SearchCombo) {
        unchecked { return (int)OleDocumentError.OLECMDERR_E_NOTSUPPORTED; }
      }
      CCITracing.TraceCall(guidCmdGroup.ToString()+","+nCmdId.ToString());
      return ExecCommand(ref guidCmdGroup, nCmdId, pvaIn, pvaOut);
    }

    protected virtual int QueryCommandStatus(ref Guid guidCmdGroup, uint cmd, out uint cmdf) {
      if (guidCmdGroup == VsConstants.guidStandardCommandSet97 && cmd == (uint)VsCommands.SolutionCfg || cmd == (uint)VsCommands.SearchCombo) {
        cmdf = (uint)OleDocumentError.OLECMDERR_E_NOTSUPPORTED;
        return 0;
      }
      cmdf = 0;
      uint supportedAndEnabled = (int)OLECMDF.OLECMDF_SUPPORTED | (int)OLECMDF.OLECMDF_ENABLED;

      if (guidCmdGroup == VsConstants.guidStandardCommandSet97) {
        switch ((VsCommands)cmd) {
          case VsCommands.Cut: goto case VsCommands.BuildSln;
          case VsCommands.Copy: goto case VsCommands.BuildSln;
          case VsCommands.Paste: goto case VsCommands.BuildSln;
          case VsCommands.Rename: goto case VsCommands.BuildSln;
          case VsCommands.ViewCode: goto case VsCommands.BuildSln;
          case VsCommands.Exit: goto case VsCommands.BuildSln;
          case VsCommands.ProjectSettings: goto case VsCommands.BuildSln;
          case VsCommands.Start: goto case VsCommands.BuildSln;
          case VsCommands.Restart: goto case VsCommands.BuildSln;
          case VsCommands.StartNoDebug: goto case VsCommands.BuildSln;
          case VsCommands.BuildSln:
            cmdf = supportedAndEnabled;
            break;
          case VsCommands.Delete: goto case VsCommands.OpenWith;
          case VsCommands.Open: goto case VsCommands.OpenWith;
          case VsCommands.OpenWith: 
            if (this.IsFile) {
              cmdf = supportedAndEnabled;
            }
            break;
          case VsCommands.ViewForm:
            if (this.hasDesigner) {
              cmdf = supportedAndEnabled;
            } else {
              unchecked { return (int)OleDocumentError.OLECMDERR_E_UNKNOWNGROUP; }
            }
            break;

          case VsCommands.CancelBuild:  
            // todo - delegate to ProjectManager so it can enable/disable
            // this command based on whether we're doing a build or not.
            cmdf = supportedAndEnabled;
            break;
          case VsCommands.NewFolder: goto case VsCommands.AddExistingItem;
          case VsCommands.AddNewItem: goto case VsCommands.AddExistingItem;
          case VsCommands.AddExistingItem:
            if (this.nodeType == HierarchyNodeType.Root || 
              this.nodeType == HierarchyNodeType.Folder) {
              cmdf = supportedAndEnabled;
            }
            break;
          case VsCommands.SetStartupProject: 
            if (this.nodeType == HierarchyNodeType.Root) {
              cmdf = supportedAndEnabled;
            }
            break;
        }
      }
      else if ( guidCmdGroup == VsConstants.guidStandardCommandSet2K) {
        switch ((VsCommands2K)cmd) {
          case VsCommands2K.ADDREFERENCE: 
            if (this.nodeType == HierarchyNodeType.RefFolder) {
              cmdf = supportedAndEnabled;
            }
            break;
          case VsCommands2K.EXCLUDEFROMPROJECT: 
            cmdf = supportedAndEnabled;
            break;
          case VsCommands2K.QUICKOBJECTSEARCH:
            if (this.nodeType == HierarchyNodeType.Reference) {
              cmdf = supportedAndEnabled;
            }
            break;
        }
      }
      else if (guidCmdGroup == VsConstants.guidCciSet) {
        switch ((Commands)cmd) {
          case Commands.Compilable:
            if (this.IsFile) {
              cmdf = (int)OLECMDF.OLECMDF_SUPPORTED;
              string strAction = this.xmlNode.GetAttribute("BuildAction");
              if (strAction == "Compile") {
                cmdf |= (int)OLECMDF.OLECMDF_ENABLED;
              }
            }
            break;
        }
      } 
      else {
        unchecked { return (int)OleDocumentError.OLECMDERR_E_UNKNOWNGROUP; }
      } 
      return 0;
    }
   
    public int QueryStatus(ref Guid guidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText) {

      if (guidCmdGroup == Guid.Empty) {
        unchecked { return (int)OleDocumentError.OLECMDERR_E_NOTSUPPORTED; }
      } 
      for (uint i = 0; i < cCmds; i++) {
        int rc = QueryCommandStatus(ref guidCmdGroup, (uint)prgCmds[i].cmdID, out prgCmds[i].cmdf);
        if (rc < 0) return rc;
      }
      return 0;      
    }
    #endregion

    #region IVsHierarchyDropDataSource2 methods
    public virtual void GetDropInfo(out uint pdwOKEffects, out Microsoft.VisualStudio.OLE.Interop.IDataObject ppDataObject,
      out IDropSource ppDropSource) {

      CCITracing.TraceCall();
      
      pdwOKEffects = (uint)DropEffect.None;
      ppDataObject = null;
      ppDropSource = null;
      dragSource = true;

      if (this.hierarchyId != VsConstants.VSITEMID_ROOT) {
        // todo - ask project if given type of object is acceptable.
        pdwOKEffects = (uint)(DropEffect.Move | DropEffect.Copy);
        ppDataObject = PackageSelectionDataObject(false);
      }
    }
    public virtual void OnDropNotify(int fDropped, uint dwEffects) {
      CCITracing.TraceCall();
      CleanupSelectionDataObject(fDropped != 0, false, dwEffects == (uint)DropEffect.Move);
      dragSource = false;
    }

    public virtual void OnBeforeDropNotify(Microsoft.VisualStudio.OLE.Interop.IDataObject o, uint dwEffect, out int fCancelDrop) {
      CCITracing.TraceCall();
      fCancelDrop = 0;
      bool dirty = false;
      foreach (HierarchyNode node in this.itemsDragged) {
        bool isDirty, isOpen, isOpenedByUs;
        uint docCookie;
        IVsPersistDocData ppIVsPersistDocData;
        node.GetDocInfo(out isOpen, out isDirty, out isOpenedByUs, out docCookie, out ppIVsPersistDocData);
        if (isDirty && isOpenedByUs) {
          dirty = true;
          break;
        }
      }
      // if there are no dirty docs we are ok to proceed
      if (!dirty) 
        return;

      // prompt to save if there are dirty docs
      string msg = UIStrings.GetString(UIStringNames.SaveModifiedDocuments);
      string caption = UIStrings.GetString(UIStringNames.SaveCaption);
      DialogResult dr = MessageBox.Show(msg, caption, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation);
      switch (dr) {
        case DialogResult.Yes:
          break;
        case DialogResult.No:
          return;
        case DialogResult.Cancel: goto default;
        default:
          fCancelDrop = 1;
          return;
      }

      foreach (HierarchyNode node in this.itemsDragged) {
        node.SaveDoc(true);
      }
    }   
    #endregion

    #region IVsHierarchyDropDataTarget methods
    public virtual void DragEnter(Microsoft.VisualStudio.OLE.Interop.IDataObject pDataObject, uint grfKeyState, uint itemid, ref uint pdwEffect) {
      CCITracing.TraceCall();
      pdwEffect = (uint)DropEffect.None;
        
      if (dragSource)
        return; 

      _ddt = QueryDropDataType(pDataObject);
      if (_ddt != DropDataType.None) {   
        pdwEffect = (uint)QueryDropEffect(_ddt, grfKeyState);
      }      
    }

    public virtual void DragLeave()  {
      CCITracing.TraceCall();
      _ddt = DropDataType.None;
    }

    public virtual void DragOver(uint grfKeyState, uint itemid, ref uint pdwEffect) {
      CCITracing.TraceCall();
      pdwEffect = (uint)QueryDropEffect((DropDataType)_ddt, grfKeyState);
    }
      
    public virtual void Drop(Microsoft.VisualStudio.OLE.Interop.IDataObject pDataObject, uint grfKeyState, uint itemid, ref uint pdwEffect) {
      CCITracing.TraceCall();
      if (pDataObject == null)
        throw new ArgumentException();
      pdwEffect = (uint)DropEffect.None;

      if (dragSource) 
        return;
  
      DropDataType ddt = DropDataType.None;
      try {        
        ProcessSelectionDataObject(pDataObject, grfKeyState, out ddt);      
        pdwEffect = (uint)QueryDropEffect(ddt, grfKeyState);
      } catch (Exception e) {
        // If it is a drop from windows and we get any kind of error we return S_FALSE and dropeffect none. This
        // prevents bogus messages from the shell from being displayed
        if (ddt == DropDataType.Shell)
          return;
        throw e;
      }            
    }
    #endregion 

    #region IVsHierarchyDeleteHandler methods
    public virtual void DeleteItem(uint delItemOp, uint itemId) {
      CCITracing.TraceCall();
      HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
      if (n != null) {
        n.Remove((delItemOp & (uint)__VSDELETEITEMOPERATION.DELITEMOP_DeleteFromStorage) != 0);
      }
    }
    public virtual void QueryDeleteItem(uint delItemOp, uint itemId, out int pfCandelete) {
      CCITracing.TraceCall();
      HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
      if (n.nodeType == HierarchyNodeType.Reference || n.nodeType == HierarchyNodeType.Root) {
        pfCandelete = (delItemOp == (uint)__VSDELETEITEMOPERATION.DELITEMOP_RemoveFromProject) ? 1 : 0; 
      }
      else if (n.nodeType == HierarchyNodeType.Folder) {
        pfCandelete = (delItemOp == (uint)__VSDELETEITEMOPERATION.DELITEMOP_DeleteFromStorage) ? 1 : 0; 
      }
      else {
        pfCandelete = 1;
      }
    } 
    #endregion

    #region IVsComponentUser methods
    /// <summary>
    /// Add Component of the IVsComponentUser interface
    /// it serves as a callback from IVsComponentSelector dialog
    /// to notify us when a component was added as a reference to the project
    /// - needs to update the persistence data here.
    /// </summary>
    public virtual void AddComponent(VSADDCOMPOPERATION dwAddCompOperation,
      uint cComponents,
      System.IntPtr[] rgpcsdComponents,
      System.IntPtr hwndDialog,
      VSADDCOMPRESULT[] pResult) {

      CCITracing.TraceCall(); 
      
      try {
      
        for (int cCount=0; cCount < cComponents; cCount++) {
          VSCOMPONENTSELECTORDATA selectorData= new VSCOMPONENTSELECTORDATA();
          IntPtr ptr = rgpcsdComponents[cCount];
          selectorData = (VSCOMPONENTSELECTORDATA) Marshal.PtrToStructure(ptr, typeof(VSCOMPONENTSELECTORDATA)); 
          CCITracing.TraceData(selectorData.bstrFile);
          this.projectMgr.AddReference(selectorData); 
        }
      }
      catch (System.Exception e) {
        CCITracing.Trace(e);
      }
      pResult[0] = VSADDCOMPRESULT.ADDCOMPRESULT_Success;
      
    }
    #endregion 


    ////////////////////////////////////////////////////////////////////////////////////////////
    //
    //  this needs to work recursively and traverse the whole tree
    //
    ////////////////////////////////////////////////////////////////////////////////////////////
    internal HierarchyNode FindChild(string file) {
      HierarchyNode result; 
      for (HierarchyNode child = this.firstChild; child != null; child = child.NextSibling) {
        if (child.Url == file) {
          return child;
        }
        result = child.FindChild(file); 
        if (result != null)
          return result; 
      }
      return null;
    }
    public virtual void RemoveChild(HierarchyNode node){
      this.projectMgr.ItemIdMap.Remove(node);      

      HierarchyNode last = null;
      for(HierarchyNode n = this.firstChild; n != null; n = n.nextSibling) {
        if (n == node) {
          if (last != null) {
            last.nextSibling = n.nextSibling;
          }
          if (n == this.lastChild) {
            if (last == this.lastChild) {
              this.lastChild = null;
            } else {
              this.lastChild = last;
            }
          }
          if (n == this.firstChild) {
            this.firstChild = n.nextSibling;
          }
          return;
        }
        last = n;
      }
      throw new InvalidOperationException("Node not found");
    }

    public HierarchyNode PreviousSibling {
      get {
        if (this.parentNode == null) return null;
        HierarchyNode prev = null;
        for (HierarchyNode child = this.parentNode.firstChild;
          child != null; child = child.nextSibling) {
          if (child == this) 
            break;
          prev = child;
        }
        return prev;
      }
    }

    public HierarchyNode FindChildByNode(XmlNode node) {
      for (HierarchyNode child = this.FirstChild; child != null; child = child.NextSibling) {
        if (child.XmlNode == node){
          return child;
        }
      }
      return null;
    }


    public virtual string GetCanonicalName() {
      return this.Url; // used for persisting properties related to this item.
    }

    static int lastTracedProperty = 0;
    object _parentHierarchy;
    uint _parentHierarchyItemId;

    public virtual object GetProperty(int propId) {
      __VSHPROPID id = (__VSHPROPID)propId;

      object result = null;
      switch (id) {
        case __VSHPROPID.VSHPROPID_Expandable:
          result = (this.firstChild != null);
          break;
        case __VSHPROPID.VSHPROPID_Caption:
          result = this.Caption;
          break;
        case __VSHPROPID.VSHPROPID_Name:
          result = this.Caption;          
          break;
        case __VSHPROPID.VSHPROPID_ExpandByDefault:
          result = false;
          break;
        case __VSHPROPID.VSHPROPID_IconHandle:
          result = GetIconHandle(false);
          break;
        case __VSHPROPID.VSHPROPID_OpenFolderIconHandle:
          result = GetIconHandle(true);
          break;
        case __VSHPROPID.VSHPROPID_NextVisibleSibling:
          goto case __VSHPROPID.VSHPROPID_NextSibling;
        case __VSHPROPID.VSHPROPID_NextSibling:
          result = (this.nextSibling != null) ? this.nextSibling.hierarchyId : VsConstants.VSITEMID_NIL;
          break;
        case __VSHPROPID.VSHPROPID_FirstChild:
          goto case __VSHPROPID.VSHPROPID_FirstVisibleChild;
        case __VSHPROPID.VSHPROPID_FirstVisibleChild:
          result = (this.firstChild != null) ? this.firstChild.hierarchyId : VsConstants.VSITEMID_NIL;
          break;
        case __VSHPROPID.VSHPROPID_Parent:
          if (this.parentNode != null) result = this.parentNode.hierarchyId;
          break;
        case __VSHPROPID.VSHPROPID_ParentHierarchyItemid:
          if (_parentHierarchy != null) result = _parentHierarchyItemId;
          break;
        case __VSHPROPID.VSHPROPID_ParentHierarchy:
          result = _parentHierarchy;
          break;
        case __VSHPROPID.VSHPROPID_Root:
          result = Marshal.GetIUnknownForObject(this.projectMgr);
          break;
        case __VSHPROPID.VSHPROPID_Expanded:
          result = this.isExpanded;  
          break;
        case __VSHPROPID.VSHPROPID_BrowseObject:
          result = new NodeProperties(this);
          break;
        case __VSHPROPID.VSHPROPID_EditLabel:
          result = GetEditLabel();
          break;
        case __VSHPROPID.VSHPROPID_SaveName:
          result = this.Url;
          break;
        case __VSHPROPID.VSHPROPID_ItemDocCookie:
          if (this.docCookie != 0) return this.docCookie;
          break;
      }
      if (propId != lastTracedProperty) {
        string trailer = (result == null) ? "null" : result.ToString();
        CCITracing.TraceCall(this.hierarchyId+","+id.ToString()+" = " + trailer);
        lastTracedProperty = propId; // some basic filtering here...
      }
      return result;
    }

    public virtual int SetProperty(int propid, object value) {
      
      __VSHPROPID id = (__VSHPROPID)propid;
    
      CCITracing.TraceCall(this.hierarchyId+","+id.ToString()); 
      switch (id) {
        case __VSHPROPID.VSHPROPID_Expanded:
          this.isExpanded = (bool)value;
          break;
        case __VSHPROPID.VSHPROPID_ParentHierarchy:
          _parentHierarchy = value;
          break;
        case __VSHPROPID.VSHPROPID_ParentHierarchyItemid:
          _parentHierarchyItemId = (uint)(Int32)value;
          break;
        case __VSHPROPID.VSHPROPID_EditLabel:
          return SetEditLabel((string)value); 
        default:
          CCITracing.TraceCall(" unhandled");
          break;
      }
      return 0;
    }

    public virtual Guid GetGuidProperty(int propid) {
      if (propid == (int) __VSHPROPID.VSHPROPID_TypeGuid) {
        switch (this.nodeType) {
          case HierarchyNodeType.Reference:
            return Guid.Empty;
          case HierarchyNodeType.Root:
            return Guid.Empty;
          case HierarchyNodeType.RefFolder:
            return Guid.Empty;
          case HierarchyNodeType.Folder:
            return HierarchyNode.guidItemTypePhysicalFolder; 
        }
      }
      return Guid.Empty;
    }

    public virtual void SetGuidProperty(int propid, ref Guid guid) {
      throw new NotImplementedException();
    }

    public virtual void DisplayContextMenu() {

      int menuId = VsConstants.IDM_VS_CTXT_NOCOMMANDS;

      switch (this.nodeType) {
        case HierarchyNodeType.File:
          menuId = VsConstants.IDM_VS_CTXT_ITEMNODE;
          break;
        case HierarchyNodeType.Reference:
          menuId = VsConstants.IDM_VS_CTXT_REFERENCE;
          break;
        case HierarchyNodeType.Root:
          menuId = VsConstants.IDM_VS_CTXT_PROJNODE;
          break;
        case HierarchyNodeType.RefFolder:
          menuId = VsConstants.IDM_VS_CTXT_REFERENCEROOT;
          break;
        case HierarchyNodeType.Folder:
          menuId = VsConstants.IDM_VS_CTXT_FOLDERNODE;
          break;
      }

      ShowContextMenu(menuId, VsConstants.guidSHLMainMenu);
    }

    public virtual void ShowContextMenu(int menuID, Guid groupGuid) {      

      Point pt = Cursor.Position;        
      POINTS[] pnts = new POINTS[1];
      pnts[0].x = (short)pt.X;
      pnts[0].y = (short)pt.Y;
      this.projectMgr.UIShell.ShowContextMenu(0, ref groupGuid, menuID, pnts, (Microsoft.VisualStudio.OLE.Interop.IOleCommandTarget)this);
    }

    ////////////////////////////////////////////////////
    /// <summary>
    /// Overwritten in subclasses
    /// </summary>
    ////////////////////////////////////////////////////
    public virtual void DoDefaultAction() {
      CCITracing.TraceCall(); 
    }

    public virtual void OpenItem(bool newFile, bool openWith) {
      Guid logicalView = Guid.Empty;
      OpenItem(newFile, openWith, logicalView);
    }
    public virtual void OpenItem(bool newFile, bool openWith, Guid logicalView) {
      IVsWindowFrame frame;
      OpenItem(newFile, openWith, ref logicalView, IntPtr.Zero, out frame);
    }

    public virtual void OpenItem(bool newFile, bool openWith, ref Guid logicalView, 
      IntPtr punkDocDataExisting, out IVsWindowFrame windowFrame) {

      this.ProjectMgr.OnOpenItem(this.Url);

      windowFrame = null;
      VsShell.OpenItem(this.projectMgr.Site, newFile, openWith, ref logicalView, punkDocDataExisting,
        (IVsHierarchy)this, this.hierarchyId, out windowFrame);

      if (windowFrame != null) {
        object var;
        windowFrame.GetProperty((int)__VSFPROPID.VSFPROPID_DocCookie, out var);
        this.docCookie = VsConstants.ForceCast((int)var);
      }
    }

    IVsOutputWindowPane GetOutputPane() {
      Guid sid = new Guid("533FAD11-FE7F-41EE-A381-8B67792CD692");
      IVsOutputWindow outputWindow = (IVsOutputWindow)QueryService(sid, typeof(IVsOutputWindow));
      Guid buildOutput = new Guid("df39052f-854c-4d4e-9b2e-d2500ddd09d1");
      IVsOutputWindowPane pane;
      outputWindow.CreatePane(ref buildOutput, "Build", 1, 1);
      outputWindow.GetPane(ref buildOutput, out pane); 
      return pane;
    }

    public virtual string GetEditLabel() {
      switch (this.nodeType) {
        case HierarchyNodeType.Root:
          return this.xmlNode.SelectSingleNode("//@Name").InnerText;
        case HierarchyNodeType.Folder:
          return Caption; 
      }
      // references are not editable.
      return null;
    }

    public virtual int SetEditLabel(string label) {
      switch (this.nodeType) {
        case HierarchyNodeType.Root:
          System.Xml.XmlAttribute a; 
          a = (System.Xml.XmlAttribute)this.xmlNode.SelectSingleNode("//@Name");
          a.InnerText = label;
          break;
        case HierarchyNodeType.Folder:
          // ren the folder
          // this whole thing needs some serious fallback strategy work
          string strOldDir = this.Url;
          try  {
            if (CreateDirectory(label) == true) {
              // then change the property in the xml
              if (this != this.projectMgr) {
                label = this.parentNode.xmlNode.GetAttribute("RelPath") + label;
                this.xmlNode.SetAttribute("RelPath", label+"\\");
              }
              // now walk over all children and change their rel path
              for (HierarchyNode child = this.FirstChild; child != null; child = child.NextSibling) {
                string strFileName = child.xmlNode.GetAttribute("RelPath");
                if (child.nodeType != HierarchyNodeType.Folder) {
                  strFileName = Path.GetFileName(strFileName);   
                  child.SetEditLabel(strFileName, label); 
                }
                else {
                  child.SetEditLabel(child.Caption);
                }
              }
              // if that all worked, delete the old dir
              Directory.Delete(strOldDir, true); 
            }
          }
          catch  {
            // report an error.
            throw new InvalidOperationException("Rename directory failed. Most likely, a file or folder with this name already exists on disk at this location");
          }
          break;
      }
      return 0;
    }

    //
    // to be overwritten in hierarchyitems
    //    this base implementation deals with renaming containers
    //
    public virtual int SetEditLabel(string label, string strRelPath) {
      throw new NotImplementedException();
    }

    public object QueryService(Guid sid, Type type) {
      return QueryService(sid, sid, type);
    }
    public object QueryService(Guid sid, Guid iid, Type type) {
      if (this.projectMgr.Site == null) return null;
      return this.projectMgr.Site.QueryService(sid, type);
    }



    ////////////////////////////////////////////////////////////////////////////////////////////////////////////
    //
    //  removes items from the hierarchy. Project overwrites this
    //
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public virtual void Remove(bool removeFromStorage) {
      // we have to close it no matter what otherwise VS gets itself
      // tied up in a knot.
      this.CloseDoc(false); 
      if (removeFromStorage) {   
        if (this.nodeType == HierarchyNodeType.Folder) {
          Directory.Delete(this.Url, true); 
        }
        else {
          File.SetAttributes(this.Url, FileAttributes.Normal); // make sure it's not readonly.
          File.Delete(this.Url);
        }
      }

      // if this is a folder, do remove it's children. Do this before removing the parent itself
      if (this.nodeType == HierarchyNodeType.Folder) {
        for (HierarchyNode child = this.FirstChild; child != null; child = child.NextSibling) {
          child.Remove(false);
        }
      }
      if (this.nodeType == HierarchyNodeType.Reference) {
        this.projectMgr.OnRemoveReferenceNode(this);
      }


      if (this.parentNode != null) {
        // the project node has no parentNode
        this.parentNode.RemoveChild(this);
      }
      // virtual folders do not exist in the xml document...
      if (this.xmlNode.ParentNode != null) {
        this.xmlNode.ParentNode.RemoveChild(this.xmlNode);      
      }

      OnItemDeleted();
      OnInvalidateItems(this.parentNode);      
    }


    ////////////////////////////////////////////////////////////////////////////////////////////////////////////


    public virtual object GetIconHandle(bool open) {
      switch (this.nodeType) {
        case HierarchyNodeType.File: 
          // let operating system provide icon. Otherwise we need to provide the icon ONLY for our file types
          // return this.projectMgr.ImageList[(int)ProjectManager.ImageNames.File].GetHicon();
          return null;
        case HierarchyNodeType.Root: 
          return this.projectMgr.ImageList[(int)ProjectManager.ImageNames.Project].GetHicon();
        case HierarchyNodeType.RefFolder:
          return open ? 
            this.projectMgr.ImageList[(int)ProjectManager.ImageNames.OpenReferenceFolder].GetHicon() :
            this.projectMgr.ImageList[(int)ProjectManager.ImageNames.ReferenceFolder].GetHicon();
        case HierarchyNodeType.Reference:
          return this.projectMgr.ImageList[(int)ProjectManager.ImageNames.Reference].GetHicon();
        case HierarchyNodeType.Folder:
          return open ? 
            this.projectMgr.ImageList[(int)ProjectManager.ImageNames.OpenFolder].GetHicon() :
            this.projectMgr.ImageList[(int)ProjectManager.ImageNames.Folder].GetHicon();
      } 
      return null;
    }

    /// <summary>
    /// handles the add item and add new item cmdIds. Does so by invoking the system VS dialog, which calls back on the 
    /// project's AddItem method
    /// </summary>
    /// <param name="addType"></param>
    /// <returns></returns>
    public virtual int AddItemToHierarchy(HierarchyAddType addType) {
      CCITracing.TraceCall(); 
      IVsAddProjectItemDlg addItemDialog;

      string  strFilter ="";
      int     iDontShowAgain;
      uint    uiFlags;
      IVsProject3 project = (IVsProject3) this.projectMgr; 

      string  strBrowseLocations = Path.GetDirectoryName(new Uri(this.projectMgr.projFile.BaseURI).LocalPath); 

      System.Guid projectGuid = this.projectMgr.GetProjectGuid(); 

      addItemDialog = (IVsAddProjectItemDlg) this.QueryService(VsConstants.SID_SVsAddProjectItemDlg, typeof(IVsAddProjectItemDlg));
      
      if (addType == HierarchyAddType.addNewItem)
        uiFlags = (uint) (__VSADDITEMFLAGS.VSADDITEM_AddNewItems | __VSADDITEMFLAGS.VSADDITEM_SuggestTemplateName); /* | VSADDITEM_ShowLocationField */
      else
        uiFlags = (uint) (__VSADDITEMFLAGS.VSADDITEM_AddExistingItems | __VSADDITEMFLAGS.VSADDITEM_AllowMultiSelect | __VSADDITEMFLAGS.VSADDITEM_AllowStickyFilter);

      addItemDialog.AddProjectItemDlg(
        this.hierarchyId,
        ref projectGuid,
        project,
        uiFlags,
        null,
        null,
        ref strBrowseLocations,
        ref strFilter,
        out iDontShowAgain); /*&fDontShowAgain*/

      return 0;
    }

    /// <summary>
    /// Get's called to a add a new Folder to the project hierarchy. Opens the dialog to do so and
    ///   creates the physical representation
    /// </summary>
    /// <returns></returns>
    public int AddNewFolder() {
      CCITracing.TraceCall(); 

      // first generate a new folder name...
      try {
        string relFolder; 
        object dummy = null; 
        IVsProject3 project = (IVsProject3) this.projectMgr; 
        IVsUIHierarchyWindow uiWindow = this.projectMgr.GetIVsUIHierarchyWindow(VsConstants.Guid_SolutionExplorer);

        project.GenerateUniqueItemName(this.hierarchyId, "", "", out relFolder); 

        if (this != this.projectMgr) {
          // add this guys relpath to it...
          relFolder = this.xmlNode.GetAttribute("RelPath") + relFolder;
        }

        // create the project part of it, the xml in the xsproj file
        XmlElement e = this.projectMgr.AddFolderNodeToProject(relFolder);
        HierarchyNode child = new HierarchyNode(this.projectMgr, HierarchyNodeType.Folder, e);
        this.AddChild(child);

        child.CreateDirectory();
        // we need to get into label edit mode now...
        // so first select the new guy...
        uiWindow.ExpandItem(this.projectMgr, child.hierarchyId, EXPANDFLAGS.EXPF_SelectItem); 
        // them post the rename command to the shell. Folder verification and creation will
        // happen in the setlabel code...
        this.projectMgr.UIShell.PostExecCommand(ref VsConstants.guidStandardCommandSet97, (uint) VsCommands.Rename, 0, ref dummy);
      }
      catch (Exception e){
        CCITracing.Trace(e);
      }

      return 0;
    }

    /// <summary>
    /// creates the physical directory for a folder node
    /// returns false if not successfull
    /// </summary>
    /// <returns></returns>
    public virtual bool CreateDirectory() {
      bool fSucceeded = false;

      try {
        if (Directory.Exists(this.Url) == false) {
          Directory.CreateDirectory(this.Url);
          fSucceeded = true;
        }
      }
      catch (System.Exception e) {
        CCITracing.Trace(e);
        fSucceeded = false;
      }
      return fSucceeded; 
    }

    /// <summary>
    /// creates a folder nodes physical directory
    /// returns false if the same original name was passed in, or no dir created
    /// </summary>
    /// <param name="strNewName"></param>
    /// <returns></returns>
    public virtual bool CreateDirectory(string strNewName) {
      bool fSucceeded = false;

      try {
        // on a new dir && enter, we get called with the same name
        Uri uri = new Uri(new Uri(this.parentNode.Url), strNewName, true);
        string strNewDir = uri.LocalPath; //??? 
        string oldDir = this.Url;
        char [] dummy = new char[1];
        dummy[0] = '\\'; 
        oldDir = oldDir.TrimEnd(dummy); 

        if (strNewDir.ToLower() != oldDir.ToLower()) {
          if (Directory.Exists(strNewDir)) {
            throw new InvalidOperationException("Directory already exists");
          }
          Directory.CreateDirectory(strNewDir);
          fSucceeded = true; 
        }
      }
      catch (System.Exception e) {
        CCITracing.Trace(e);
        throw e;
      }
      return fSucceeded; 
    }


    /// <summary>
    /// Get's called to add a project reference. Uses the IVsComponentSelectorDialog to do so. 
    /// that one calls back on IVsComponentUser.AddComponent to tell us about changes to the store
    /// </summary>
    /// <returns>0 to indicate we handled the command</returns>
    public virtual int AddProjectReference() {
      CCITracing.TraceCall(); 

      IVsComponentSelectorDlg componentDialog;
      Guid guidEmpty = Guid.Empty; 
      VSCOMPONENTSELECTORTABINIT[] tabInit = new VSCOMPONENTSELECTORTABINIT[1]; 
      string  strBrowseLocations = Path.GetDirectoryName(new Uri(this.projectMgr.projFile.BaseURI).LocalPath); 

      tabInit[0].dwSize= 48; 
      tabInit[0].guidTab = guidEmpty; 
      tabInit[0].varTabInitInfo = 0;

      componentDialog = (IVsComponentSelectorDlg) this.QueryService(VsConstants.SID_SVsComponentSelectorDlg, typeof(IVsComponentSelectorDlg));

      try {

        // call the container to open the add reference dialog.
        componentDialog.ComponentSelectorDlg((System.UInt32) (__VSCOMPSELFLAGS.VSCOMSEL_MultiSelectMode | __VSCOMPSELFLAGS.VSCOMSEL_IgnoreMachineName), 
          (IVsComponentUser) this,
          "Add Reference",
          "",
          ref guidEmpty,ref guidEmpty,
          "",
          0,
          tabInit,
          "*.dll", 
          ref strBrowseLocations); 
      }
      catch (System.Exception e) {
        CCITracing.Trace(e); 
      }

      unchecked { return (int)0; }
    }


    // File node properties.
    void GetDocInfo(out bool pfOpen,     // true if the doc is opened
      out bool pfDirty,    // true if the doc is dirty
      out bool pfOpenByUs, // true if opened by our project
      out uint pVsDocCookie,
      out IVsPersistDocData ppIVsPersistDocData) {// VSDOCCOOKIE if open

      pfOpen = pfDirty = pfOpenByUs = false;
      pVsDocCookie = VsConstants.VSDOCCOOKIE_NIL;

      IVsHierarchy  srpIVsHierarchy;
      uint vsitemid       = VsConstants.VSITEMID_NIL;
      
      GetRDTDocumentInfo(this.Url, out srpIVsHierarchy, out vsitemid, out ppIVsPersistDocData, out pVsDocCookie);

      if (srpIVsHierarchy == null || pVsDocCookie == VsConstants.VSDOCCOOKIE_NIL)
        return;

      pfOpen = true;

      // check if the doc is opened by another project
      if ((IVsHierarchy)this == srpIVsHierarchy || (IVsHierarchy)this.projectMgr == srpIVsHierarchy) {
        pfOpenByUs = true;
      }

      if (ppIVsPersistDocData != null) {
        int pf;
        ppIVsPersistDocData.IsDocDataDirty(out pf);
        pfDirty = (pf != 0);
      }

    }

    void GetRDTDocumentInfo(string pszDocumentName, out IVsHierarchy ppIVsHierarchy,
      out uint pitemid, out IVsPersistDocData ppIVsPersistDocData,
      out uint pVsDocCookie) {

      ppIVsHierarchy = null;
      pitemid = VsConstants.VSITEMID_NIL;
      ppIVsPersistDocData = null;
      pVsDocCookie = VsConstants.VSDOCCOOKIE_NIL;
      if (pszDocumentName == null || pszDocumentName == "")
        return;

      // Get the document info.
      IVsRunningDocumentTable pRDT = (IVsRunningDocumentTable)this.QueryService(VsConstants.SID_SVsRunningDocumentTable, typeof(IVsRunningDocumentTable));
      if (pRDT == null) return;

      IntPtr docData;
      pRDT.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock,
        pszDocumentName, out ppIVsHierarchy, out pitemid,
        out docData, out pVsDocCookie);

    
      if (docData != IntPtr.Zero) {
        try  {
          // if interface is not supported, return null
          ppIVsPersistDocData = (IVsPersistDocData)Marshal.GetObjectForIUnknown(docData);          
        }
        catch  {
        };
      }
    }


    public void CloseDoc(bool save) {
      bool isDirty, isOpen, isOpenedByUs;
      uint docCookie;
      IVsPersistDocData ppIVsPersistDocData;
      this.GetDocInfo(out isOpen, out isDirty, out isOpenedByUs, out docCookie, out ppIVsPersistDocData);
      if (isDirty && save && ppIVsPersistDocData != null) {
        string name;
        int cancelled;
        ppIVsPersistDocData.SaveDocData(VSSAVEFLAGS.VSSAVE_SilentSave, out name, out cancelled);
      }
      if (isOpenedByUs) {
        IVsUIShellOpenDocument shell = (IVsUIShellOpenDocument)QueryService(VsConstants.SID_VsUIShellOpenDocument, typeof(IVsUIShellOpenDocument));
        Guid logicalView = Guid.Empty;
        uint grfIDO = 0;
        IVsUIHierarchy pHierOpen;
        uint itemIdOpen;
        IVsWindowFrame windowFrame;
        int fOpen;
        shell.IsDocumentOpen((IVsUIHierarchy)this, this.hierarchyId, this.Url,
          ref logicalView, grfIDO, out pHierOpen, out itemIdOpen, out windowFrame, out fOpen);

        if (windowFrame != null) {
          windowFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
          docCookie = 0;
        }
      }
    }

    public void SaveDoc(bool saveIfDirty) {
      bool isDirty, isOpen, isOpenedByUs;
      uint docCookie;
      IVsPersistDocData ppIVsPersistDocData;
      this.GetDocInfo(out isOpen, out isDirty, out isOpenedByUs, out docCookie, out ppIVsPersistDocData);
      if (isDirty && saveIfDirty && ppIVsPersistDocData != null) {
        string name;
        int cancelled;
        ppIVsPersistDocData.SaveDocData(VSSAVEFLAGS.VSSAVE_SilentSave, out name, out cancelled);
      }
    }

    // ================= Drag/Drop/Cut/Copy/Paste ========================
    // Ported from HeirUtil7\PrjHeir.cpp
    void ProcessSelectionDataObject(Microsoft.VisualStudio.OLE.Interop.IDataObject pDataObject, uint grfKeyState, out DropDataType pddt) {

      pddt = DropDataType.None;               

      // try HDROP
      FORMATETC fmtetc = DragDropHelper.CreateFormatEtc(CF_HDROP);       

      bool hasData = false;
      try {
        DragDropHelper.QueryGetData(pDataObject, ref fmtetc);
        hasData= true;
      } catch (Exception ) {
      }

      if (hasData) {
        try {
          STGMEDIUM stgmedium = DragDropHelper.GetData(pDataObject, ref fmtetc);
          if (stgmedium.tymed == (uint)TYMED.TYMED_HGLOBAL) {
            IntPtr hDropInfo = stgmedium.unionmember;
            if (hDropInfo != IntPtr.Zero) {
              pddt = DropDataType.Shell;
              try {
                uint numFiles = DragQueryFile(hDropInfo, 0xFFFFFFFF, null, 0);
                char[] szMoniker = new char[MAX_PATH+1];
                IVsProject vsProj = (IVsProject)this.projectMgr;                  
                for (uint iFile = 0; iFile < numFiles; iFile++) {
                  uint len = DragQueryFile(hDropInfo, iFile, szMoniker, MAX_PATH);
                  string filename = new String(szMoniker, 0, (int)len);
                  // Is full path returned
                  if (File.Exists(filename)) {
                    VSADDRESULT[] vsaddresult = new VSADDRESULT[1];
                    vsaddresult[0] = VSADDRESULT.ADDRESULT_Failure;
                    string[] files = new String[1] {filename};    
                    // TODO: support dropping into subfolders...
                    vsProj.AddItem(this.projectMgr.hierarchyId, VSADDITEMOPERATION.VSADDITEMOP_OPENFILE, 
                      null, 1, files, IntPtr.Zero, vsaddresult);
                  }
                }
                Marshal.FreeHGlobal(hDropInfo);
              } catch (Exception e) {
                Marshal.FreeHGlobal(hDropInfo);
                throw e;
              }
            }
          }
          return;
        } catch (Exception) {
          hasData = false;
        }        
      }

      if (DragDropHelper.AttemptVsFormat(this, DragDropHelper.CF_VSREFPROJECTITEMS, pDataObject, grfKeyState, out pddt))
        return;

      if (DragDropHelper.AttemptVsFormat(this, DragDropHelper.CF_VSSTGPROJECTITEMS, pDataObject, grfKeyState, out pddt))
        return;
    }


    static Guid GUID_ItemType_PhysicalFile =  new Guid( 0x6bb5f8ee, 0x4483, 0x11d3, 0x8b, 0xcf, 0x0, 0xc0, 0x4f, 0x8e, 0xc2, 0x8c );
    static Guid GUID_ItemType_PhysicalFolder = new Guid( 0x6bb5f8ef, 0x4483, 0x11d3, 0x8b, 0xcf, 0x0, 0xc0, 0x4f, 0x8e, 0xc2, 0x8c );
    static Guid GUID_ItemType_VirtualFolder = new Guid( 0x6bb5f8f0, 0x4483, 0x11d3, 0x8b, 0xcf, 0x0, 0xc0, 0x4f, 0x8e, 0xc2, 0x8c );
    static Guid GUID_ItemType_SubProject = new Guid( 0xEA6618E8, 0x6E24, 0x4528, 0x94, 0xBE, 0x68, 0x89, 0xFE, 0x16, 0x48, 0x5C );

    // This is for moving files from one part of our project to another.
    public void AddFiles(string[] rgSrcFiles) {
      if (rgSrcFiles == null || rgSrcFiles.Length == 0)
        return;
      IVsSolution srpIVsSolution = (IVsSolution)this.QueryService(VsConstants.SID_SVsSolution, typeof(IVsSolution));
      if (srpIVsSolution == null) 
        return;

      IVsProject ourProj = (IVsProject)this.projectMgr;                  
                
      foreach(string file in rgSrcFiles) {
        uint itemidLoc;
        IVsHierarchy srpIVsHierarchy;
        string str;
        VSUPDATEPROJREFREASON[] reason = new VSUPDATEPROJREFREASON[1];
        srpIVsSolution.GetItemOfProjref(file, out srpIVsHierarchy, out itemidLoc, out str, reason);
        if (srpIVsHierarchy == null) {
          throw new InvalidOperationException();//E_UNEXPECTED;
        }

        IVsProject srpIVsProject = (IVsProject)srpIVsHierarchy;
        if (srpIVsProject == null) {          
          continue;
        }

        string cbstrMoniker;
        srpIVsProject.GetMkDocument(itemidLoc, out cbstrMoniker);
        if (File.Exists(cbstrMoniker)) {        
          string[] files = new String[1] {cbstrMoniker};                  
          VSADDRESULT[] vsaddresult = new VSADDRESULT[1];
          vsaddresult[0] = VSADDRESULT.ADDRESULT_Failure;
          // bugbug: support dropping into subfolder.
          ourProj.AddItem(this.projectMgr.hierarchyId, VSADDITEMOPERATION.VSADDITEMOP_OPENFILE, 
            null, 1, files, IntPtr.Zero, vsaddresult);
          if (vsaddresult[0] == VSADDRESULT.ADDRESULT_Cancel) {
            break;
          }
        }
      }
    }   

    MyDataObject PackageSelectionDataObject(bool cutHighlightItems){
      CleanupSelectionDataObject(false, false, false);
      IVsUIHierarchyWindow w = this.projectMgr.GetIVsUIHierarchyWindow(VsConstants.Guid_SolutionExplorer);
      IVsSolution solution = (IVsSolution)this.QueryService(VsConstants.SID_SVsSolution, typeof(IVsSolution));
      IVsMonitorSelection ms = (IVsMonitorSelection)this.QueryService(VsConstants.SID_SVsShellMonitorSelection, typeof(IVsMonitorSelection));
      IntPtr psel;
      IVsMultiItemSelect itemSelect;
      IntPtr psc;
      uint vsitemid;
      StringBuilder sb = new StringBuilder();
      ms.GetCurrentSelection(out psel, out vsitemid, out itemSelect, out psc);

      IVsHierarchy sel = (IVsHierarchy)Marshal.GetTypedObjectForIUnknown(psel, typeof(IVsHierarchy));
      ISelectionContainer sc = (ISelectionContainer)Marshal.GetTypedObjectForIUnknown(psc, typeof(ISelectionContainer));

      const uint GSI_fOmitHierPtrs = 0x00000001;

      if  ( ( sel != (IVsHierarchy)this)  ||
        ( vsitemid == VsConstants.VSITEMID_ROOT) || 
        ( vsitemid == VsConstants.VSITEMID_NIL ) )
        throw new InvalidOperationException();
    
      if ( (vsitemid == VsConstants.VSITEMID_SELECTION) && (itemSelect != null) ) {
        int singleHierarchy;
        uint pcItems;
        itemSelect.GetSelectionInfo(out pcItems, out singleHierarchy);
        if (singleHierarchy != 0) // "!BOOL" == "!= 0" ?
          throw new InvalidOperationException();

        this.itemsDragged = new ArrayList();
        VSITEMSELECTION[] items = new VSITEMSELECTION[pcItems];
        itemSelect.GetSelectedItems(GSI_fOmitHierPtrs, pcItems, items);
        for (uint i = 0; i < pcItems; i++) {
          if (items[i].itemid == VsConstants.VSITEMID_ROOT) {
            this.itemsDragged.Clear();// abort
            break;
          }
          this.itemsDragged.Add(items[i].pHier);
          string projref;
          solution.GetProjrefOfItem((IVsHierarchy)this, items[i].itemid, out projref);
          if ( (projref == null) || (projref.Length==0) ) {
            this.itemsDragged.Clear(); // abort
            break;
          }
          sb.Append(projref);
          sb.Append('\0'); // separated by nulls.
        }
      } else if (vsitemid != VsConstants.VSITEMID_ROOT) {
        this.itemsDragged = new ArrayList();
        this.itemsDragged.Add(this.projectMgr.NodeFromItemId(vsitemid));
    
        string projref;
        solution.GetProjrefOfItem((IVsHierarchy)this, vsitemid, out projref);
        sb.Append(projref);
      }
      if (sb.ToString() == "" || this.itemsDragged.Count == 0) 
        return null;

      sb.Append('\0'); // double null at end.

      _DROPFILES df = new _DROPFILES();
      int dwSize = Marshal.SizeOf(df);
      Int16 wideChar = 0;
      int dwChar = Marshal.SizeOf(wideChar);
      IntPtr ptr = Marshal.AllocHGlobal(dwSize+((sb.Length+1)*dwChar));
      df.pFiles = dwSize;
      df.fWide = 1;
      IntPtr data = MyDataObject.GlobalLock(ptr);
      Marshal.StructureToPtr(df, data, false);
      IntPtr strData = new IntPtr((long)data + dwSize);
      MyDataObject.CopyStringToHGlobal(sb.ToString(), strData);
      MyDataObject.GlobalUnLock(data);

      MyDataObject dobj = new MyDataObject();

      FORMATETC fmt = DragDropHelper.CreateFormatEtc(); 

      dobj.SetData(fmt, ptr);
      if (cutHighlightItems) {
        bool first = true;
        foreach (HierarchyNode node in this.itemsDragged) {
          w.ExpandItem((IVsUIHierarchy)this.projectMgr, node.hierarchyId, first ? EXPANDFLAGS.EXPF_CutHighlightItem : EXPANDFLAGS.EXPF_AddCutHighlightItem);
          first = false;
        }
      }
      return dobj;
    }

    
    [DllImport("shell32.dll", EntryPoint="DragQueryFileW",  
       SetLastError=true, CharSet=CharSet.Unicode, ExactSpelling=true,
       CallingConvention=CallingConvention.StdCall)]
    static extern uint DragQueryFile(IntPtr hDrop, uint iFile, char[] lpszFile, uint cch);



    void CleanupSelectionDataObject(bool dropped, bool cut, bool moved) {
      if (this.itemsDragged == null || this.itemsDragged.Count == 0)
        return;
      IVsUIHierarchyWindow w = this.projectMgr.GetIVsUIHierarchyWindow(VsConstants.Guid_SolutionExplorer);
      foreach (HierarchyNode node in this.itemsDragged) {
        if ( (moved && dropped) || cut ) {
          // do not close it if the doc is dirty or we do not own it
          bool isDirty, isOpen, isOpenedByUs;
          uint docCookie;
          IVsPersistDocData ppIVsPersistDocData;
          node.GetDocInfo(out isOpen, out isDirty, out isOpenedByUs, out docCookie, out ppIVsPersistDocData);
          if (isDirty || (isOpen && isOpenedByUs) )
            continue;

          // close it if opened
          if (isOpen) {
            node.CloseDoc(false);
          }

          node.Remove(false);
        } else if (w != null ) {
          try {
            w.ExpandItem((IVsUIHierarchy)this, node.hierarchyId, EXPANDFLAGS.EXPF_UnCutHighlightItem);
          } catch (Exception) {
          }
        }
      } 
    }

    static ushort CF_HDROP = 15; // winuser.h

    DropDataType QueryDropDataType(Microsoft.VisualStudio.OLE.Interop.IDataObject  pDataObject) {

      DragDropHelper.RegisterClipboardFormats();

      // known formats include File Drops (as from WindowsExplorer),
      // VSProject Reference Items and VSProject Storage Items.
      FORMATETC fmt = DragDropHelper.CreateFormatEtc(CF_HDROP); 

      try {
        DragDropHelper.QueryGetData(pDataObject, ref fmt);
        return DropDataType.Shell;
      } catch { }    
      fmt.cfFormat = DragDropHelper.CF_VSREFPROJECTITEMS;
      try { 
        DragDropHelper.QueryGetData(pDataObject, ref fmt);
        return DropDataType.Shell;
      } catch { }
    
      fmt.cfFormat = DragDropHelper.CF_VSSTGPROJECTITEMS;   
      try {
        DragDropHelper.QueryGetData(pDataObject, ref fmt);
        // Data is from a Ref-based project.
        return DropDataType.VsRef;
      }  catch { }

      return DropDataType.None;
    }

    const uint MK_CONTROL  = 0x0008; //winuser.h
    const uint MK_SHIFT  = 0x0004;

    DropEffect QueryDropEffect(DropDataType ddt, uint grfKeyState) {

      // We are reference-based project so we should perform as follow:
      // for shell and physical items:
      //  NO MODIFIER - LINK
      //  SHIFT DRAG - NO DROP
      //  CTRL DRAG - NO DROP
      //  CTRL-SHIFT DRAG - LINK
      // for reference/link items
      //  NO MODIFIER - MOVE
      //  SHIFT DRAG - MOVE
      //  CTRL DRAG - COPY
      //  CTRL-SHIFT DRAG - LINK

      if ( (ddt != DropDataType.Shell) && (ddt != DropDataType.VsRef) && (ddt != DropDataType.VsStg) )
        return DropEffect.None;

      switch (ddt) {
        case DropDataType.Shell: goto case DropDataType.VsStg;
        case DropDataType.VsStg:

          // CTRL-SHIFT
          if((grfKeyState & MK_CONTROL) != 0
            && (grfKeyState & MK_SHIFT) != 0) {
            return DropEffect.Link;
          }
          // CTRL
          if((grfKeyState & MK_CONTROL) != 0)
            return DropEffect.None;

          // SHIFT
          if((grfKeyState & MK_SHIFT) != 0)
            return DropEffect.None;

          // no modifier
          return DropEffect.Link;
            
        case DropDataType.VsRef:
          // CTRL-SHIFT
          if((grfKeyState & MK_CONTROL)!=0 && (grfKeyState & MK_SHIFT)!=0) {
            return DropEffect.Link;
          }
          // CTRL
          if((grfKeyState & MK_CONTROL) != 0) {
            return DropEffect.Copy;
          }

          // SHIFT
          if((grfKeyState & MK_SHIFT) != 0) {
            return DropEffect.Move;
          }

          // no modifier
          return DropEffect.Move;
      }

      return DropEffect.None;
    }

    //=================== interface implementations ======================

    /*
        #region ObsoleteHierarchy Members

        // IVsHierarchy
        void ObsoleteIVSHierarchy.AdviseHierarchyEvents(IVsHierarchyEvents sink, out uint cookie) {
          CCITracing.TraceCall();
          cookie = this.hierarchyEventSinks.Add(sink);      
        }
        void ObsoleteIVSHierarchy.Close() {
          CCITracing.TraceCall();
          CloseDoc(true);
          // walk tree closing any open docs that we own.
          for (HierarchyNode n = this.firstChild; n!= null; n = n.nextSibling) {
            n.Close();
          }
        }

        void ObsoleteIVSHierarchy.GetCanonicalName(uint itemId, out string name) {
          CCITracing.TraceCall();
          HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
          name = (n != null) ? n.GetCanonicalName() : null;
        }

        int ObsoleteIVSHierarchy.GetProperty(uint itemId, int propId, out object propVal) {
          propVal = null;
          HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
          if (n != null) propVal = n.GetProperty(propId);
          if (propVal == null){
            unchecked{return (int) OleStandardErrors.DISP_E_MEMBERNOTFOUND;}
          }
            return 0;
        }

        int ObsoleteIVSHierarchy.GetNestedHierarchy(uint itemId, ref Guid iidHierarchyNested, out IntPtr ppHierarchyNested, out uint pItemId){
          CCITracing.TraceCall();
          // nested hierarchies are not used here.
          ppHierarchyNested = IntPtr.Zero;
          pItemId = 0;
          unchecked{return (int)0x80004001;} //E_NOTIMPL
        }

        void ObsoleteIVSHierarchy.GetSite(out Microsoft.VisualStudio.OLE.Interop.IServiceProvider site){
          CCITracing.TraceCall();      
          throw new NotImplementedException();
        }


        /// <summary>
        /// the canonicalName of an item is it's URL, or better phrased,
        ///  the persistence data we put into @RelPath, which is a relative URL
        ///  to the root project
        ///  
        ///  returning the itemID from this means scanning the list
        /// </summary>
        /// <param name="name"></param>
        /// <param name="itemId"></param>
        void ObsoleteIVSHierarchy.ParseCanonicalName(string name, out uint itemId){
          CCITracing.TraceCall(); 

          // we always start at the current node and go it's children down, so 
          //  if you want to scan the whole tree, better call 
          // the root
          itemId = 0; 

          if (name == this.Url) {
            itemId = this.hierarchyId; 
            return; 
          }
          if (itemId == 0 && this.firstChild != null) {
            this.firstChild.ParseCanonicalName(name, out itemId); 
          }
          if (itemId == 0 && this.nextSibling != null) {
            this.nextSibling.ParseCanonicalName(name, out itemId); 
          }
        }


        void ObsoleteIVSHierarchy.QueryClose(out int fCanClose){
          CCITracing.TraceCall();      
          fCanClose = 1;
        }

        int ObsoleteIVSHierarchy.GetGuidProperty(uint itemId, int propid, out Guid guid){
          guid = Guid.Empty;
          HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
          if (n != null) {
            guid = n.GetGuidProperty(propid);
          }
          __VSHPROPID vspropId = (__VSHPROPID)propid;
          CCITracing.TraceCall(vspropId.ToString()+"="+guid.ToString());
          if (guid == Guid.Empty) 
          {
            unchecked{return (int) OleStandardErrors.DISP_E_MEMBERNOTFOUND;}
          }
          return 0;
        }
        int ObsoleteIVSHierarchy.SetGuidProperty(uint itemId, int propid, ref Guid guid){  
          CCITracing.TraceCall();      
          HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
          if (n != null) {
            n.SetGuidProperty(propid, ref guid); return 0;
          }
          throw new InvalidOperationException("item not found");
        }

        int ObsoleteIVSHierarchy.SetProperty(uint itemId, int propid, object value){      
          CCITracing.TraceCall();      
          HierarchyNode n = this.projectMgr.NodeFromItemId(itemId);
          if (n != null) {
            n.SetProperty(propid, value);
            return 0;
          } else {
            unchecked{return (int) OleStandardErrors.DISP_E_MEMBERNOTFOUND;}
          }
        }

        void ObsoleteIVSHierarchy.SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider site){
          CCITracing.TraceCall();
          throw new NotImplementedException(); 
        }
        void ObsoleteIVSHierarchy.UnadviseHierarchyEvents(uint cookie){
          CCITracing.TraceCall();
          this.hierarchyEventSinks.RemoveAt(cookie);
        }
        void ObsoleteIVSHierarchy.Unused0(){
          CCITracing.TraceCall();
          throw new NotImplementedException();
        }
        void ObsoleteIVSHierarchy.Unused1(){
          CCITracing.TraceCall();
          throw new NotImplementedException();
        }

        void ObsoleteIVSHierarchy.Unused2(){
          CCITracing.TraceCall();
          throw new NotImplementedException();
        }

        void ObsoleteIVSHierarchy.Unused3() {
          CCITracing.TraceCall();
          throw new NotImplementedException();
        }

        void ObsoleteIVSHierarchy.Unused4() {
          CCITracing.TraceCall();
          throw new NotImplementedException();
        }



        #endregion 
    */
  } // end of class

} // end of namespace




