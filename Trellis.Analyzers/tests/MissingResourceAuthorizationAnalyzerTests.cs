namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

public class MissingResourceAuthorizationAnalyzerTests
{
    private const string AuthStubs = """
        namespace Mediator
        {
            public interface ICommand<out TResponse> { }
            public interface IQuery<out TResponse> { }
            public interface IRequest<out TResponse> { }
        }

        namespace Trellis.Authorization
        {
            public interface IAuthorizeResource<in TResource>
            {
                Trellis.IResult Authorize(object actor, TResource resource);
            }
        }

        namespace Trellis
        {
            public interface IResult { }

            public interface IScalarValue<TSelf, TPrimitive>
                where TSelf : IScalarValue<TSelf, TPrimitive>
                where TPrimitive : System.IComparable
            {
                static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null);
                TPrimitive Value { get; }
            }

            public readonly struct Result<T>
            {
                public bool IsSuccess { get; }
            }
        }
        """;

    private const string SampleIdType = """
        namespace TestNamespace
        {
            public readonly struct TodoId : Trellis.IScalarValue<TodoId, System.Guid>
            {
                public System.Guid Value { get; }
                public static Trellis.Result<TodoId> TryCreate(System.Guid value, string? fieldName = null) => default;
            }

            public readonly struct MatchId : Trellis.IScalarValue<MatchId, System.Guid>
            {
                public System.Guid Value { get; }
                public static Trellis.Result<MatchId> TryCreate(System.Guid value, string? fieldName = null) => default;
            }

            public class TodoItem { }
        }
        """;

    #region Should Warn

    [Fact]
    public async Task Command_with_typed_id_no_authorize_warns()
    {
        const string source = """
            namespace TestNamespace
            {
                public sealed record CompleteTodoCommand(TodoId TodoId)
                    : Mediator.ICommand<Trellis.Result<TodoItem>>;
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingResourceAuthorization)
                .WithLocation(3, 26)
                .WithArguments("CompleteTodoCommand", "'TodoId'"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Query_with_typed_id_no_authorize_warns()
    {
        const string source = """
            namespace TestNamespace
            {
                public sealed record GetTodoByIdQuery(TodoId TodoId)
                    : Mediator.IQuery<Trellis.Result<TodoItem>>;
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingResourceAuthorization)
                .WithLocation(3, 26)
                .WithArguments("GetTodoByIdQuery", "'TodoId'"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Command_with_multiple_typed_ids_no_authorize_lists_all()
    {
        const string source = """
            namespace TestNamespace
            {
                public sealed record TransferCommand(TodoId TodoId, MatchId MatchId)
                    : Mediator.ICommand<Trellis.Result<TodoItem>>;
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingResourceAuthorization)
                .WithLocation(3, 26)
                .WithArguments("TransferCommand", "'TodoId', 'MatchId'"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Command_with_id_property_not_positional_warns()
    {
        const string source = """
            namespace TestNamespace
            {
                public sealed record UpdateTodoCommand : Mediator.ICommand<Trellis.Result<TodoItem>>
                {
                    public TodoId TodoId { get; init; }
                }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingResourceAuthorization)
                .WithLocation(3, 26)
                .WithArguments("UpdateTodoCommand", "'TodoId'"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Has_IIdentifyResource_but_no_IAuthorizeResource_still_warns()
    {
        const string source = """
            namespace Trellis.Authorization
            {
                public interface IIdentifyResource<TResource, out TId>
                {
                    TId GetResourceId();
                }
            }

            namespace TestNamespace
            {
                public sealed record CompleteTodoCommand(TodoId TodoId)
                    : Mediator.ICommand<Trellis.Result<TodoItem>>,
                      Trellis.Authorization.IIdentifyResource<TodoItem, TodoId>
                {
                    public TodoId GetResourceId() => TodoId;
                }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingResourceAuthorization)
                .WithLocation(11, 26)
                .WithArguments("CompleteTodoCommand", "'TodoId'"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Inherited_typed_id_from_base_class_warns()
    {
        const string source = """
            namespace TestNamespace
            {
                public abstract record ResourceCommand(TodoId TodoId);

                public sealed record ArchiveTodoCommand(TodoId TodoId)
                    : ResourceCommand(TodoId), Mediator.ICommand<Trellis.Result<TodoItem>>;
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingResourceAuthorization)
                .WithLocation(5, 26)
                .WithArguments("ArchiveTodoCommand", "'TodoId'"));

        await test.RunAsync();
    }

    #endregion

    #region Should Not Warn

    [Fact]
    public async Task Command_with_IAuthorizeResource_no_warning()
    {
        const string source = """
            namespace TestNamespace
            {
                public sealed record CompleteTodoCommand(TodoId TodoId)
                    : Mediator.ICommand<Trellis.Result<TodoItem>>,
                      Trellis.Authorization.IAuthorizeResource<TodoItem>
                {
                    public Trellis.IResult Authorize(object actor, TodoItem resource) => default!;
                }
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Base_type_has_IAuthorizeResource_no_warning()
    {
        const string source = """
            namespace TestNamespace
            {
                public abstract record AuthorizedCommand(TodoId TodoId)
                    : Mediator.ICommand<Trellis.Result<TodoItem>>,
                      Trellis.Authorization.IAuthorizeResource<TodoItem>
                {
                    public Trellis.IResult Authorize(object actor, TodoItem resource) => default!;
                }

                public sealed record CompleteTodoCommand(TodoId TodoId)
                    : AuthorizedCommand(TodoId);
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Command_with_primitive_id_no_warning()
    {
        const string source = """
            namespace TestNamespace
            {
                public sealed record GetByIdQuery(System.Guid Id)
                    : Mediator.IQuery<Trellis.Result<TodoItem>>;
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Not_a_command_or_query_no_warning()
    {
        const string source = """
            namespace TestNamespace
            {
                public sealed record TodoDto(TodoId TodoId);
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Command_without_id_properties_no_warning()
    {
        const string source = """
            namespace TestNamespace
            {
                public sealed record CreateTodoCommand(string Title)
                    : Mediator.ICommand<Trellis.Result<TodoItem>>;
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Command_with_non_scalar_custom_type_no_warning()
    {
        const string source = """
            namespace TestNamespace
            {
                public class MyOptions { }
                public sealed record ProcessCommand(MyOptions Options)
                    : Mediator.ICommand<Trellis.Result<TodoItem>>;
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Command_with_static_id_property_no_warning()
    {
        const string source = """
            namespace TestNamespace
            {
                public sealed record StaticIdCommand : Mediator.ICommand<Trellis.Result<TodoItem>>
                {
                    public static TodoId DefaultTodoId { get; } = default;
                }
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    #endregion

    private static CSharpAnalyzerTest<MissingResourceAuthorizationAnalyzer, DefaultVerifier> CreateTest(string source)
    {
        var test = new CSharpAnalyzerTest<MissingResourceAuthorizationAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        test.TestState.Sources.Add(("AuthStubs.cs", AuthStubs));
        test.TestState.Sources.Add(("SampleIdType.cs", SampleIdType));
        return test;
    }
}
