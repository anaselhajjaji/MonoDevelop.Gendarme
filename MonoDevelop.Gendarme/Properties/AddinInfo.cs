using System;
using Mono.Addins;
using Mono.Addins.Description;

[assembly:Addin (
    "MonoDevelop.Gendarme", 
    Namespace = "MonoDevelop.Gendarme",
	Version = "1.0"
)]

[assembly:AddinName ("MonoDevelop.Gendarme")]
[assembly:AddinCategory ("MonoDevelop.Gendarme")]
[assembly:AddinDescription ("MonoDevelop.Gendarme")]
[assembly:AddinAuthor ("Anas EL HAJJAJI")]

[assembly:AddinDependency ("::MonoDevelop.Core", MonoDevelop.BuildInfo.Version)]
[assembly:AddinDependency ("::MonoDevelop.Ide", MonoDevelop.BuildInfo.Version)]
