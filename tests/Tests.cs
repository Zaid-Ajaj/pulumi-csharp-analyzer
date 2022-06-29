using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PulumiCSharpAnalyzer.Test
{
    [TestClass]
    public class Tests
    {
        [TestMethod]
        public async Task BasicVerificationWorks_NoDiagnostics()
        {
            var tester = new CSharpAnalyzerVerifier<PulumiAnalyzer>.Test();
            tester.TestCode = @"
using System;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InputAttribute : Attribute
{
    internal string Name { get; }
    internal bool IsRequired { get; }
    internal bool Json { get; }

    public InputAttribute(string name, bool required = false, bool json = false)
    {
        Name = name;
        IsRequired = required;
        Json = json;
    }
}

class Output<T> 
{
    public Output<U> Apply<U>(Func<T, U> map) => null;
}

class Output
{
    public static Output<T> Create<T>(T value) => null;
}

class ResourceArgs 
{
    
}

class CustomResource
{
}

class StorageAccountArgs : ResourceArgs
{
    [Input(""resourceName"", true, false)]
    public string ResourceName { get; set; }

    [Input(""version"", false, false)]
    public int Version { get; set; }
}

class StorageAccount : CustomResource
{
    
}

class Program
{
    static void Main()
    {
        var args = new StorageAccountArgs
        {
            ResourceName = ""fooBar"",
            Version = 42
        };

        var output = Output.Create(42);
        output.Apply(value => 
        {
            return 1;
        });
    }
}
";
            await tester.RunAsync(CancellationToken.None);
        }
        
        [TestMethod]
        public async Task BasicVerificationWorks_MissingRequiredProperty()
        {
            var tester = new CSharpAnalyzerVerifier<PulumiAnalyzer>.Test();
            tester.TestCode = @"
using System;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InputAttribute : Attribute
{
    internal string Name { get; }
    internal bool IsRequired { get; }
    internal bool Json { get; }

    public InputAttribute(string name, bool required = false, bool json = false)
    {
        Name = name;
        IsRequired = required;
        Json = json;
    }
}

class ResourceArgs 
{
    
}

class CustomResource
{
}

class StorageAccountArgs : ResourceArgs
{
    [Input(""resourceName"", true, false)]
    public string ResourceName { get; set; }

    [Input(""version"", false, false)]
    public int Version { get; set; }
}

class StorageAccount : CustomResource
{
    
}

class Program
{
    static void Main()
    {
        var args = new StorageAccountArgs
        {
            Version = 42
        };
    }
}
";
            
            var diagnosticResult =
                DiagnosticResult
                    .CompilerWarning("MissingRequiredProperty")
                    .WithArguments("property ResourceName", "StorageAccountArgs")
                    .WithSpan(46, 20, 49, 10)
                    .WithMessage("Missing required property ResourceName when initializing properties of type StorageAccountArgs");
            
            tester.ExpectedDiagnostics.Add(diagnosticResult);
            await tester.RunAsync(CancellationToken.None);
        }
        
        [TestMethod]
        public async Task BasicVerificationWorks_MissingMultipleRequiredProperty()
        {
            var tester = new CSharpAnalyzerVerifier<PulumiAnalyzer>.Test();
            tester.TestCode = @"
using System;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InputAttribute : Attribute
{
    internal string Name { get; }
    internal bool IsRequired { get; }
    internal bool Json { get; }

    public InputAttribute(string name, bool required = false, bool json = false)
    {
        Name = name;
        IsRequired = required;
        Json = json;
    }
}

class ResourceArgs 
{
    
}

class CustomResource
{
}

class StorageAccountArgs : ResourceArgs
{
    [Input(""resourceName"", true, false)]
    public string ResourceName { get; set; }

    [Input(""version"", true, false)]
    public int Version { get; set; }
}

class StorageAccount : CustomResource
{
    
}

class Program
{
    static void Main()
    {
        var args = new StorageAccountArgs
        {

        };
    }
}
";
            var diagnosticResult =
                DiagnosticResult
                    .CompilerWarning("MissingRequiredProperty")
                    .WithArguments("properties ResourceName and Version", "StorageAccountArgs")
                    .WithSpan(46, 20, 49, 10)
                    .WithMessage("Missing required properties ResourceName and Version when initializing properties of type StorageAccountArgs");
            
            tester.ExpectedDiagnostics.Add(diagnosticResult);
            await tester.RunAsync(CancellationToken.None);
        }
        
        [TestMethod]
        public async Task BasicVerificationWorks_ResourceCreatedInsideApply()
        {
            var tester = new CSharpAnalyzerVerifier<PulumiAnalyzer>.Test();
            tester.TestCode = @"
using System;

class Output<T> 
{
    public Output<U> Apply<U>(Func<T, U> map) => null;
}

class Output
{
    public static Output<T> Create<T>(T value) => null;
}

class ResourceArgs 
{
    
}

class CustomResource
{
}

class StorageAccount : CustomResource
{
    
}

class Program
{
    static void Main()
    {
        Output.Create(42).Apply(value => 
        {
            return new StorageAccount { };
        });
    }
}
";
            var diagnosticResult =
                DiagnosticResult
                    .CompilerWarning("ResourceCreatedInsideApply")
                    .WithArguments("StorageAccount")
                    .WithSpan(34, 20, 34, 42)
                    .WithMessage("Resource StorageAccount created from inside Output.Apply(...) potentially would not show up during pulumi preview phase depending on whether or not the value of the output instance is known");
            
            tester.ExpectedDiagnostics.Add(diagnosticResult);
            await tester.RunAsync(CancellationToken.None);
        }
        
        [TestMethod]
        public async Task BasicVerificationWorks_MissingPropertyInsideFunctionInvokeArgs()
        {
            var tester = new CSharpAnalyzerVerifier<PulumiAnalyzer>.Test();
            tester.TestCode = @"
using System;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InputAttribute : Attribute
{
    internal string Name { get; }
    internal bool IsRequired { get; }
    internal bool Json { get; }

    public InputAttribute(string name, bool required = false, bool json = false)
    {
        Name = name;
        IsRequired = required;
        Json = json;
    }
}

class Output<T> 
{
    public Output<U> Apply<U>(Func<T, U> map) => null;
}

class Output
{
    public static Output<T> Create<T>(T value) => null;
}

class InvokeArgs 
{
    
}


class GetStorageAccountArgs : InvokeArgs
{
    [Input(""resourceName"", true, false)]
    public string ResourceName { get; set; }
}

class Program
{
    static void Main()
    {
        // missing ResourceName property initializer
        new GetStorageAccountArgs { };
    }
}
";
            var diagnosticResult =
                DiagnosticResult
                    .CompilerWarning("MissingRequiredProperty")
                    .WithArguments("property ResourceName", "GetStorageAccountArgs")
                    .WithSpan(46, 9, 46, 38)
                    .WithMessage("Missing required property ResourceName when initializing properties of type GetStorageAccountArgs");
            
            tester.ExpectedDiagnostics.Add(diagnosticResult);
            await tester.RunAsync(CancellationToken.None);
        }
    }
}
