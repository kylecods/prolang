// The compiler holds process-wide mutable state — most visibly BuiltInModule's registry of
// .NET namespaces, which ProLangCompilation.Create mutates during import resolution. Running
// test collections concurrently would let one test's imports leak into another's binding.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
