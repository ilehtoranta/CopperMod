using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CopperMod.PerformanceAnalyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HotPathAllocationAnalyzer : DiagnosticAnalyzer
{
    public const string AllocationDiagnosticId = "CMPERF001";
    public const string AllocatingApiDiagnosticId = "CMPERF002";
    public const string StateMachineDiagnosticId = "CMPERF003";
    public const string AllowReasonDiagnosticId = "CMPERF004";
    public const string ColdCallDiagnosticId = "CMPERF005";

    private static readonly DiagnosticDescriptor AllocationRule = new(
        AllocationDiagnosticId,
        "Hot path contains an allocation",
        "Hot path allocation: {0}",
        "Performance",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AllocatingApiRule = new(
        AllocatingApiDiagnosticId,
        "Hot path calls an allocating API",
        "Hot path allocating API call: {0}",
        "Performance",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor StateMachineRule = new(
        StateMachineDiagnosticId,
        "Hot path creates a compiler state machine",
        "Hot path state machine allocation: {0}",
        "Performance",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AllowReasonRule = new(
        AllowReasonDiagnosticId,
        "Allocation allowance requires a reason",
        "HotPathAllocationAllowed requires a non-empty reason",
        "Performance",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ColdCallRule = new(
        ColdCallDiagnosticId,
        "Hot path calls a cold allocation-allowed member",
        "Hot path calls allocation-allowed member: {0}",
        "Performance",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(AllocationRule, AllocatingApiRule, StateMachineRule, AllowReasonRule, ColdCallRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(
            AnalyzeAccessor,
            SyntaxKind.GetAccessorDeclaration,
            SyntaxKind.SetAccessorDeclaration,
            SyntaxKind.InitAccessorDeclaration,
            SyntaxKind.AddAccessorDeclaration,
            SyntaxKind.RemoveAccessorDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeAllocationAllowance, SyntaxKind.Attribute);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var declaration = (BaseMethodDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        if (symbol == null || !IsHotPath(symbol) || IsAllocationAllowed(symbol))
        {
            return;
        }

        if (declaration is MethodDeclarationSyntax method)
        {
            if (method.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                Report(context, StateMachineRule, method.Identifier.GetLocation(), "async method");
            }

            if (method.ExpressionBody != null)
            {
                AnalyzeNodeTree(context, method.ExpressionBody.Expression);
            }
        }

        if (declaration.Body != null)
        {
            AnalyzeNodeTree(context, declaration.Body);
        }
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken);
        if (symbol == null || !IsHotPath(symbol) || IsAllocationAllowed(symbol))
        {
            return;
        }

        if (property.ExpressionBody != null)
        {
            AnalyzeNodeTree(context, property.ExpressionBody.Expression);
        }
    }

    private static void AnalyzeAccessor(SyntaxNodeAnalysisContext context)
    {
        var accessor = (AccessorDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(accessor, context.CancellationToken);
        if (symbol == null || !IsHotPath(symbol) || IsAllocationAllowed(symbol))
        {
            return;
        }

        if (accessor.ExpressionBody != null)
        {
            AnalyzeNodeTree(context, accessor.ExpressionBody.Expression);
        }

        if (accessor.Body != null)
        {
            AnalyzeNodeTree(context, accessor.Body);
        }
    }

    private static void AnalyzeNodeTree(SyntaxNodeAnalysisContext context, SyntaxNode root)
    {
        foreach (var node in root.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case ObjectCreationExpressionSyntax objectCreation:
                    AnalyzeObjectCreation(context, objectCreation);
                    break;
                case ImplicitObjectCreationExpressionSyntax objectCreation:
                    AnalyzeObjectCreation(context, objectCreation);
                    break;
                case ArrayCreationExpressionSyntax arrayCreation:
                    Report(context, AllocationRule, arrayCreation.GetLocation(), "array creation");
                    break;
                case ImplicitArrayCreationExpressionSyntax arrayCreation:
                    Report(context, AllocationRule, arrayCreation.GetLocation(), "array creation");
                    break;
                case CollectionExpressionSyntax collectionExpression:
                    Report(context, AllocationRule, collectionExpression.GetLocation(), "collection expression");
                    break;
                case AnonymousObjectCreationExpressionSyntax anonymousObject:
                    Report(context, AllocationRule, anonymousObject.GetLocation(), "anonymous object creation");
                    break;
                case SimpleLambdaExpressionSyntax lambda:
                    Report(context, AllocationRule, lambda.GetLocation(), "lambda/delegate creation");
                    break;
                case ParenthesizedLambdaExpressionSyntax lambda:
                    Report(context, AllocationRule, lambda.GetLocation(), "lambda/delegate creation");
                    break;
                case AnonymousMethodExpressionSyntax anonymousMethod:
                    Report(context, AllocationRule, anonymousMethod.GetLocation(), "anonymous delegate creation");
                    break;
                case InterpolatedStringExpressionSyntax interpolatedString:
                    Report(context, AllocationRule, interpolatedString.GetLocation(), "interpolated string");
                    break;
                case YieldStatementSyntax yieldStatement:
                    Report(context, StateMachineRule, yieldStatement.GetLocation(), "iterator block");
                    break;
                case InvocationExpressionSyntax invocation:
                    AnalyzeInvocation(context, invocation);
                    break;
                case ExpressionSyntax expression:
                    AnalyzeBoxing(context, expression);
                    break;
            }
        }
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context, BaseObjectCreationExpressionSyntax node)
    {
        if (node.FirstAncestorOrSelf<ThrowStatementSyntax>() != null)
        {
            return;
        }

        if (node.FirstAncestorOrSelf<ThrowExpressionSyntax>() != null)
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(node, context.CancellationToken).Type;
        if (type?.IsValueType == false)
        {
            var display = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            Report(context, AllocationRule, node.GetLocation(), "new " + display);
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax node)
    {
        if (context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (IsAllocationAllowed(method))
        {
            Report(context, ColdCallRule, node.GetLocation(), method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            return;
        }

        if (IsKnownAllocatingApi(method))
        {
            Report(context, AllocatingApiRule, node.GetLocation(), method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        }
    }

    private static void AnalyzeBoxing(SyntaxNodeAnalysisContext context, ExpressionSyntax node)
    {
        if (node.FirstAncestorOrSelf<ThrowStatementSyntax>() != null ||
            node.FirstAncestorOrSelf<ThrowExpressionSyntax>() != null)
        {
            return;
        }

        if (node is LiteralExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax)
        {
            var conversion = context.SemanticModel.GetConversion(node, context.CancellationToken);
            if (conversion.IsBoxing)
            {
                Report(context, AllocationRule, node.GetLocation(), "boxing conversion");
            }
        }
    }

    private static void AnalyzeAllocationAllowance(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol;
        if (symbol?.ContainingType == null || !IsAttributeNamed(symbol.ContainingType, "HotPathAllocationAllowedAttribute"))
        {
            return;
        }

        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();
        var reason = argument == null
            ? null
            : context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken);
        if (reason.HasValue && reason.Value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Report(context, AllowReasonRule, attribute.GetLocation());
    }

    private static bool IsHotPath(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { AssociatedSymbol: { } associatedSymbol } &&
            HasAttribute(associatedSymbol, "HotPathAttribute"))
        {
            return true;
        }

        for (var current = symbol; current != null; current = current.ContainingType)
        {
            if (HasAttribute(current, "HotPathAttribute"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllocationAllowed(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { AssociatedSymbol: { } associatedSymbol } &&
            HasAttribute(associatedSymbol, "HotPathAllocationAllowedAttribute"))
        {
            return true;
        }

        return HasAttribute(symbol, "HotPathAllocationAllowedAttribute");
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
        => symbol.GetAttributes().Any(attribute => IsAttributeNamed(attribute.AttributeClass, metadataName));

    private static bool IsAttributeNamed(INamedTypeSymbol? symbol, string metadataName)
        => symbol != null &&
            (symbol.MetadataName == metadataName || symbol.Name == metadataName || symbol.Name == metadataName.Replace("Attribute", string.Empty));

    private static bool IsKnownAllocatingApi(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        var containingName = containingType?.ToDisplayString() ?? string.Empty;
        if (containingName == "System.Linq.Enumerable")
        {
            return true;
        }

        if (method.Name is "ToArray" or "ToList")
        {
            return true;
        }

        if (containingName == "System.String" && method.Name is "Format" or "Join")
        {
            return true;
        }

        if (containingName == "System.Text.StringBuilder")
        {
            return true;
        }

        return false;
    }

    private static void Report(SyntaxNodeAnalysisContext context, DiagnosticDescriptor descriptor, Location location, params object[] messageArgs)
        => context.ReportDiagnostic(Diagnostic.Create(descriptor, location, messageArgs));
}
