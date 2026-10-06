namespace Trellis.Mediator.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public class ActorHandlerConstraintTests
{
    [Fact]
    public void HandlerConstraints_AllSixBases_PreserveResourceOnlyStructMessageAndValueOwnerShapes()
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
            public abstract class A : ActorCommandHandler<Command, R>;
            public abstract class B : ActorQueryHandler<Query, R>;
            public abstract class C : ActorResourceCommandHandler<DirectCommand, Resource, R>;
            public abstract class D : ActorResourceQueryHandler<DirectQuery, Resource, R>;
            public abstract class E : ActorResourceViaCommandHandler<ViaCommand, Resource, int, R>;
            public abstract class F : ActorResourceViaQueryHandler<ViaQuery, Resource, int, R>;
            """);

        diagnostics.Should().BeEmpty();
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
