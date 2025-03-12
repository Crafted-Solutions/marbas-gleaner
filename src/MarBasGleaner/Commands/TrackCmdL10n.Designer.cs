﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace CraftedSolutions.MarBasGleaner.Commands {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class TrackCmdL10n {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal TrackCmdL10n() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("CraftedSolutions.MarBasGleaner.Commands.TrackCmdL10n", typeof(TrackCmdL10n).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Sets up tracking of MarBas grains in local directory.
        /// </summary>
        internal static string CmdDesc {
            get {
                return ResourceManager.GetString("CmdDesc", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Anchor grain {0:D} doesn&apos;t seem to be exportable.
        /// </summary>
        internal static string ErrorAnchorImport {
            get {
                return ResourceManager.GetString("ErrorAnchorImport", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Anchor grain &apos;{0}&apos; could not be loaded.
        /// </summary>
        internal static string ErrorAnchorLoad {
            get {
                return ResourceManager.GetString("ErrorAnchorLoad", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;{0}&apos; is not a valid grain path nor a UID.
        /// </summary>
        internal static string ErrorIdOrPath {
            get {
                return ResourceManager.GetString("ErrorIdOrPath", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Error initializing snapshot directory &apos;{0}&apos;: {1}.
        /// </summary>
        internal static string ErrorInitializationException {
            get {
                return ResourceManager.GetString("ErrorInitializationException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;{0}&apos; already contains a tracking snapshot.
        /// </summary>
        internal static string ErrorSnapshotExists {
            get {
                return ResourceManager.GetString("ErrorSnapshotExists", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;{0}&apos; already contains a tracking snapshot which is unconnected, execute &apos;connect&apos; command.
        /// </summary>
        internal static string ErrorSnapshotState {
            get {
                return ResourceManager.GetString("ErrorSnapshotState", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;{0}&apos; is not a recognizable absolute URI.
        /// </summary>
        internal static string ErrorURL {
            get {
                return ResourceManager.GetString("ErrorURL", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Identifier (GUID) or path (i.e. &apos;/marbas/&lt;path&gt;&apos;) of the top grain to track.
        /// </summary>
        internal static string IdArgDesc {
            get {
                return ResourceManager.GetString("IdArgDesc", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to List of grain IDs to ignore.
        /// </summary>
        internal static string IgnoreGrainsOptionDesc {
            get {
                return ResourceManager.GetString("IgnoreGrainsOptionDesc", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to List of type names of grains to ignore.
        /// </summary>
        internal static string IgnoreTypeNamesOptionDesc {
            get {
                return ResourceManager.GetString("IgnoreTypeNamesOptionDesc", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to List of type IDs of grains to ignore.
        /// </summary>
        internal static string IgnoreTypesOptionDesc {
            get {
                return ResourceManager.GetString("IgnoreTypesOptionDesc", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Setting up tracking of {0} in {1}.
        /// </summary>
        internal static string MsgCmdStart {
            get {
                return ResourceManager.GetString("MsgCmdStart", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Snapshot of {0} created successfully.
        /// </summary>
        internal static string MsgCmdSuccess {
            get {
                return ResourceManager.GetString("MsgCmdSuccess", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Tracking scope.
        /// </summary>
        internal static string ScopeOptionDesc {
            get {
                return ResourceManager.GetString("ScopeOptionDesc", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Source control system used for snapshots (currently only {0} is supported).
        /// </summary>
        internal static string ScsOptionDesc {
            get {
                return ResourceManager.GetString("ScsOptionDesc", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Pulling grain {0:D} ({1}).
        /// </summary>
        internal static string StatusGrainPull {
            get {
                return ResourceManager.GetString("StatusGrainPull", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Anchor grain {0:D} is in the ignore list.
        /// </summary>
        internal static string WarnAnchorIgnored {
            get {
                return ResourceManager.GetString("WarnAnchorIgnored", resourceCulture);
            }
        }
    }
}
