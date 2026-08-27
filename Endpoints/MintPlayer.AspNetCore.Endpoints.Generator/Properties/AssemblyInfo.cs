using System.Runtime.CompilerServices;

// EndpointInfo, GroupInfo, AssemblyInfo and DiagnosticDescriptors are internal.
// AssemblyInfo.GetMethodName() and EndpointInfo.GetBaseClassName() are pure string
// functions carrying real defects, and are far cheaper to test directly than through
// a full generator run.
[assembly: InternalsVisibleTo("MintPlayer.AspNetCore.Endpoints.Generator.Tests")]
