//-----------------------------------------------------------------------------
//
// Copyright (C) Microsoft Corporation.  All Rights Reserved.
//
//-----------------------------------------------------------------------------
namespace Microsoft.VisualStudio.CodeTools
{
  using System;
  using System.Runtime.InteropServices;
  using Microsoft.VisualStudio.Shell.Interop;
  
  // The main package class
  #region Attributes
#if DEV11
  [Microsoft.VisualStudio.Shell.DefaultRegistryRoot("Software\\Microsoft\\VisualStudio\\11.0")
  ,Microsoft.VisualStudio.Shell.ProvideLoadKey("Standard", "1.0", "Microsoft.VisualStudio.CodeTools.PropertyPage", "Microsoft", 1)
  ,Guid("072DD0C6-AE1E-4ed6-A0BF-B99D5B68D29E")]
#elif DEV10
  [Microsoft.VisualStudio.Shell.DefaultRegistryRoot("Software\\Microsoft\\VisualStudio\\10.0")
  ,Microsoft.VisualStudio.Shell.ProvideLoadKey("Standard", "1.0", "Microsoft.VisualStudio.CodeTools.PropertyPage", "Microsoft", 1)
  ,Guid("072DD0C6-AE1E-4ed6-A0BF-B99D5B68D29E")]
#else
  [Microsoft.VisualStudio.Shell.DefaultRegistryRoot("Software\\Microsoft\\VisualStudio\\9.0")
  ,Microsoft.VisualStudio.Shell.ProvideLoadKey("Standard", "1.0", "Microsoft.VisualStudio.CodeTools.PropertyPage", "Microsoft", 1)
  ,Guid("072DD0C6-AE1E-4ed6-A0BF-B99D5B68D29E")]
#endif  
  #endregion
  public sealed class PropertyPagePackage : Microsoft.VisualStudio.Shell.Package
  {
    #region Construction
    private PropertyPanes propPanes;

    protected override void Initialize()
    {
      base.Initialize();
      // Initialize common functionality
      Common.Initialize(this);
      propPanes = new PropertyPanes();
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing) {
        propPanes.Release();
        Common.Release();
        GC.SuppressFinalize(this);
      }
      base.Dispose(disposing);
    }
    #endregion
  }
}
