namespace Trellis.Mediator.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public class ActorHandlerConstraintTests
{
    [Fact]
    public void HandlerConstraints_AllSixSealedOverrides_UseProtectedHandleAndRetainPublicInterfaceEntry()
    {
        var diagnostics = Compile("""
            public readonly record struct Command : ICommand<R>, IAuthorize
            {
                public IReadOnlyList<string> RequiredPermissions => [];
            }
            public readonly record struct Query : IQuery<R>, IAuthorize
            {
                public IReadOnlyList<string> RequiredPermissions => [];
            }
            public readonly record struct DirectCommand : ICommand<R>, IAuthorizeResource<Resource>
            {
                public IResult Authorize(Actor actor, Resource resource) => Result.Ok();
            }
            public readonly record struct DirectQuery : IQuery<R>, IAuthorizeResource<Resource>
            {
                public IResult Authorize(Actor actor, Resource resource) => Result.Ok();
            }
            public readonly record struct ViaCommand : ICommand<R>, IAuthorizeResourceVia<int>
            {
                public IResult Authorize(Actor actor, IReadOnlyList<int> owners) => Result.Ok();
            }
            public readonly record struct ViaQuery : IQuery<R>, IAuthorizeResourceVia<int>
            {
                public IResult Authorize(Actor actor, IReadOnlyList<int> owners) => Result.Ok();
            }
            public sealed class A : ActorCommandHandler<Command, R>
            {
                protected override ValueTask<R> Handle(Command command, Actor actor, CancellationToken token) =>
                    new(Result.Ok("handled"));
            }
            public sealed class B : ActorQueryHandler<Query, R>
            {
                protected override ValueTask<R> Handle(Query query, Actor actor, CancellationToken token) =>
                    new(Result.Ok("handled"));
            }
            public sealed class C : ActorResourceCommandHandler<DirectCommand, Resource, R>
            {
                protected override ValueTask<R> Handle(DirectCommand command, Actor actor, Resource resource, CancellationToken token) =>
                    new(Result.Ok("handled"));
            }
            public sealed class D : ActorResourceQueryHandler<DirectQuery, Resource, R>
            {
                protected override ValueTask<R> Handle(DirectQuery query, Actor actor, Resource resource, CancellationToken token) =>
                    new(Result.Ok("handled"));
            }
            public sealed class E : ActorResourceViaCommandHandler<ViaCommand, Resource, int, R>
            {
                protected override ValueTask<R> Handle(ViaCommand command, Actor actor, Resource leaf, CancellationToken token) =>
                    new(Result.Ok("handled"));
            }
            public sealed class F : ActorResourceViaQueryHandler<ViaQuery, Resource, int, R>
            {
                protected override ValueTask<R> Handle(ViaQuery query, Actor actor, Resource leaf, CancellationToken token) =>
                    new(Result.Ok("handled"));
            }
            public static class Entries
            {
                public static void Invoke()
                {
                    _ = new A().Handle(new Command(), CancellationToken.None);
                    _ = new B().Handle(new Query(), CancellationToken.None);
                    _ = new C().Handle(new DirectCommand(), CancellationToken.None);
                    _ = new D().Handle(new DirectQuery(), CancellationToken.None);
                    _ = new E().Handle(new ViaCommand(), CancellationToken.None);
                    _ = new F().Handle(new ViaQuery(), CancellationToken.None);
                    _ = ((ICommandHandler<Command, R>)new A()).Handle(new Command(), CancellationToken.None);
                    _ = ((IQueryHandler<Query, R>)new B()).Handle(new Query(), CancellationToken.None);
                }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ICommand<R>, IAuthorize", "ActorCommandHandler<Message, R>", "Actor actor", "actor")]
    [InlineData("IQuery<R>, IAuthorize", "ActorQueryHandler<Message, R>", "Actor actor", "actor")]
    [InlineData("ICommand<R>, IAuthorizeResource<Resource>", "ActorResourceCommandHandler<Message, Resource, R>", "Actor actor, Resource resource", "actor, resource")]
    [InlineData("IQuery<R>, IAuthorizeResource<Resource>", "ActorResourceQueryHandler<Message, Resource, R>", "Actor actor, Resource resource", "actor, resource")]
    [InlineData("ICommand<R>, IAuthorizeResourceVia<int>", "ActorResourceViaCommandHandler<Message, Resource, int, R>", "Actor actor, Resource leaf", "actor, leaf")]
    [InlineData("IQuery<R>, IAuthorizeResourceVia<int>", "ActorResourceViaQueryHandler<Message, Resource, int, R>", "Actor actor, Resource leaf", "actor, leaf")]
    public void Handle_ExplicitArguments_AreInaccessibleToConsumers(string capabilities, string baseType, string parameters, string arguments)
    {
        var diagnostics = Compile($$"""
            public record Message : {{capabilities}}
            {
                public IReadOnlyList<string> RequiredPermissions => [];
                public IResult Authorize(Actor actor, Resource resource) => Result.Ok();
                public IResult Authorize(Actor actor, IReadOnlyList<int> owners) => Result.Ok();
            }
            public abstract class Handler : {{baseType}};
            public static class Consumer
            {
                public static void Invoke(Handler handler, Message message, {{parameters}})
                {
                    _ = handler.Handle(message, {{arguments}}, CancellationToken.None);
                }
            }
            """);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("CS1501");
    }

    [Theory]
    [InlineData("public record Message : ICommand<R>; public abstract class H : ActorCommandHandler<Message, R>;", "CS0311")]
    [InlineData("public record Message : IQuery<R>; public abstract class H : ActorQueryHandler<Message, R>;", "CS0311")]
    [InlineData("""
        public record Message : ICommand<R>, IAuthorizeResource<OtherResource>
        {
            public IResult Authorize(Actor actor, OtherResource resource) => Result.Ok();
        }
        public abstract class H : ActorResourceCommandHandler<Message, Resource, R>;
        """, "CS0311")]
    [InlineData("""
        public record Message : IQuery<R>, IAuthorizeResource<OtherResource>
        {
            public IResult Authorize(Actor actor, OtherResource resource) => Result.Ok();
        }
        public abstract class H : ActorResourceQueryHandler<Message, Resource, R>;
        """, "CS0311")]
    [InlineData("""
        public record Message : ICommand<R>, IAuthorizeResourceVia<int>
        {
            public IResult Authorize(Actor actor, IReadOnlyList<int> owners) => Result.Ok();
        }
        public abstract class H : ActorResourceViaCommandHandler<Message, Resource, string, R>;
        """, "CS0311")]
    [InlineData("public record Message : ICommand<string>, IAuthorizationMessage; public abstract class H : ActorCommandHandler<Message, string>;", "CS0311")]
    [InlineData("""
        public record Message : ICommand<R>, IAuthorizeResource<int>
        {
            public IResult Authorize(Actor actor, int resource) => Result.Ok();
        }
        public abstract class H : ActorResourceCommandHandler<Message, int, R>;
        """, "CS0452")]
    public void HandlerConstraints_IncompatibleCapabilities_AreRejectedByCompiler(string source, string diagnosticId)
        => Compile(source).Should().Contain(diagnostic => diagnostic.Id == diagnosticId);

    private static Diagnostic[] Compile(string source)
    {
        var preamble = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Mediator;
            using Trellis;
            using Trellis.Authorization;
            using Trellis.Mediator;
            using R = Trellis.Result<string>;
            public class Resource;
            public class OtherResource;

            """;
        var assemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("The managed compiler tests need trusted platform assembly paths.");
        var references = assemblies.Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("HandlerConstraintProbe",
            [CSharpSyntaxTree.ParseText(preamble + source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
    }
}
