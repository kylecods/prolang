```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8246)
Unknown processor
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2
  DefaultJob : .NET 10.0.7 (10.0.726.21808), X64 RyuJIT AVX2


```
| Method                  | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------ |---------:|---------:|---------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| SimpleCompilation       | 145.9 μs | 14.17 μs | 41.78 μs | 132.0 μs |  1.00 |    0.00 |  4.0283 |      - |  25.36 KB |        1.00 |
| StringProcessingProgram | 269.4 μs |  3.96 μs |  3.51 μs | 268.3 μs |  2.80 |    0.48 | 21.9727 | 2.9297 |  135.2 KB |        5.33 |
| StructHeavyProgram      | 201.8 μs |  2.39 μs |  2.11 μs | 201.4 μs |  2.10 |    0.36 |  9.7656 | 0.7324 |  59.85 KB |        2.36 |
