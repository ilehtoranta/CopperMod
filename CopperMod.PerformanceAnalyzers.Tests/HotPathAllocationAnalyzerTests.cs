using System.Collections.Immutable;
using CopperMod.PerformanceAnalyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CopperMod.PerformanceAnalyzers.Tests;

public sealed class HotPathAllocationAnalyzerTests
{
    [Fact]
    public async Task ReportsCommonAllocationsInHotPath()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;
            using System.Linq;
            using System.Text;

            namespace TestCode;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
            internal sealed class HotPathAttribute : Attribute { }

            [HotPath]
            internal sealed class Runtime
            {
                private readonly int[] _values = new int[8];

                public void Frame()
                {
                    var a = new object();
                    var b = new int[4];
                    Func<int, int> c = value => value + _values[0];
                    var d = $"frame {a}";
                    var e = _values.Where(value => value != 0).ToArray();
                    var f = new StringBuilder();
                    object g = 1;
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == HotPathAllocationAnalyzer.AllocationDiagnosticId);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == HotPathAllocationAnalyzer.AllocatingApiDiagnosticId);
    }

    [Fact]
    public async Task AllowsStackallocSpansAndValueTypesInHotPath()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;

            namespace TestCode;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
            internal sealed class HotPathAttribute : Attribute { }

            internal readonly struct Sample
            {
                public Sample(int value) => Value = value;
                public int Value { get; }
            }

            [HotPath]
            internal sealed class Runtime
            {
                public int Frame()
                {
                    Span<int> scratch = stackalloc int[4];
                    var sample = new Sample(2);
                    scratch[0] = sample.Value;
                    return scratch[0];
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsAsyncAndIteratorStateMachinesInHotPath()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace TestCode;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
            internal sealed class HotPathAttribute : Attribute { }

            internal sealed class Runtime
            {
                [HotPath]
                public async Task FrameAsync()
                {
                    await Task.Yield();
                }

                [HotPath]
                public IEnumerable<int> Frames()
                {
                    yield return 1;
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == HotPathAllocationAnalyzer.StateMachineDiagnosticId);
    }

    [Fact]
    public async Task ReportsTargetTypedNewAndCollectionExpressionsInHotPath()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;
            using System.Collections.Generic;

            namespace TestCode;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
            internal sealed class HotPathAttribute : Attribute { }

            [HotPath]
            internal sealed class Runtime
            {
                public void Frame()
                {
                    List<int> values = new();
                    int[] copy = [1, 2, 3];
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == HotPathAllocationAnalyzer.AllocationDiagnosticId &&
            diagnostic.GetMessage().Contains("new List<int>", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == HotPathAllocationAnalyzer.AllocationDiagnosticId &&
            diagnostic.GetMessage().Contains("collection expression", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsAllocationsInHotPathPropertiesAndAccessors()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;

            namespace TestCode;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property)]
            internal sealed class HotPathAttribute : Attribute { }

            [HotPath]
            internal sealed class Runtime
            {
                public object ExpressionProperty => new object();

                public object AccessorProperty
                {
                    get
                    {
                        return new object();
                    }
                }
            }
            """);

        Assert.True(
            diagnostics.Count(diagnostic => diagnostic.Id == HotPathAllocationAnalyzer.AllocationDiagnosticId) >= 2,
            "Expected both expression-bodied properties and accessor bodies to be analyzed.");
    }

    [Fact]
    public async Task RequiresAllocationAllowanceReason()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;

            namespace TestCode;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
            internal sealed class HotPathAllocationAllowedAttribute : Attribute
            {
                public HotPathAllocationAllowedAttribute(string reason) { }
            }

            internal sealed class Runtime
            {
                [HotPathAllocationAllowed("")]
                public void Snapshot()
                {
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == HotPathAllocationAnalyzer.AllowReasonDiagnosticId);
    }

    [Fact]
    public async Task ReportsHotPathCallsIntoAllocationAllowedMembers()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;

            namespace TestCode;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
            internal sealed class HotPathAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor)]
            internal sealed class HotPathAllocationAllowedAttribute : Attribute
            {
                public HotPathAllocationAllowedAttribute(string reason) { }
            }

            internal sealed class Runtime
            {
                [HotPath]
                public void Frame()
                {
                    Snapshot();
                }

                [HotPathAllocationAllowed("debug snapshot allocates copied state")]
                public void Snapshot()
                {
                    _ = new object();
                }
            }
            """);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == HotPathAllocationAnalyzer.ColdCallDiagnosticId);
    }

    [Fact]
    public async Task ReportsExcessiveBranchesInHotPathMethod()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;

            namespace TestCode;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property)]
            internal sealed class HotPathAttribute : Attribute
            {
                public int MaxBranches { get; set; }
            }

            internal sealed class Runtime
            {
                [HotPath(MaxBranches = 2)]
                public int OverBudget(int a, int b, int c)
                {
                    if (a > 0) return a;
                    if (b > 0) return b;
                    if (c > 0) return c;
                    return 0;
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics.Where(d => d.Id == HotPathAllocationAnalyzer.BranchComplexityDiagnosticId));
        Assert.Contains("3 branch points", diagnostic.GetMessage());
        Assert.Contains("MaxBranches=2", diagnostic.GetMessage());
    }

    [Fact]
    public async Task AllowsMethodWithinBranchBudget()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;

            namespace TestCode;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property)]
            internal sealed class HotPathAttribute : Attribute
            {
                public int MaxBranches { get; set; }
            }

            internal sealed class Runtime
            {
                [HotPath(MaxBranches = 3)]
                public int WithinBudget(int a, int b)
                {
                    if (a > 0) return a;
                    if (b > 0) return b;
                    return 0;
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id == HotPathAllocationAnalyzer.BranchComplexityDiagnosticId);
    }

    private static async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var analyzer = new HotPathAllocationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
