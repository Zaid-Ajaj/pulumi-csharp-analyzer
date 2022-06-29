using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace PulumiCSharpAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class PulumiAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor MissingRequiredPropertyRule = new DiagnosticDescriptor(
            id: "MissingRequiredProperty", 
            title: "Missing required property", 
            messageFormat: "Missing required {0} when initializing properties of type {1}", 
            category: "Usage", 
            defaultSeverity: DiagnosticSeverity.Warning, 
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ResourceCreatedInsideApplyRule = new DiagnosticDescriptor(
            id: "ResourceCreatedInsideApply",
            title: "Resource created inside Apply",
            messageFormat: "Resource {0} created from inside Output.Apply(...) potentially would not show up during pulumi preview phase depending on whether or not the value of the output instance is known",
            category: "Usage",
            defaultSeverity:DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );
        
        private static readonly DiagnosticDescriptor ApplyResultDiscardedRule = new DiagnosticDescriptor(
            id: "ApplyResultDiscarded",
            title: "The result of the Apply transform was ignored",
            messageFormat: "The result of Output.Apply(...) is discarded. Consider assigning the function call to a variable",
            category: "Usage",
            defaultSeverity:DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );
        
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            MissingRequiredPropertyRule, 
            ResourceCreatedInsideApplyRule,
            ApplyResultDiscardedRule
        );

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
            context.RegisterOperationAction(AnalyzeOutputApplyCall, OperationKind.Invocation);
        }

        private void AnalyzeOutputApplyCall(OperationAnalysisContext context)
        {
            var operation = (IInvocationOperation)context.Operation;
            var targetMethod = operation.TargetMethod;
            if (targetMethod.Name == "Apply")
            {
                if (targetMethod.ReceiverType != null && targetMethod.ReceiverType.Name.EndsWith("Output"))
                {
                    foreach (var childOperation in operation.Descendants())
                    {
                        if (childOperation is IObjectCreationOperation objectCreationOperation)
                        {
                            if (IsResourceType(objectCreationOperation.Type))
                            {
                                var diagnostic = Diagnostic.Create(
                                    descriptor: ResourceCreatedInsideApplyRule,
                                    location: objectCreationOperation.Syntax.GetLocation(),
                                    messageArgs: new object[] { objectCreationOperation.Type?.Name });
                                
                                context.ReportDiagnostic(diagnostic);
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        ///  Detects resource argument creation and reports required properties that are not defined.
        /// </summary>
        /// <param name="context">The analysis context</param>
        private void AnalyzeObjectCreation(OperationAnalysisContext context)
        {
            var operation = (IObjectCreationOperation)context.Operation;
            if (operation.Type != null)
            {
                var typeName = operation.Type.Name;
                if (operation.Type.BaseType != null)
                {
                    var baseTypeName = operation.Type.BaseType.Name;
                    if (baseTypeName.EndsWith("ResourceArgs") || baseTypeName.EndsWith("InvokeArgs"))
                    {
                        var assignedProperties = GetAssignedProperties(operation);
                        var requiredProperties = GetRequiredProperties(operation.Type);
                        var missingRequiredProperties = new List<string>();
                        foreach (var requiredProperty in requiredProperties)
                        {
                            if (!assignedProperties.Contains(requiredProperty) 
                                && !missingRequiredProperties.Contains(requiredProperty))
                            {
                                missingRequiredProperties.Add(requiredProperty);
                            }
                        }

                        if (missingRequiredProperties.Any())
                        {
                            if (missingRequiredProperties.Count == 1)
                            {
                                var diagnostic = Diagnostic.Create(
                                    descriptor: MissingRequiredPropertyRule,
                                    location: operation.Syntax.GetLocation(),
                                    $"property {missingRequiredProperties[0]}",
                                    typeName);

                                context.ReportDiagnostic(diagnostic);
                            }
                            else
                            {
                                var last = missingRequiredProperties.Last();
                                var allButLast = missingRequiredProperties.TakeWhile(prop => prop != last);
                                var joinedFirstProperties = string.Join(", ", allButLast);
                                var diagnostic = Diagnostic.Create(
                                    descriptor: MissingRequiredPropertyRule,
                                    location: operation.Syntax.GetLocation(),
                                    $"properties {joinedFirstProperties} and {last}",
                                    typeName);

                                context.ReportDiagnostic(diagnostic);
                            }
                        }
                    }
                }
            }
        }

        bool IsResourceType(ITypeSymbol typeSymbol)
        {
            if (typeSymbol?.BaseType == null)
            {
                return false;
            }

            return typeSymbol.BaseType.Name.EndsWith("CustomResource");
        }
        
        private List<string> GetAssignedProperties(IObjectCreationOperation objectCreation)
        {
            var result = new List<string>();
            if (objectCreation.Initializer != null)
            {
                foreach (var initializer in objectCreation.Initializer.Initializers)
                {
                    if (initializer is ISimpleAssignmentOperation assignmentOperation &&
                        assignmentOperation.Target is IPropertyReferenceOperation propertyReference)
                    {
                        result.Add(propertyReference.Property.Name);
                    }
                }
            }

            return result;
        }

        private List<string> GetRequiredProperties(ITypeSymbol type)
        {
            var properties = new List<string>();

            // Walk all the properties of this args type looking for those that have a
            // `[Input("name", required: true)]` arg.
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol property)
                {
                    var attributes = property.GetAttributes();
                    foreach (var attribute in attributes)
                    {
                        var args = attribute.ConstructorArguments;
                        if (args.Length >= 2 && args[1].Value is true)
                        {
                            properties.Add(member.Name);
                        }
                    }
                }
            }

            return properties.Distinct().ToList();
        }
    }
}
