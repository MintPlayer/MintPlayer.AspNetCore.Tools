namespace System.Runtime.CompilerServices;

/// <summary>
/// Stands in for an attribute the compiler emits, by living in the namespace the filter recognises.
/// </summary>
/// <remarks>
/// Declared here rather than borrowing a real one. Which scope Roslyn stamps a genuine
/// <c>NullableContextAttribute</c> onto — module, type or member — depends on where the nullable
/// context happens to be uniform, and in this test assembly it lands on the <i>module</i>, so no
/// fixture type here carries one and a test written against one would pass vacuously. Naming the rule
/// directly is the assertion that keeps working.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class InCompilerServicesNamespaceAttribute : Attribute;
