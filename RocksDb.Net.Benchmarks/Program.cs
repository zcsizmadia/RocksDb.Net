using BenchmarkDotNet.Running;

// Every benchmark class in this assembly, selectable by name or by the
// interactive menu. `--filter '*Callback*'` runs one suite; `--list flat`
// prints what is available.
//
// The type is partial because top-level statements generate their own Program
// class, and the switcher needs a type in this assembly to scan from.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

internal sealed partial class Program
{
}
