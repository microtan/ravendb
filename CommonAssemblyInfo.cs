﻿using System;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyCompany("Hibernating Rhinos")]
[assembly: AssemblyCopyright("© Hibernating Rhinos 2004 - 2.13. All rights reserved.")]
[assembly: AssemblyTrademark("")]
[assembly: ComVisible(false)]

[assembly: AssemblyTitle("RavenDB")]
[assembly: AssemblyVersion("3.0.0")]
[assembly: AssemblyFileVersion("3.0.13.0")]
[assembly: AssemblyInformationalVersion("3.0.0 / {commit} / {stable}")]
[assembly: AssemblyProduct("RavenDB")]
[assembly: AssemblyDescription("RavenDB is a second generation LINQ enabled document database for .NET")]

[assembly: CLSCompliant(false)]
[assembly: SuppressIldasm()]

#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
#endif

[assembly: AssemblyDelaySign(false)]
[assembly: NeutralResourcesLanguage("en-US")]
