﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace MarBasGleaner.Commands {
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
    internal class GenericCmdL10n {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal GenericCmdL10n() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("MarBasGleaner.Commands.GenericCmdL10n", typeof(GenericCmdL10n).Assembly);
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
        ///   Looks up a localized string similar to Local directory containing tracking information.
        /// </summary>
        internal static string DirectoryOpionDesc {
            get {
                return ResourceManager.GetString("DirectoryOpionDesc", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to This tool requires API version {0}, while data broker reports {1}.
        /// </summary>
        internal static string ErrorAPIVersion {
            get {
                return ResourceManager.GetString("ErrorAPIVersion", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Failed querying broker API at &apos;{0}&apos;.
        /// </summary>
        internal static string ErrorBrokerConnection {
            get {
                return ResourceManager.GetString("ErrorBrokerConnection", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Connection to broker at {0} failed due to error: {1}.
        /// </summary>
        internal static string ErrorBrokerConnectionException {
            get {
                return ResourceManager.GetString("ErrorBrokerConnectionException", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Snapshot checkpoints are missing, delete {0} and execute &apos;connect&apos; command.
        /// </summary>
        internal static string ErrorCheckpointMissing {
            get {
                return ResourceManager.GetString("ErrorCheckpointMissing", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;{0}&apos; is not connected to broker, execute &apos;connect&apos; first.
        /// </summary>
        internal static string ErrorConnectedState {
            get {
                return ResourceManager.GetString("ErrorConnectedState", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;{0}&apos; contains no tracking snapshots.
        /// </summary>
        internal static string ErrorReadyState {
            get {
                return ResourceManager.GetString("ErrorReadyState", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Broker schema version {0} is incompatible with snapshot {1}.
        /// </summary>
        internal static string ErrorSchemaVersion {
            get {
                return ResourceManager.GetString("ErrorSchemaVersion", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Version of snapshot {0} is not supported (actual: {1}, expected {2}).
        /// </summary>
        internal static string ErrorSnapshotVersion {
            get {
                return ResourceManager.GetString("ErrorSnapshotVersion", resourceCulture);
            }
        }
    }
}
